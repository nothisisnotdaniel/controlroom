using System.Globalization;
using System.Text.RegularExpressions;

namespace ControlRoom.Server;

/// <summary>The last CI result the factory recorded for a project: <c>Status</c> one of
/// <see cref="ProjectStatusEndpoint.CiStatuses"/>, <c>Url</c> an <c>https://github.com/…</c> link or <c>null</c>,
/// <c>Sha</c> the first 7 hex characters or <c>null</c>, <c>Time</c> the job's <c>updated</c> as
/// <c>YYYY-MM-DDTHH:MM:SSZ</c> or <c>null</c>.</summary>
internal sealed record ProjectCi(string Status, string? Url, string? Sha, string? Time);

/// <summary>One project's status: its last CI result (or <c>null</c>) and the commits on <c>develop</c> not on
/// <c>main</c> (or <c>null</c> when unknown).</summary>
internal sealed record ProjectStatus(string Name, ProjectCi? Ci, int? Ahead);

/// <summary>The <c>GET /api/status</c> response.</summary>
internal sealed record ProjectStatusResponse(IReadOnlyList<ProjectStatus> Projects);

/// <summary><c>GET|HEAD /api/status</c>: per discovered project (in <see cref="ProjectFinder"/> order) the CI result
/// of its newest job that has a valid <c>ci</c> object (highest numeric id prefix, ties by id ordinal descending; the
/// project name must match exactly) and <see cref="ProjectGit.AheadOfMain"/>. Read-only, <c>no-store</c>; any query
/// parameter gives <c>400 {"error":"invalid"}</c>. Runs only the one fixed git call per project, never <c>gh</c> or
/// the network. A job's <c>ci.url</c> is kept only as an <c>https://github.com/</c> URL, so a crafted
/// <c>state.json</c> cannot become a <c>javascript:</c> link.</summary>
internal static partial class ProjectStatusEndpoint
{
    public const string Route = "/api/status";

    public const string InvalidBody = "{\"error\":\"invalid\"}";

    public const int MaxUrlLength = 500;

    public const int ShaLength = 7;

    public static readonly IReadOnlySet<string> CiStatuses =
        new HashSet<string>(["success", "failure", "timeout", "skipped"], StringComparer.Ordinal);

    public static void Map(WebApplication app)
    {
        app.MapMethods(Route, [HttpMethods.Get, HttpMethods.Head], async (FactoryRoot root, HttpContext context) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            if (context.Request.Query.Count > 0)
            {
                return Results.Content(InvalidBody, "application/json", statusCode: StatusCodes.Status400BadRequest);
            }

            return Results.Json(await Build(root.Path));
        });
    }

    public static async Task<ProjectStatusResponse> Build(string factoryRoot)
    {
        IReadOnlyList<ProjectFolder> projects = ProjectFinder.Find(factoryRoot);
        IReadOnlyList<JobSummary> jobs = JobReader.ReadAll(factoryRoot);
        List<ProjectStatus> statuses = [];
        foreach (ProjectFolder project in projects)
        {
            int? ahead = await ProjectGit.AheadOfMain(project.Path);
            statuses.Add(new ProjectStatus(project.Name, LastCi(project.Name, jobs), ahead));
        }

        return new ProjectStatusResponse(statuses);
    }

    /// <summary>The CI result of the project's newest job with a valid status, or <c>null</c>.</summary>
    public static ProjectCi? LastCi(string project, IReadOnlyList<JobSummary> jobs)
    {
        JobSummary? newest = null;
        long newestNumber = -1;
        foreach (JobSummary job in jobs)
        {
            if (!string.Equals(job.Project, project, StringComparison.Ordinal)
                || job.CiStatus is null
                || !CiStatuses.Contains(job.CiStatus))
            {
                continue;
            }

            long number = IdNumber(job.Id);
            if (newest is null
                || number > newestNumber
                || (number == newestNumber && string.CompareOrdinal(job.Id, newest.Id) > 0))
            {
                newest = job;
                newestNumber = number;
            }
        }

        if (newest is null)
        {
            return null;
        }

        return new ProjectCi(
            newest.CiStatus!,
            SafeUrl(newest.CiUrl),
            ShortSha(newest.CiSha),
            newest.Updated?.UtcDateTime.ToString(JobReader.UpdatedFormat, CultureInfo.InvariantCulture));
    }

    /// <summary>The URL when it is an absolute <c>https://github.com/…</c> URL with no userinfo or port and at most
    /// <see cref="MaxUrlLength"/> characters; else <c>null</c>.</summary>
    public static string? SafeUrl(string? text)
    {
        if (text is null || text.Length > MaxUrlLength || !text.StartsWith("https://github.com/", StringComparison.Ordinal))
        {
            return null;
        }

        if (!Uri.TryCreate(text, UriKind.Absolute, out Uri? uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !string.Equals(uri.Host, "github.com", StringComparison.Ordinal)
            || uri.UserInfo.Length > 0
            || !uri.IsDefaultPort
            || text.Any(char.IsControl)
            || text.Any(char.IsWhiteSpace))
        {
            return null;
        }

        return text;
    }

    /// <summary>The first 7 characters of a 7–40 character lowercase hex sha, else <c>null</c>.</summary>
    public static string? ShortSha(string? text)
    {
        return text is not null && ShaPattern().IsMatch(text) ? text[..ShaLength] : null;
    }

    private static long IdNumber(string id)
    {
        int dash = id.IndexOf('-', StringComparison.Ordinal);
        string digits = dash < 0 ? id : id[..dash];
        return long.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out long number) ? number : -1;
    }

    [GeneratedRegex(@"\A[0-9a-f]{7,40}\z", RegexOptions.CultureInvariant)]
    private static partial Regex ShaPattern();
}
