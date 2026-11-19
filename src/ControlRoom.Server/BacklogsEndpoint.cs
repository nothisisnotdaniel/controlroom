namespace ControlRoom.Server;

/// <summary>The factory root the server reads, fixed at start.</summary>
internal sealed record FactoryRoot(string Path);

/// <summary>One project in the <c>GET /api/backlogs</c> response. <c>Version</c> is the SHA-256 of the
/// <c>backlog.md</c> bytes (<c>null</c> when unreadable), sent back with every change. <c>Next</c>, <c>Stopped</c> and
/// <c>Blocked</c> come from <see cref="BacklogPlan"/> (<c>null</c>, <c>false</c> and empty when unreadable).</summary>
internal sealed record ProjectBacklog(
    string Name,
    bool Readable,
    IReadOnlyList<BacklogEntry> Open,
    IReadOnlyList<BacklogEntry> Approval,
    IReadOnlyList<BacklogEntry> User,
    IReadOnlyList<BacklogEntry> Done,
    string? Version,
    PlanItem? Next,
    bool Stopped,
    IReadOnlyList<BlockedItem> Blocked);

/// <summary>The <c>GET /api/backlogs</c> response.</summary>
internal sealed record BacklogsResponse(IReadOnlyList<ProjectBacklog> Projects);

/// <summary><c>GET /api/backlogs</c>: every project's backlog, grouped by mark. Read-only, no route
/// parameters.</summary>
internal static class BacklogsEndpoint
{
    public const string Route = "/api/backlogs";

    public static void Map(WebApplication app)
    {
        app.MapMethods(Route, [HttpMethods.Get, HttpMethods.Head], (FactoryRoot root, HttpContext context) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            return Results.Json(Build(root.Path));
        });
    }

    public static BacklogsResponse Build(string factoryRoot)
    {
        List<(ProjectFolder Project, BacklogFile? File)> read = ProjectFinder.Find(factoryRoot)
            .Select(project => (project, BacklogReader.ReadFile(project)))
            .ToList();
        BacklogWaiting waiting = BacklogWaiting.From(read.Select(pair => (pair.Project.Name, pair.File?.Backlog)));

        List<ProjectBacklog> projects = [];
        foreach ((ProjectFolder project, BacklogFile? file) in read)
        {
            Backlog groups = file?.Backlog ?? Backlog.Empty;
            ProjectPlan plan = BacklogPlan.For(file?.Backlog, project.Name, waiting);
            projects.Add(new ProjectBacklog(
                project.Name,
                file is not null,
                WithWaiting(groups.Open, project.Name, waiting),
                WithWaiting(groups.Approval, project.Name, waiting),
                WithWaiting(groups.User, project.Name, waiting),
                WithWaiting(groups.Done, project.Name, waiting),
                file?.Version,
                plan.Next,
                plan.Stopped,
                plan.Blocked));
        }

        return new BacklogsResponse(projects);
    }

    /// <summary>Marks an entry waiting when any of its dependencies is not met (<see cref="BacklogWaiting"/>).</summary>
    private static List<BacklogEntry> WithWaiting(
        IReadOnlyList<BacklogEntry> entries, string projectName, BacklogWaiting waiting)
    {
        return entries
            .Select(entry => entry with { Waiting = waiting.IsWaiting(entry, projectName) })
            .ToList();
    }
}
