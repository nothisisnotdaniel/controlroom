using Microsoft.Extensions.Primitives;

namespace ControlRoom.Server;

/// <summary>One record of a job: its file name (relative to the job folder), its tab label, whether it could be
/// read and its block tree (empty when not readable).</summary>
internal sealed record JobRecord(string Name, string Label, bool Readable, IReadOnlyList<StoryBlock> Blocks);

/// <summary>The <c>GET /api/job</c> response.</summary>
internal sealed record JobResponse(JobSummary Job, IReadOnlyList<JobRecord> Records, JobRequestView? Request);

/// <summary><c>GET /api/job?id=&lt;id&gt;</c>: one job and its Markdown records. The id is matched exactly
/// (ordinal) against the listed job folders and never joined into a path; record names come only from a fixed
/// list. <c>request</c> is the job's <c>human.json</c> (<see cref="JobAction.Read"/>): <c>null</c>, the parsed
/// request or <c>{"action":"unreadable"}</c>. Every refusal is the same fixed 404.</summary>
internal static class JobEndpoint
{
    public const string Route = "/api/job";

    public const string NotFoundBody = "{\"error\":\"not found\"}";

    public const long MaxRecordBytes = 1024 * 1024;

    /// <summary>The highest round whose reviews are listed.</summary>
    public const int MaxRounds = 20;

    private static readonly (string Name, string Label)[] LeadingRecords =
    [
        ("spec.md", "spec"),
        ("spec-review.md", "spec check"),
        ("task.md", "task"),
        ("findings.md", "findings"),
        ("build.md", "build"),
    ];

    private static readonly (string Name, string Label)[] TrailingRecords =
    [
        ("feedback.md", "feedback"),
        ("decision.md", "decision"),
    ];

    public static void Map(WebApplication app)
    {
        app.MapMethods(Route, [HttpMethods.Get, HttpMethods.Head], (FactoryRoot root, HttpContext context) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            JobResponse? job = Build(root.Path, context.Request.Query["id"]);
            if (job is null)
            {
                return Results.Content(NotFoundBody, "application/json", statusCode: StatusCodes.Status404NotFound);
            }

            return Results.Json(job);
        });
    }

    public static JobResponse? Build(string factoryRoot, StringValues idValue)
    {
        if (idValue.Count != 1 || string.IsNullOrEmpty(idValue[0]))
        {
            return null;
        }

        string id = idValue[0]!;
        JobFolder? folder = JobReader.ListFolders(factoryRoot)
            .FirstOrDefault(candidate => string.Equals(candidate.Id, id, StringComparison.Ordinal));
        if (folder is null)
        {
            return null;
        }

        JobSummary? job = JobReader.Read(folder);
        if (job is null)
        {
            return null;
        }

        return new JobResponse(JobAction.WithRequest(job, folder), Records(folder, job.Round ?? 0), JobAction.Read(folder));
    }

    /// <summary>The records that exist, in the fixed order: leading files, then each round's reviews, then
    /// feedback and decision.</summary>
    private static List<JobRecord> Records(JobFolder folder, int round)
    {
        List<JobRecord> records = [];
        foreach ((string name, string label) in LeadingRecords)
        {
            AddIfPresent(records, folder.Path, name, label, followable: true);
        }

        for (int n = 1; n <= Math.Min(round, MaxRounds); n++)
        {
            string roundName = "round-" + n.ToString(System.Globalization.CultureInfo.InvariantCulture);
            bool roundIsLink = IsLink(Path.Combine(folder.Path, roundName));
            foreach (string key in JobReader.ReviewerKeys)
            {
                AddIfPresent(records, folder.Path, roundName + "/review-" + key + ".md", "R" + n + " " + key,
                    followable: !roundIsLink);
            }
        }

        foreach ((string name, string label) in TrailingRecords)
        {
            AddIfPresent(records, folder.Path, name, label, followable: true);
        }

        return records;
    }

    /// <summary>Adds the record when something exists at its path. It is read only when <paramref
    /// name="followable"/> (its folder is no symlink), through <see cref="SafeFileReader"/>: no-follow, regular
    /// file, <see cref="MaxRecordBytes"/> cap.</summary>
    private static void AddIfPresent(
        List<JobRecord> records, string jobPath, string name, string label, bool followable)
    {
        string path = Path.Combine(jobPath, name);
        if (!Exists(path))
        {
            return;
        }

        string? text = followable ? SafeFileReader.ReadText(path, MaxRecordBytes) : null;
        records.Add(text is null
            ? new JobRecord(name, label, false, [])
            : new JobRecord(name, label, true, StoryMarkdown.Parse(text)));
    }

    private static bool Exists(string path)
    {
        try
        {
            return File.Exists(path) || Directory.Exists(path) || new FileInfo(path).LinkTarget is not null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool IsLink(string path)
    {
        try
        {
            return new DirectoryInfo(path).LinkTarget is not null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }
}
