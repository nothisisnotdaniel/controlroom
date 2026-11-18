using System.Net;
using Microsoft.Extensions.DependencyInjection;

namespace ControlRoom.Server.Tests;

/// <summary><c>GET /api/update</c> through the hosted server: each test awaits the first check on the resolved
/// <see cref="UpdateChecker"/> before asking.</summary>
public sealed class UpdateApiTests
{
    [Fact]
    public async Task NewerRelease_IsAvailableWithTheInstallCommand()
    {
        using TempFactoryRoot root = new();
        using ServerFactory factory = new(root.Path) { CurrentVersion = "1.0.0" };
        factory.Update.RespondWithTag("v1.2.0");
        using HttpClient client = factory.CreateTokenClient();
        await factory.Services.GetRequiredService<UpdateChecker>().Check();

        using HttpResponseMessage response = await client.GetAsync(UpdateEndpoint.Route);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal(
            "{\"current\":\"1.0.0\",\"available\":true,\"latest\":\"1.2.0\","
            + "\"url\":\"https://github.com/nothisisnotdaniel/controlroom/releases/tag/v1.2.0\","
            + "\"command\":\"scripts/launch-agent.sh install --release v1.2.0\",\"autoUpdate\":false}",
            await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task WithATailnetName_TheCommandAddsTheTailnetHost()
    {
        using TempFactoryRoot root = new();
        using ServerFactory factory = new(root.Path, null, "mac.tail1234.ts.net") { CurrentVersion = "1.0.0-rc.1" };
        factory.Update.RespondWithTag("v1.0.0");
        using HttpClient client = factory.CreateTokenClient();
        await factory.Services.GetRequiredService<UpdateChecker>().Check();

        string json = await client.GetStringAsync(UpdateEndpoint.Route);

        Assert.Equal(
            "{\"current\":\"1.0.0-rc.1\",\"available\":true,\"latest\":\"1.0.0\","
            + "\"url\":\"https://github.com/nothisisnotdaniel/controlroom/releases/tag/v1.0.0\","
            + "\"command\":\"scripts/launch-agent.sh install --release v1.0.0 --tailnet-host mac.tail1234.ts.net\",\"autoUpdate\":false}",
            json);
    }

    [Theory]
    [InlineData("1.2.0", "v1.2.0")]
    [InlineData("1.2.0", "v1.1.9")]
    [InlineData("1.2.0", "nope")]
    [InlineData("dev", "v9.0.0")]
    public async Task NoNewerRelease_IsNotAvailableAndHasNoLatestUrlOrCommand(string current, string tag)
    {
        using TempFactoryRoot root = new();
        using ServerFactory factory = new(root.Path, null, "mac.tail1234.ts.net") { CurrentVersion = current };
        factory.Update.RespondWithTag(tag);
        using HttpClient client = factory.CreateTokenClient();
        await factory.Services.GetRequiredService<UpdateChecker>().Check();

        string json = await client.GetStringAsync(UpdateEndpoint.Route);

        Assert.Equal(
            "{\"current\":\"" + current + "\",\"available\":false,\"latest\":null,\"url\":null,\"command\":null,\"autoUpdate\":false}", json);
    }

    [Fact]
    public async Task WithoutTheToken_Is401()
    {
        using TempFactoryRoot root = new();
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(UpdateEndpoint.Route);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("{\"error\":\"unauthorized\"}", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Post_IsRefused()
    {
        using TempFactoryRoot root = new();
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();

        using HttpResponseMessage response = await client.PostAsync(UpdateEndpoint.Route, null);

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }
}
