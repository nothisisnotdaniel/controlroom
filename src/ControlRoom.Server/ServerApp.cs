using Microsoft.AspNetCore.Server.Kestrel.Core;

namespace ControlRoom.Server;

/// <summary>Builds Control Room on <see cref="RoomKitApp"/> with <see cref="ControlRoomApp"/>'s values and
/// endpoints.</summary>
internal static class ServerApp
{
    /// <summary>Builds the app, or returns <c>null</c> after writing a fixed error text (see
    /// <see cref="RoomKitApp.Create"/>).</summary>
    public static WebApplication? Create(string[] args, TextWriter error) => Create(args, error, null);

    /// <summary>As <see cref="Create(string[], TextWriter)"/>; <paramref name="onListen"/> is a test seam called for
    /// every endpoint Kestrel listens on, <c>null</c> in production.</summary>
    internal static WebApplication? Create(string[] args, TextWriter error, Action<ListenOptions>? onListen)
    {
        return RoomKitApp.Create(
            args, error, ControlRoomApp.Options, ControlRoomApp.Map, onListen, new FactoryModule(ControlRoomApp.Jobs));
    }
}
