using System.Net;

namespace ControlRoom.Server;

/// <summary>The one address the server listens on: loopback only, port 8100.</summary>
internal static class ServerEndpoint
{
    public const int Port = 8100;

    public static readonly IPEndPoint Address = new(IPAddress.Loopback, Port);
}
