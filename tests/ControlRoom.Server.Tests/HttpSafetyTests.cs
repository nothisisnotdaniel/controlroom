using System.Net;

namespace ControlRoom.Server.Tests;

public sealed class HttpSafetyTests
{
    private const string ExpectedPolicy =
        "default-src 'none'; script-src 'self'; style-src 'self'; img-src 'self'; connect-src 'self'; "
        + "base-uri 'none'; form-action 'none'; frame-ancestors 'none'";

    public static TheoryData<string, string> WriteRequests()
    {
        TheoryData<string, string> data = [];
        foreach (string method in new[] { "POST", "PUT", "PATCH", "DELETE", "OPTIONS", "TRACE" })
        {
            foreach (string path in new[]
                     {
                         "/", "/api/backlogs", "/api/story?project=Normal&number=1", "/index.html",
                         "/Normal/backlog.md", "/x",
                     })
            {
                data.Add(method, path);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(WriteRequests))]
    public async Task OtherMethods_AreRefusedAndWriteNothing(string method, string path)
    {
        using TempFactoryRoot root = TempFactoryRoot.WithSamples();
        IReadOnlyList<string> before = root.Snapshot();
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateClient();
        using HttpRequestMessage request = new(new HttpMethod(method), path)
        {
            Content = new StringContent("- [ ] Injected: line\n"),
        };

        using HttpResponseMessage response = await client.SendAsync(request);

        Assert.Contains(response.StatusCode, new[] { HttpStatusCode.MethodNotAllowed, HttpStatusCode.NotFound });
        Assert.Equal(before, root.Snapshot());
    }

    [Theory]
    [InlineData("/CLAUDE.md")]
    [InlineData("/factory/CLAUDE.md")]
    [InlineData("/Normal/backlog.md")]
    [InlineData("/Times/backlog.md")]
    [InlineData("/../x")]
    [InlineData("/%2e%2e/CLAUDE.md")]
    [InlineData("/..%2fCLAUDE.md")]
    [InlineData("/api/backlogs/Normal")]
    public async Task FactoryRootFiles_AreNeverServed(string path)
    {
        using TempFactoryRoot root = TempFactoryRoot.WithSamples();
        root.AddProject("Times", "- [ ] Times: line\n");
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Root_ServesIndexHtml()
    {
        using TempFactoryRoot root = new();
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("<main id=\"backlogs\">", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("GET", "/")]
    [InlineData("GET", "/index.html")]
    [InlineData("GET", "/app.js")]
    [InlineData("GET", "/app.css")]
    [InlineData("GET", "/api/backlogs")]
    [InlineData("GET", "/missing")]
    [InlineData("HEAD", "/")]
    [InlineData("POST", "/api/backlogs")]
    public async Task EveryResponse_CarriesCspAndNoSniff(string method, string path)
    {
        using TempFactoryRoot root = new();
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateClient();

        using HttpRequestMessage request = new(new HttpMethod(method), path);
        using HttpResponseMessage response = await client.SendAsync(request);

        Assert.Equal(ExpectedPolicy, string.Join(",", response.Headers.GetValues("Content-Security-Policy")));
        Assert.Equal("nosniff", string.Join(",", response.Headers.GetValues("X-Content-Type-Options")));
    }
}
