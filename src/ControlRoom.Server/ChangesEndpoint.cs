namespace ControlRoom.Server;

/// <summary><c>GET /api/changes</c>: cheap change fingerprints (<see cref="ChangeFingerprint"/>) the web app polls to
/// know when to fetch the board, the backlogs or the open story again. Read-only, no parameters; file metadata
/// only, never file content.</summary>
internal static class ChangesEndpoint
{
    public const string Route = "/api/changes";

    public static void Map(WebApplication app)
    {
        app.MapMethods(Route, [HttpMethods.Get, HttpMethods.Head], (
            FactoryRoot root, BacklogWorktree worktree, HttpContext context) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            return Results.Json(ChangeFingerprint.Build(root.Path, worktree));
        });
    }
}
