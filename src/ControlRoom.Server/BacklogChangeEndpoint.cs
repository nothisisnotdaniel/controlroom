using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ControlRoom.Server;

/// <summary>Serializes backlog writes: one change at a time per server.</summary>
internal sealed class BacklogWriteLock
{
    public SemaphoreSlim Gate { get; } = new(1, 1);
}

/// <summary>A checked <c>POST /api/backlog/change</c> body. Fields an action does not use are <c>null</c>.</summary>
internal sealed record BacklogChangeRequest(
    string Project,
    string Version,
    string Action,
    int? Line,
    AddPosition? Where,
    string? Title,
    string? What,
    string? Why,
    string? Story,
    string? StoryVersion,
    string? Markdown,
    string? Note = null);

/// <summary>The <c>200</c> response of a change.</summary>
internal sealed record BacklogChangeResponse(
    bool Committed,
    bool Pushed,
    string Version,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Warning);

/// <summary>One file a change writes: the path relative to the project (also the git path), the full path, the new
/// bytes (<c>null</c> deletes it) and the bytes before (<c>null</c> when it did not exist).</summary>
internal sealed record FileChange(string RelativePath, string FullPath, byte[]? NewBytes, byte[]? OldBytes);

/// <summary>What a change writes and commits, or the refusal (<c>Error</c> with its status).</summary>
internal sealed record ChangePlan(
    IReadOnlyList<FileChange> Files,
    string Message,
    byte[] BacklogBytes,
    int Status = StatusCodes.Status200OK,
    string? Error = null)
{
    public static ChangePlan Refused(int status, string error)
    {
        return new ChangePlan([], "", [], status, error);
    }
}

/// <summary><c>POST /api/backlog/change</c>: one checked change of a project's <c>backlog.md</c> (and its story
/// files), committed on <c>develop</c> and pushed when there is an <c>origin</c>. The project is matched against
/// <see cref="ProjectFinder"/>; every path comes from the backlog line, never from the request.</summary>
internal static class BacklogChangeEndpoint
{
    public const string Route = "/api/backlog/change";

    public const int MaxBodyBytes = 64 * 1024;

    public const int MaxStoryBytes = 64 * 1024;

    public const string Invalid = "invalid";

    public const string NotDevelop = "not_develop";

    public const string Dirty = "dirty";

    public const string Changed = "changed";

    public const string CommitFailed = "commit_failed";

    public const string WriteFailed = "write_failed";

    public const string PushFailed = "push_failed";

