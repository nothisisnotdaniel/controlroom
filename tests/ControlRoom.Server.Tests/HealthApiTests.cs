using System.Net;

namespace ControlRoom.Server.Tests;

/// <summary><c>/health</c>: the one public route beside the app shell. It answers only the running version, still
/// after the Host check and the method rule; every <c>/api/</c> route still needs the token.</summary>
public sealed class HealthApiTests
{
    [Fact]
    public async Task Get_WithoutAnyToken_ReturnsOnlyTheVersion()
    {
        using TempFactoryRoot root = new();
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(HealthEndpoint.Route);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal("{\"version\":\"" + AppVersion.Current + "\"}", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Head_WithoutAnyToken_Is200WithNoStore()
    {
        using TempFactoryRoot root = new();
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateClient();
        using HttpRequestMessage request = new(HttpMethod.Head, HealthEndpoint.Route);

        using HttpResponseMessage response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
    }

    [Fact]
    public async Task BadHost_Is421()
    {
        using TempFactoryRoot root = new();
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateClient();
        using HttpRequestMessage request = new(HttpMethod.Get, HealthEndpoint.Route);
        request.Headers.Host = "evil.example";

        using HttpResponseMessage response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.MisdirectedRequest, response.StatusCode);
        Assert.DoesNotContain("version", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    public async Task OtherMethods_Get405(string method)
    {
        using TempFactoryRoot root = new();
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateClient();
        using HttpRequestMessage request = new(new HttpMethod(method), HealthEndpoint.Route)
        {
            Content = new StringContent("{}"),
        };

        using HttpResponseMessage response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/version")]
    [InlineData("/api/update")]
    [InlineData("/api/board")]
    public async Task OtherRoutes_StillNeedTheToken(string path)
    {
        using TempFactoryRoot root = new();
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
