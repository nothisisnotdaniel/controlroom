using System.Text.Json;

namespace ControlRoom.Server;

/// <summary>The <c>GET /api/update</c> response; <c>latest</c>, <c>url</c> and <c>command</c> are <c>null</c> unless
/// <c>available</c>; <c>autoUpdate</c> is <c>null</c> when <c>settings.json</c> is refused.</summary>
internal sealed record UpdateResponse(
    string Current, bool Available, string? Latest, string? Url, string? Command, bool? AutoUpdate = false);

/// <summary>The update routes, behind the token gate. <c>GET /api/update</c>: the running version and, when the last
/// check found a newer release, its version, release page and the command that installs it (with
/// <c>--tailnet-host</c> when <c>TailnetHost</c> is set), plus the <c>autoUpdate</c> setting. <c>POST
/// /api/update/settings</c> with exactly <c>{"autoUpdate":true|false}</c> stores the setting (else <c>400
/// {"error":"invalid"}</c>, checked before the store); a refused <c>settings.json</c> gives <c>500
/// {"error":"settings_store"}</c>. Both <c>no-store</c>.</summary>
internal static class UpdateEndpoint
{
    public const string Route = "/api/update";

    public const string SettingsRoute = "/api/update/settings";

    public const string CommandPrefix = "scripts/launch-agent.sh install --release v";

    public const string StoreError = "settings_store";

    public static void Map(WebApplication app, string? tailnetHost)
    {
        app.MapMethods(Route, [HttpMethods.Get, HttpMethods.Head], (
            UpdateState state, RunningVersion running, UpdateSettingsStore settings, HttpContext context) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            bool? autoUpdate;
            try
            {
                autoUpdate = settings.AutoUpdate();
            }
            catch (PrivateFileException)
            {
                autoUpdate = null;
            }

            return Results.Json(Describe(state.Latest, running.Value, tailnetHost) with { AutoUpdate = autoUpdate });
        });

        app.MapPost(SettingsRoute, async (UpdateSettingsStore settings, HttpContext context) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            byte[]? body = await PushEndpoint.ReadJsonBody(context.Request);
            bool? wanted = body is null ? null : ParseSettings(body);
            if (wanted is null)
            {
                return PushEndpoint.Error(StatusCodes.Status400BadRequest, PushEndpoint.Invalid);
            }

            try
            {
                return Results.Json(new { autoUpdate = settings.SetAutoUpdate(wanted.Value) });
            }
            catch (PrivateFileException)
            {
                return PushEndpoint.Error(StatusCodes.Status500InternalServerError, StoreError);
            }
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

    /// <summary>The value when the body is an object with exactly one property, <c>autoUpdate</c>, a boolean; else
    /// <c>null</c>.</summary>
    private static bool? ParseSettings(byte[] body)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            bool? value = null;
            int count = 0;
            foreach (JsonProperty property in document.RootElement.EnumerateObject())
            {
                count++;
                if (property.NameEquals("autoUpdate") && property.Value.ValueKind is JsonValueKind.True or JsonValueKind.False)
                {
                    value = property.Value.GetBoolean();
                }
            }

            return count == 1 ? value : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