    private static readonly HashSet<string> Fields = new(StringComparer.Ordinal)
    {
        "project", "version", "action", "line", "where", "title", "what", "why", "story", "storyVersion", "markdown", "note",
    };

    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static void Map(WebApplication app)
    {
        app.MapPost(Route, async (FactoryRoot root, BacklogWriteLock writeLock, TimeProvider time, HttpContext context) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            if (!IsJson(context.Request.ContentType))
            {
                return Error(StatusCodes.Status415UnsupportedMediaType, Invalid);
            }

            byte[]? body = await ReadBody(context.Request);
            BacklogChangeRequest? request = body is null ? null : ParseRequest(body);
            ProjectFolder? project = request is null
                ? null
                : ProjectFinder.Find(root.Path)
                    .FirstOrDefault(folder => string.Equals(folder.Name, request.Project, StringComparison.Ordinal));
            if (request is null || project is null)
            {
                return Error(StatusCodes.Status400BadRequest, Invalid);
            }

            await writeLock.Gate.WaitAsync(context.RequestAborted);
            try
            {
                return await Change(project, request, DateOnly.FromDateTime(time.GetLocalNow().DateTime));
            }
            finally
            {
                writeLock.Gate.Release();
            }
        });
    }

    private static async Task<IResult> Change(ProjectFolder project, BacklogChangeRequest request, DateOnly today)
    {
        if (await ProjectGit.CurrentBranch(project.Path) != ProjectGit.Branch)
        {
            return Error(StatusCodes.Status409Conflict, NotDevelop);
        }

        if (!await ProjectGit.BacklogIsClean(project.Path))
        {
            return Error(StatusCodes.Status409Conflict, Dirty);
        }

        BacklogFile? file = BacklogReader.ReadFile(project);
        if (file is null)
        {
            return Error(StatusCodes.Status400BadRequest, Invalid);
        }

        if (!string.Equals(file.Version, request.Version, StringComparison.Ordinal))
        {
            return Error(StatusCodes.Status409Conflict, Changed);
        }

        EditableText? text = EditableText.Parse(file.Bytes);
        if (text is null)
        {
            return Error(StatusCodes.Status400BadRequest, Invalid);
        }

        ChangePlan plan = Plan(project, request, text, file, today);
        if (plan.Error is not null)
        {
            return Error(plan.Status, plan.Error);
        }

        List<FileChange> applied = [];
        string[] paths = plan.Files.Select(change => change.RelativePath).ToArray();
        bool createdFolder = false;
        try
        {
            string storyFolder = Path.Combine(project.Path, StoryReader.FolderName);
            if (plan.Files.Any(change => change.NewBytes is not null && change.RelativePath != BacklogReader.FileName)
                && !Directory.Exists(storyFolder))
            {
                Directory.CreateDirectory(storyFolder);
                createdFolder = true;
            }

            foreach (FileChange change in plan.Files)
            {
                applied.Add(change);
                Apply(change.FullPath, change.NewBytes);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Restore(applied, project, createdFolder);
            return Error(StatusCodes.Status500InternalServerError, WriteFailed);
        }
        catch
        {
            // Any other failure still puts the files back before it surfaces.
            Restore(applied, project, createdFolder);
            throw;
        }

        if (!await ProjectGit.Commit(project.Path, plan.Message, paths))
        {
            Restore(applied, project, createdFolder);
            await ProjectGit.Unstage(project.Path, paths);
            return Error(StatusCodes.Status500InternalServerError, CommitFailed);
        }

        bool pushed = false;
        string? warning = null;
        if (await ProjectGit.HasOrigin(project.Path))
        {
            pushed = await ProjectGit.Push(project.Path);
            warning = pushed ? null : PushFailed;
        }

        // Every plan carries the backlog bytes after the change (a story edit passes the unchanged file bytes), so this
        // is the version the client sends next.
        return Results.Json(new BacklogChangeResponse(true, pushed, SafeFileReader.Version(plan.BacklogBytes), warning));
    }

    /// <summary>The files and commit message of a change, or its refusal. Nothing is written here.
    /// <paramref name="today"/> is the local date a note is dated with.</summary>
    public static ChangePlan Plan(
        ProjectFolder project, BacklogChangeRequest request, EditableText text, BacklogFile file, DateOnly today)
    {
        int line = request.Line ?? -1;
        string backlogPath = Path.Combine(project.Path, BacklogReader.FileName);
        switch (request.Action)
        {
            case "approve":
            {
                BacklogEdit? edit = BacklogEditor.Approve(text, line);
                return edit is null ? InvalidPlan() : BacklogOnly(edit, "approve", backlogPath, file);
            }

            case "done":
            {
                BacklogEdit? edit = BacklogEditor.Done(text, line);
                return edit is null ? InvalidPlan() : BacklogOnly(edit, "done", backlogPath, file);
            }

            case "up":
            case "down":
            case "top":
            {
                MoveDirection direction = request.Action switch
                {
                    "up" => MoveDirection.Up,
                    "down" => MoveDirection.Down,
                    _ => MoveDirection.Top,
                };
                BacklogEdit? edit = BacklogEditor.Move(text, line, direction);
                string verb = direction == MoveDirection.Top ? "to top" : request.Action;
                return edit is null ? InvalidPlan() : BacklogOnly(edit, "move", backlogPath, file, " " + verb);
            }

            case "drop":
                return PlanDrop(project, text, line, backlogPath, file);
            case "add":
                return PlanAdd(project, request, text, backlogPath, file);
            case "title":
                return PlanTitle(project, request, text, line, backlogPath, file);
            case "story":
                return PlanStory(project, request, text, line, file);
            case "note":
                return PlanNote(project, request, text, line, file, today);
            default:
                return InvalidPlan();
        }
    }

    private static ChangePlan PlanDrop(
        ProjectFolder project, EditableText text, int line, string backlogPath, BacklogFile file)
    {
        BacklogEntry? entry = BacklogEditor.NumberedEntry(text, line);
        BacklogEdit? edit = BacklogEditor.Drop(text, line);
        if (edit is null)
        {
            return InvalidPlan();
        }

        byte[] bytes = edit.Text.ToBytes();
        List<FileChange> files = [new(BacklogReader.FileName, backlogPath, bytes, file.Bytes)];
        string? storyPath = entry is null ? null : StoryReader.ResolvePath(project, entry);
        if (storyPath is not null)
        {
            FileInfo story = new(storyPath);
            if (story.LinkTarget is not null || Directory.Exists(storyPath))
            {
                return InvalidPlan();
            }

            if (story.Exists)
            {
                byte[]? old = SafeFileReader.ReadBytes(storyPath, StoryReader.MaxBytes);
                if (old is null)
                {
                    return InvalidPlan();
                }

                files.Add(new FileChange(StoryRelative(storyPath), storyPath, null, old));
            }
        }

        return new ChangePlan(files, "backlog: drop " + edit.Subject, bytes);
    }

    private static ChangePlan PlanAdd(
        ProjectFolder project, BacklogChangeRequest request, EditableText text, string backlogPath, BacklogFile file)
    {
        string folder = Path.Combine(project.Path, StoryReader.FolderName);
        List<string> names = [];
        try
        {
            DirectoryInfo info = new(folder);
            if (info.LinkTarget is not null || File.Exists(folder))
            {
                return InvalidPlan();
            }

            if (info.Exists)
            {
                names.AddRange(info.EnumerateFileSystemInfos().Select(entry => entry.Name));
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return InvalidPlan();
        }

        string title = request.Title!;
        int number = BacklogEditor.NextNumber(text, names);
        string relative = BacklogEditor.StoryPath(number, title);
        string storyPath = Path.Combine(project.Path, relative);
        if (File.Exists(storyPath) || Directory.Exists(storyPath) || new FileInfo(storyPath).LinkTarget is not null)
        {
            return InvalidPlan();
        }

        string story = BacklogEditor.RenderStory(number, title, request.Story, request.What!, request.Why!);
        byte[] storyBytes = StrictUtf8.GetBytes(story);
        BacklogEdit edit = BacklogEditor.Add(text, number, title, request.Where ?? AddPosition.Bottom);
        byte[] bytes = edit.Text.ToBytes();
        return new ChangePlan(
            [new(BacklogReader.FileName, backlogPath, bytes, file.Bytes), new(relative, storyPath, storyBytes, null)],
            "backlog: add " + edit.Subject,
            bytes);
    }

    private static ChangePlan PlanTitle(
        ProjectFolder project,
        BacklogChangeRequest request,
        EditableText text,
        int line,
        string backlogPath,
        BacklogFile file)
    {
        BacklogEntry? entry = BacklogEditor.NumberedEntry(text, line);
        BacklogEdit? edit = BacklogEditor.Title(text, line, request.Title!);
        if (entry?.Number is null || edit is null)
        {
            return InvalidPlan();
        }

        byte[] bytes = edit.Text.ToBytes();
        List<FileChange> files = [new(BacklogReader.FileName, backlogPath, bytes, file.Bytes)];
        string? storyPath = StoryReader.ResolvePath(project, entry);
        byte[]? old = storyPath is null ? null : SafeFileReader.ReadBytes(storyPath, StoryReader.MaxBytes);
        EditableText? story = old is null ? null : EditableText.Parse(old);
        if (storyPath is not null && old is not null && story is not null)
        {
            string body = story.Text;
            string? retitled = BacklogEditor.RetitleStory(body, entry.Number.Value, entry.Title, request.Title!);
            if (retitled is not null && retitled != body)
            {
                byte[] next = StrictUtf8.GetBytes(retitled);
                byte[] newBytes = story.HasBom ? [0xEF, 0xBB, 0xBF, .. next] : next;
                files.Add(new FileChange(StoryRelative(storyPath), storyPath, newBytes, old));
            }
        }

        return new ChangePlan(files, "backlog: edit title of " + edit.Subject, bytes);
    }

    private static ChangePlan PlanStory(
        ProjectFolder project, BacklogChangeRequest request, EditableText text, int line, BacklogFile file)
    {
        ChangePlan? refused = OpenStory(project, request, text, line, out BacklogEntry? entry, out string? storyPath,
            out byte[]? old);
        if (refused is not null)
        {
            return refused;
        }

        byte[] next = StrictUtf8.GetBytes(request.Markdown!);
        if (next.Length > MaxStoryBytes || next.AsSpan().SequenceEqual(old))
        {
            return InvalidPlan();
        }

        return new ChangePlan(
            [new FileChange(StoryRelative(storyPath!), storyPath!, next, old)],
            "backlog: edit story " + Subject(entry!),
            file.Bytes);
    }

    /// <summary>Appends a dated note to the story's <c>## Notes</c> section, with the same checks as
    /// <c>story</c>.</summary>
    private static ChangePlan PlanNote(
        ProjectFolder project, BacklogChangeRequest request, EditableText text, int line, BacklogFile file, DateOnly today)
    {
        ChangePlan? refused = OpenStory(project, request, text, line, out BacklogEntry? entry, out string? storyPath,
            out byte[]? old);
        if (refused is not null)
        {
            return refused;
        }

        EditableText? story = EditableText.Parse(old!);
        if (story is null)
        {
            return InvalidPlan();
        }

        byte[] next = BacklogEditor.AppendNote(story, today, request.Note!).ToBytes();
        if (next.Length > MaxStoryBytes)
        {
            return InvalidPlan();
        }

        return new ChangePlan(
            [new FileChange(StoryRelative(storyPath!), storyPath!, next, old)],
            "backlog: note on " + Subject(entry!),
            file.Bytes);
    }

    /// <summary>The checks <c>story</c> and <c>note</c> share: a numbered open item, its story path from the line, a
    /// safe read and the <c>storyVersion</c> the client saw. <c>null</c> when all pass, else the refusal.</summary>
    private static ChangePlan? OpenStory(
        ProjectFolder project,
        BacklogChangeRequest request,
        EditableText text,
        int line,
        out BacklogEntry? entry,
        out string? storyPath,
        out byte[]? old)
    {
        entry = BacklogEditor.NumberedEntry(text, line);
        storyPath = null;
        old = null;
        if (entry?.Number is null || !BacklogEditor.IsOpenItem(text, line))
        {
            return InvalidPlan();
        }

        storyPath = StoryReader.ResolvePath(project, entry);
        old = storyPath is null ? null : SafeFileReader.ReadBytes(storyPath, StoryReader.MaxBytes);
        if (storyPath is null || old is null)
        {
            return InvalidPlan();
        }

        if (!string.Equals(SafeFileReader.Version(old), request.StoryVersion, StringComparison.Ordinal))
        {
            return ChangePlan.Refused(StatusCodes.Status409Conflict, Changed);
        }

        return null;
    }

    private static string Subject(BacklogEntry entry)
    {
        return "#" + entry.Number!.Value.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>Parses and checks the body: a JSON object with known fields of the right types, a known action
    /// with the fields it needs, and clean title, what, why, story, markdown and note texts. <c>null</c> otherwise.</summary>
    public static BacklogChangeRequest? ParseRequest(byte[] body)
    {
        Dictionary<string, JsonElement> fields = new(StringComparer.Ordinal);
        try
        {
            using JsonDocument document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            foreach (JsonProperty property in document.RootElement.EnumerateObject())
            {
                if (!Fields.Contains(property.Name) || !fields.TryAdd(property.Name, property.Value.Clone()))
                {
                    return null;
                }
            }

            return Check(fields);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException
                                              or ArgumentException)
        {
            return null;
        }
    }

    private static BacklogChangeRequest? Check(Dictionary<string, JsonElement> fields)
    {
        if (!TryString(fields, "project", out string? project) || project is null
            || !TryString(fields, "version", out string? version) || version is null
            || !TryString(fields, "action", out string? action) || action is null
            || !TryString(fields, "where", out string? where)
            || !TryString(fields, "title", out string? title)
            || !TryString(fields, "what", out string? what)
            || !TryString(fields, "why", out string? why)
            || !TryString(fields, "story", out string? story)
            || !TryString(fields, "storyVersion", out string? storyVersion)
            || !TryString(fields, "markdown", out string? markdown)
            || !TryString(fields, "note", out string? note))
        {
            return null;
        }

        int? line = null;
        if (fields.TryGetValue("line", out JsonElement lineValue))
        {
            if (lineValue.ValueKind != JsonValueKind.Number || !lineValue.TryGetInt32(out int number) || number < 0)
            {
                return null;
            }

            line = number;
        }

        BacklogChangeRequest request = new(
            project, version, action, line, null, null, null, null, null, null, null);
        switch (action)
        {
            case "approve":
            case "drop":
            case "done":
            case "up":
            case "down":
            case "top":
                return line is null ? null : request;
            case "add":
            {
                string? clean = BacklogEditor.CleanTitle(title);
                AddPosition? position = where switch
                {
                    "top" => AddPosition.Top,
                    "bottom" => AddPosition.Bottom,
                    _ => null,
                };
                if (clean is null || position is null || !IsBlockText(what) || !IsBlockText(why)
                    || (story is not null && !BacklogEditor.IsStoryText(story)))
                {
                    return null;
                }

                return request with { Where = position, Title = clean, What = what, Why = why, Story = story };
            }

            case "title":
            {
                string? clean = BacklogEditor.CleanTitle(title);
                return line is null || clean is null ? null : request with { Title = clean };
            }

            case "story":
                if (line is null || storyVersion is null || markdown is null || !BacklogEditor.IsStoryText(markdown)
                    || StrictUtf8.GetByteCount(markdown) > MaxStoryBytes)
                {
                    return null;
                }

                return request with { StoryVersion = storyVersion, Markdown = markdown };
            case "note":
            {
                string? clean = BacklogEditor.CleanNote(note);
                return line is null || storyVersion is null || clean is null
                    ? null
                    : request with { StoryVersion = storyVersion, Note = clean };
            }

            default:
                return null;
        }
    }

    private static bool IsBlockText(string? text)
    {
        return text is not null && text.Trim().Length > 0 && BacklogEditor.IsStoryText(text);
    }

    /// <summary>A missing field is <c>null</c> and fine; a field that is there must be a JSON string.</summary>
    private static bool TryString(Dictionary<string, JsonElement> fields, string name, out string? value)
    {
        value = null;
        if (!fields.TryGetValue(name, out JsonElement element))
        {
            return true;
        }

        if (element.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = element.GetString();
        return value is not null;
    }

    private static bool IsJson(string? contentType)
    {
        if (contentType is null || !MediaTypeHeaderValue.TryParse(contentType, out MediaTypeHeaderValue? media))
        {
            return false;
        }

        return string.Equals(media.MediaType, "application/json", StringComparison.OrdinalIgnoreCase)
            && (media.CharSet is null || string.Equals(media.CharSet, "utf-8", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The body when it is at most <see cref="MaxBodyBytes"/>, else <c>null</c>.</summary>
    private static async Task<byte[]?> ReadBody(HttpRequest request)
    {
        if (request.ContentLength > MaxBodyBytes)
        {
            return null;
        }

        byte[] buffer = new byte[MaxBodyBytes + 1];
        int total = 0;
        int read;
        while (total < buffer.Length && (read = await request.Body.ReadAsync(buffer.AsMemory(total))) > 0)
        {
            total += read;
        }

        return total > MaxBodyBytes ? null : buffer[..total];
    }

    private static void Apply(string path, byte[]? bytes)
    {
        if (bytes is null)
        {
            SafeFileWriter.Delete(path);
        }
        else
        {
            SafeFileWriter.Write(path, bytes);
        }
    }

    /// <summary>Puts the written files back as they were, and removes a story folder the change created.</summary>
    private static void Restore(IReadOnlyList<FileChange> applied, ProjectFolder project, bool createdFolder)
    {
        foreach (FileChange change in applied)
        {
            try
            {
                Apply(change.FullPath, change.OldBytes);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Best effort; the repo then shows the change as uncommitted and further writes are refused.
            }
        }

        string folder = Path.Combine(project.Path, StoryReader.FolderName);
        try
        {
            if (createdFolder && Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any())
            {
                Directory.Delete(folder);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // An empty folder is not tracked by git; leaving it changes nothing.
        }
    }

    private static ChangePlan BacklogOnly(
        BacklogEdit edit, string verb, string backlogPath, BacklogFile file, string suffix = "")
    {
        byte[] bytes = edit.Text.ToBytes();
        return new ChangePlan(
            [new(BacklogReader.FileName, backlogPath, bytes, file.Bytes)],
            "backlog: " + verb + " " + edit.Subject + suffix,
            bytes);
    }

    private static string StoryRelative(string storyPath)
    {
        return StoryReader.FolderName + "/" + Path.GetFileName(storyPath);
    }

    private static ChangePlan InvalidPlan()
    {
        return ChangePlan.Refused(StatusCodes.Status400BadRequest, Invalid);
    }

    private static IResult Error(int status, string error)
    {
        return Results.Content("{\"error\":\"" + error + "\"}", "application/json", statusCode: status);
    }
}
