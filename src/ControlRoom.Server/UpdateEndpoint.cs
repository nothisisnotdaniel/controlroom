namespace ControlRoom.Server;

/// <summary>The <c>GET /api/update</c> response; <c>latest</c>, <c>url</c> and <c>command</c> are <c>null</c> unless
/// <c>available</c>.</summary>
internal sealed record UpdateResponse(string Current, bool Available, string? Latest, string? Url, string? Command);

/// <summary><c>GET /api/update</c>, behind the token gate: the running version and, when the last check found a
/// newer release, its version, release page and the command that installs it (with <c>--tailnet-host</c> when
/// <c>TailnetHost</c> is set). Read-only, no parameters, <c>no-store</c>.</summary>
internal static class UpdateEndpoint
{
    public const string Route = "/api/update";

    public const string CommandPrefix = "scripts/launch-agent.sh install --release v";

    public static void Map(WebApplication app, string? tailnetHost)
    {
        app.MapMethods(Route, [HttpMethods.Get, HttpMethods.Head], (UpdateState state, RunningVersion running, HttpContext context) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            return Results.Json(Describe(state.Latest, running.Value, tailnetHost));
        });
    }

    public static UpdateResponse Describe(UpdateRelease? latest, string current, string? tailnetHost)
    {
        SemVer? running = SemVer.Parse(current);
        if (latest is null || running is null || !latest.Version.IsNewerThan(running))
        {
            return new UpdateResponse(current, false, null, null, null);
        }

        string command = CommandPrefix + latest.Version + (tailnetHost is null ? "" : " --tailnet-host " + tailnetHost);
        return new UpdateResponse(current, true, latest.Version.ToString(), latest.Url, command);
    }
}
