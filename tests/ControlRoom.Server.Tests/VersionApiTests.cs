using System.Net;

namespace ControlRoom.Server.Tests;

/// <summary><c>GET /api/version</c> and the <see cref="AppVersion"/> mapping. The test build has no
/// <c>-p:Version</c>, so it reports <c>dev</c>.</summary>
public sealed class VersionApiTests
{
    [Fact]
    public async Task Version_WithTheToken_ReturnsDevWithNoStore()
    {
        using TempFactoryRoot root = new();
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();

        using HttpResponseMessage response = await client.GetAsync("/api/version");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal("{\"version\":\"dev\"}", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Version_WithoutTheToken_Is401()
    {
        using TempFactoryRoot root = new();
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync("/api/version");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("{\"error\":\"unauthorized\"}", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    [InlineData("PATCH")]
    public async Task Version_OtherMethods_Get405(string method)
    {
        using TempFactoryRoot root = new();
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();
        using HttpRequestMessage request = new(new HttpMethod(method), "/api/version")
        {
            Content = new StringContent("{}"),
        };

        using HttpResponseMessage response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    [Fact]
    public void Current_IsDevForABuildWithoutAVersion()
    {
        Assert.Equal("dev", AppVersion.Current);
    }

    [Theory]
    [InlineData("1.2.3", "1.2.3")]
    [InlineData("1.2.3+abc", "1.2.3")]
    [InlineData("1.2.3-rc.1", "1.2.3-rc.1")]
    [InlineData("1.2.3-rc.1+abc.def", "1.2.3-rc.1")]
    [InlineData("dev", "dev")]
    [InlineData("", "dev")]
    [InlineData(null, "dev")]
    [InlineData("+abc", "dev")]
    public void FromMetadata_DropsBuildMetadataAndDefaultsToDev(string? metadata, string expected)
    {
        Assert.Equal(expected, AppVersion.FromMetadata(metadata));
    }
}
