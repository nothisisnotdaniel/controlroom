using System.Reflection;

namespace ControlRoom.Server;

/// <summary>Control Room's values for RoomKit: names, port, release repository, capture note, version and the
/// <c>state.json</c> job format.</summary>
internal static class ControlRoomApp
{
    /// <summary>The app's options. The version is this assembly's informational version (<c>dev</c> without
    /// <c>-p:Version</c>), never RoomKit's.</summary>
    public static RoomKitOptions Options { get; } = new()
    {
        AppName = "Control Room",
        ShortName = "ControlRoom",
        DataFolderName = "ControlRoom",
        SolutionFileName = "ControlRoom.slnx",
        DefaultPort = 8100,
        ReleaseRepository = "nothisisnotdaniel/controlroom",
        CaptureNote = "Draft from ControlRoom",
        InformationalVersion = typeof(ControlRoomApp).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
    };

    /// <summary>The factory's job format.</summary>
    public static IJobFormat Jobs { get; } = new StateJsonJobFormat();

    /// <summary>Maps Control Room's own endpoints: project status, releases, board, changes, job and job act.</summary>
    public static void Map(RoomRoutes routes)
    {
        ArgumentNullException.ThrowIfNull(routes);
        ProjectStatusEndpoint.Map(routes.App);
        ReleasesEndpoint.Map(routes.App);
        BoardEndpoint.Map(routes.App);
        ChangesEndpoint.Map(routes.App);
        JobEndpoint.Map(routes.App);
        JobActEndpoint.Map(routes);
    }
}
