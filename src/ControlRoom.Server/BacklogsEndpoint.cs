namespace ControlRoom.Server;

/// <summary>The factory root the server reads, fixed at start.</summary>
internal sealed record FactoryRoot(string Path);

/// <summary>One project in the <c>GET /api/backlogs</c> response.</summary>
internal sealed record ProjectBacklog(
    string Name,
    bool Readable,
    IReadOnlyList<BacklogEntry> Open,
    IReadOnlyList<BacklogEntry> Approval,
    IReadOnlyList<BacklogEntry> User,
    IReadOnlyList<BacklogEntry> Done);

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
        List<ProjectBacklog> projects = [];
        foreach (ProjectFolder project in ProjectFinder.Find(factoryRoot))
        {
            Backlog? backlog = BacklogReader.Read(project);
            Backlog groups = backlog ?? Backlog.Empty;
            projects.Add(new ProjectBacklog(
                project.Name, backlog is not null, groups.Open, groups.Approval, groups.User, groups.Done));
        }

        return new BacklogsResponse(projects);
    }
}
