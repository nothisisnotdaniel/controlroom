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
        List<(ProjectFolder Project, Backlog? Backlog)> read = ProjectFinder.Find(factoryRoot)
            .Select(project => (project, BacklogReader.Read(project)))
            .ToList();
        Dictionary<string, HashSet<int>> doneNumbers = new(StringComparer.Ordinal);
        foreach ((ProjectFolder project, Backlog? backlog) in read)
        {
            doneNumbers[project.Name] = (backlog ?? Backlog.Empty).Done
                .Where(entry => entry.Number is not null)
                .Select(entry => entry.Number!.Value)
                .ToHashSet();
        }

        List<ProjectBacklog> projects = [];
        foreach ((ProjectFolder project, Backlog? backlog) in read)
        {
            Backlog groups = backlog ?? Backlog.Empty;
            projects.Add(new ProjectBacklog(
                project.Name,
                backlog is not null,
                WithWaiting(groups.Open, project.Name, doneNumbers),
                WithWaiting(groups.Approval, project.Name, doneNumbers),
                WithWaiting(groups.User, project.Name, doneNumbers),
                WithWaiting(groups.Done, project.Name, doneNumbers)));
        }

        return new BacklogsResponse(projects);
    }

    /// <summary>Marks an entry waiting when any of its dependencies is not a done line of the named (or the same)
    /// project; an unknown project or number counts as not done.</summary>
    private static List<BacklogEntry> WithWaiting(
        IReadOnlyList<BacklogEntry> entries, string projectName, Dictionary<string, HashSet<int>> doneNumbers)
    {
        return entries
            .Select(entry => entry with
            {
                Waiting = entry.After.Any(dependency =>
                    !doneNumbers.TryGetValue(dependency.Project ?? projectName, out HashSet<int>? done)
                    || !done.Contains(dependency.Number)),
            })
            .ToList();
    }
}
