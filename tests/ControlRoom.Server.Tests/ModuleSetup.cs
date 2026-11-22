using System.Runtime.CompilerServices;

namespace ControlRoom.Server.Tests;

/// <summary>Configures RoomKit with Control Room's values before any test runs, as <c>Program</c> does.</summary>
internal static class ModuleSetup
{
    [ModuleInitializer]
    internal static void Init()
    {
        RoomContext.Use(ControlRoomApp.Options, ControlRoomApp.Jobs);
    }
}
