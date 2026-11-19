using System.Net.Http.Headers;
using System.Text.Json;

namespace ControlRoom.Server;

/// <summary>Serializes writes of job request files: one at a time per server.</summary>
internal sealed class JobActLock
{
    public SemaphoreSlim Gate { get; } = new(1, 1);
}

/// <summary>A checked <c>POST /api/job/act</c> body; <c>Note</c> is the cleaned note of a rework, else
/// <c>null</c>.</summary>
internal sealed record JobActRequest(string Id, string Action, int Round, string? Note);

/// <summary>The <c>200</c> response: the request now pending, or <c>null</c> after a cancel.</summary>
internal sealed record JobActResponse(JobRequestView? Request);

/// <summary><c>POST /api/job/act</c>: asks the next <c>/factory</c> session to approve or rework a
/// <c>needs-human</c> job, or cancels that request, by writing or deleting the job's <c>human.json</c>. Nothing is
/// run and nothing else is written. The id is matched exactly against <see cref="JobReader.ListFolders"/> and never
/// joined into a path; <c>state.json</c> is read again under the lock; the file is written through
/// <see cref="SafeFileWriter"/> and never through a symlink.</summary>
internal static class JobActEndpoint
{
    public const string Route = "/api/job/act";

    public const int MaxBodyBytes = 8 * 1024;

    public const string NeedsHuman = "needs-human";

    public const string Cancel = "cancel";

    public const string Invalid = "invalid";

    public const string NotWaiting = "not_waiting";

    public const string Changed = "changed";

    public const string Pending = "pending";

    public const string NotPending = "not_pending";

    public const string WriteFailed = "write_failed";

    private static readonly HashSet<string> Fields = new(StringComparer.Ordinal) { "id", "action", "round", "note" };

    public static void Map(WebApplication app)
    {
        app.MapPost(Route, async (FactoryRoot root, JobActLock actLock, TimeProvider time, HttpContext context) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            if (!IsJson(context.Request.ContentType))
            {
                return Error(StatusCodes.Status415UnsupportedMediaType, Invalid);
            }

            byte[]? body = await ReadBody(context.Request);
            JobActRequest? request = body is null ? null : ParseRequest(body);
            if (request is null)
            {
                return Error(StatusCodes.Status400BadRequest, Invalid);
            }

            await actLock.Gate.WaitAsync(context.RequestAborted);
            try
            {
                return Act(root.Path, request, time);
            }
            finally
            {
                actLock.Gate.Release();
            }
        });
    }

    private static IResult Act(string factoryRoot, JobActRequest request, TimeProvider time)
    {
        JobFolder? folder = JobReader.ListFolders(factoryRoot)
            .FirstOrDefault(candidate => string.Equals(candidate.Id, request.Id, StringComparison.Ordinal));
        JobSummary? job = folder is null ? null : JobReader.Read(folder);
        if (folder is null || job is null)
        {
            return Results.Content(JobEndpoint.NotFoundBody, "application/json", statusCode: StatusCodes.Status404NotFound);
        }

        if (!string.Equals(job.Stage, NeedsHuman, StringComparison.Ordinal))
        {
            return Error(StatusCodes.Status409Conflict, NotWaiting);
        }

        int round = job.Round ?? 0;
        if (request.Round != round)
        {
            return Error(StatusCodes.Status409Conflict, Changed);
        }

        string path = Path.Combine(folder.Path, JobAction.FileName);
        PathKind kind = NoFollowStat.Of(path).Kind;
        if (kind is not (PathKind.Missing or PathKind.File))
        {
            // A symlink (even a dangling one), a folder or anything else is never written through or removed.
            return Error(StatusCodes.Status500InternalServerError, WriteFailed);
        }

        try
        {
            if (request.Action == Cancel)
            {
                if (kind == PathKind.Missing)
                {
                    return Error(StatusCodes.Status409Conflict, NotPending);
                }

                SafeFileWriter.Delete(path);
                return Results.Json(new JobActResponse(null));
            }

            if (kind == PathKind.File)
            {
                return Error(StatusCodes.Status409Conflict, Pending);
            }

            JobRequest written = new(request.Action, round, request.Note, JobAction.FormatTime(time.GetUtcNow()));
            byte[] bytes = JobAction.Render(written);
            if (bytes.Length > JobAction.MaxFileBytes)
            {
                return Error(StatusCodes.Status400BadRequest, Invalid);
            }

            SafeFileWriter.Write(path, bytes);
            return Results.Json(new JobActResponse(JobRequestView.Of(written)));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Error(StatusCodes.Status500InternalServerError, WriteFailed);
        }
    }

    /// <summary>The checked body: one JSON object with only the known fields, each at most once, a string
    /// <c>id</c> and <c>action</c> (<c>approve</c>, <c>rework</c> or <c>cancel</c>), a non-negative integer
    /// <c>round</c>, and a <c>note</c> that only a rework has and that passes <see cref="BacklogEditor.CleanNote"/>.
    /// <c>null</c> otherwise.</summary>
    public static JobActRequest? ParseRequest(byte[] body)
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
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException)
        {
            return null;
        }

        string? id = Text(fields, "id");
        string? action = Text(fields, "action");
        if (id is null || action is null
            || !fields.TryGetValue("round", out JsonElement roundValue)
            || roundValue.ValueKind != JsonValueKind.Number
            || !roundValue.TryGetInt32(out int round) || round < 0)
        {
            return null;
        }

        bool hasNote = fields.ContainsKey("note");
        switch (action)
        {
            case JobAction.Approve:
            case Cancel:
                return hasNote ? null : new JobActRequest(id, action, round, null);
            case JobAction.Rework:
            {
                string? note = BacklogEditor.CleanNote(Text(fields, "note"));
                return note is null ? null : new JobActRequest(id, action, round, note);
            }

            default:
                return null;
        }
    }

    private static string? Text(Dictionary<string, JsonElement> fields, string name)
    {
        return fields.TryGetValue(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
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

    private static IResult Error(int status, string error)
    {
        return Results.Content("{\"error\":\"" + error + "\"}", "application/json", statusCode: status);
    }
}
