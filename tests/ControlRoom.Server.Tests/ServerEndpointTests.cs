using System.Net;
using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ControlRoom.Server.Tests;

public sealed class ServerEndpointTests
{
    [Fact]
    public void Endpoint_IsLoopbackPort8100()
    {
        Assert.Equal(IPAddress.Loopback, ServerEndpoint.Address.Address);
        Assert.Equal(8100, ServerEndpoint.Address.Port);
    }

    [Fact]
    public async Task Kestrel_ListensOnlyOnTheEndpointWhatever_UrlsOrKestrelConfigSay()
    {
        using TempFactoryRoot root = new();
        WebApplication? app = ServerApp.Create(
            [
                "--FactoryRoot", root.Path,
                "--urls", "http://0.0.0.0:5999",
                "--Kestrel:Endpoints:Other:Url", "http://0.0.0.0:5998",
            ],
            TextWriter.Null);
        Assert.NotNull(app);
        await using WebApplication built = app;

        KestrelServerOptions options = built.Services.GetRequiredService<IOptions<KestrelServerOptions>>().Value;

        Assert.Equal([ServerEndpoint.Address], CodeEndpoints(options));
        Assert.NotNull(options.ConfigurationLoader);
        Assert.Empty(options.ConfigurationLoader.Configuration.GetChildren());
    }

    private static List<IPEndPoint?> CodeEndpoints(KestrelServerOptions options)
    {
        PropertyInfo property = typeof(KestrelServerOptions).GetProperty(
            "CodeBackedListenOptions", BindingFlags.Instance | BindingFlags.NonPublic)!;
        IEnumerable<ListenOptions> listen = (IEnumerable<ListenOptions>)property.GetValue(options)!;
        return listen.Select(item => item.IPEndPoint).ToList();
    }
}
