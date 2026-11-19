namespace ControlRoom.Server;

/// <summary>One <c>[h]</c> backlog line in the <c>GET /api/board</c> response: a task for the user.</summary>
internal sealed record BoardTask(string Project, int? Number, string Title, string Rest);

/// <summary>The <c>GET /api/board</c> response.</summary>
internal sealed record BoardResponse(IReadOnlyList<JobSummary> Jobs, IReadOnlyList<BoardTask> Tasks);

/// <summary><c>GET /api/board</c>: every job (from <see cref="JobReader"/>, a <c>needs-human</c> job with its
/// <c>request</c> action) and the <c>[h]</c> lines of every
/// project's backlog. Read-only, no parameters.</summary>
internal static class BoardEndpoint
{
    public const string Route = "/api/board";

    public static void Map(WebApplication app)
    {
        app.MapMethods(Route, [HttpMethods.Get, HttpMethods.Head], (FactoryRoot root, HttpContext context) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            return Results.Json(Build(root.Path));
        });
    }

    public static BoardResponse Build(string factoryRoot)
    {
        List<JobSummary> jobs = [];
        foreach (JobFolder folder in JobReader.ListFolders(factoryRoot))
        {
            JobSummary? job = JobReader.Read(folder);
            if (job is not null)
            {
                jobs.Add(JobAction.WithRequest(job, folder));
            }
        }

        return new BoardResponse(jobs, Tasks(factoryRoot));
    }

    /// <summary>The <c>[h]</c> lines of every project, in project order and then file order; an unreadable
    /// backlog adds none.</summary>
    private static List<BoardTask> Tasks(string factoryRoot)
    {
        List<BoardTask> tasks = [];
        foreach (ProjectFolder project in ProjectFinder.Find(factoryRoot))
        {
            Backlog? backlog = BacklogReader.Read(project);
            if (backlog is null)
            {
                continue;
            }

            tasks.AddRange(backlog.User.Select(entry =>
                new BoardTask(project.Name, entry.Number, entry.Title, entry.Rest)));
        }

        return tasks;
    }
}
