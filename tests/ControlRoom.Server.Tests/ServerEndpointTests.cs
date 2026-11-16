using System.Net;
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
        using TempTokenFolder tokens = new();
        List<IPEndPoint?> listening = [];
        WebApplication? app = ServerApp.Create(
            [
                "--FactoryRoot", root.Path,
                "--TokenFile", tokens.TokenFile,
                "--urls", "http://0.0.0.0:5999",
                "--Kestrel:Endpoints:Other:Url", "http://0.0.0.0:5998",
            ],
            TextWriter.Null,
            listen => listening.Add(listen.IPEndPoint));
        Assert.NotNull(app);
        await using WebApplication built = app;

        KestrelServerOptions options = built.Services.GetRequiredService<IOptions<KestrelServerOptions>>().Value;

        Assert.Equal([ServerEndpoint.Address], listening);
        Assert.NotNull(options.ConfigurationLoader);
        Assert.Empty(options.ConfigurationLoader.Configuration.GetChildren());
    }
}
