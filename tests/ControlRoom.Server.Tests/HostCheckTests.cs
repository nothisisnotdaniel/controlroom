using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;

namespace ControlRoom.Server.Tests;

/// <summary>The <c>Host</c> check (DNS-rebinding defence): only <c>127.0.0.1:8100</c>, <c>localhost:8100</c> and the
/// configured tailnet name are handled; every other <c>Host</c> gets the fixed <c>421</c> before anything else.</summary>
public sealed class HostCheckTests
{
    private const string ExpectedPolicy =
        "default-src 'none'; script-src 'self'; style-src 'self'; img-src 'self'; connect-src 'self'; "
        + "manifest-src 'self'; worker-src 'self'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'";

    private const string Tailnet = "mac.tail1234.ts.net";

    private const string ChangeRoute = "/api/backlog/change";

    private const string Backlog = "# Alpha backlog\n\n- [?] [#1](backlog/001-one.md) Proposal one\n";

    public static TheoryData<string> RefusedHosts()
    {
        return
        [
            "evil.example", "evil.example:8100", "127.0.0.1", "localhost", "127.0.0.1:80", "localhost:8101",
            "[::1]:8100", "0.0.0.0:8100", "localhost.:8100", "127.0.0.1:8100.evil.example", Tailnet,
        ];
    }

    public static TheoryData<string> RefusedPaths()
    {
        return ["/", "/index.html", "/sw.js", "/api/board"];
    }

    [Theory]
    [InlineData("127.0.0.1:8100")]
    [InlineData("localhost:8100")]
    [InlineData("LOCALHOST:8100")]
    [InlineData("LocalHost:8100")]
    public async Task LoopbackHosts_AreHandledAsBefore(string host)
    {
        using TempFactoryRoot root = new();
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();

        await AssertHandled(client, host);
    }

    [Theory]
    [MemberData(nameof(RefusedHosts))]
    public async Task OtherHosts_GetTheFixed421OnEveryPath(string host)
    {
        using TempFactoryRoot root = new();
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();

        foreach (string path in RefusedPaths())
        {
            using HttpResponseMessage response = await Send(client, HttpMethod.Get, path, host, null);
            await AssertRefused(response);
        }

        using HttpResponseMessage post = await Send(client, HttpMethod.Post, ChangeRoute, host, Json("{}"));
        await AssertRefused(post);
    }

    [Fact]
    public void EmptyOrMissingHost_IsRefused()
    {
        Assert.False(HostCheck.IsAllowed(""));
        Assert.False(HostCheck.IsAllowed(null));
        Assert.False(HostCheck.IsAllowed("", Tailnet));
        Assert.False(HostCheck.IsAllowed(null, Tailnet));
        Assert.True(HostCheck.IsAllowed("127.0.0.1:8100"));
    }

    [Fact]
    public async Task RefusedApiRequests_WriteNothingSendNoPushAndNeverReachTheTokenCheck()
    {
        using TempFactoryRoot root = new();
        string folder = root.AddProject("Alpha", Backlog);
        TempFactoryRoot.AddStory(folder, "001-one.md", "# 001 Proposal one\n");
        TempFactoryRoot.InitGit(folder, "develop");
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();
        using PushReceiver receiver = new();
        (await PushTestData.Subscribe(client, receiver, PushTestData.AppleEndpoint)).Dispose();
        string tokenFolder = Path.GetDirectoryName(factory.TokenFile)!;
        IReadOnlyList<string> rootBefore = root.Snapshot();
        IReadOnlyList<string> tokensBefore = FolderListing(tokenFolder);
        string version = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Backlog))).ToLowerInvariant();
        string change = JsonSerializer.Serialize(new { project = "Alpha", version, action = "approve", line = 2 });

        using HttpResponseMessage board = await Send(client, HttpMethod.Get, "/api/board", "evil.example", null);
        using HttpResponseMessage write = await Send(client, HttpMethod.Post, ChangeRoute, "evil.example", Json(change));
        using HttpResponseMessage test = await Send(
            client, HttpMethod.Post, PushEndpoint.TestRoute, "evil.example", null);
        using HttpClient noToken = factory.CreateClient();
        using HttpResponseMessage anonymous = await Send(noToken, HttpMethod.Get, "/api/board", "evil.example", null);

        await AssertRefused(board);
        await AssertRefused(write);
        await AssertRefused(test);
        await AssertRefused(anonymous);
        Assert.Equal(rootBefore, root.Snapshot());
        Assert.Equal(tokensBefore, FolderListing(tokenFolder));
        Assert.Empty(factory.Push.Requests);
    }

    [Fact]
    public async Task ApprovedChange_StillWorksWithALoopbackHost()
    {
        using TempFactoryRoot root = new();
        string folder = root.AddProject("Alpha", Backlog);
        TempFactoryRoot.AddStory(folder, "001-one.md", "# 001 Proposal one\n");
        TempFactoryRoot.InitGit(folder, "develop");
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();
        string version = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Backlog))).ToLowerInvariant();
        string change = JsonSerializer.Serialize(new { project = "Alpha", version, action = "approve", line = 2 });

        using HttpResponseMessage write = await Send(client, HttpMethod.Post, ChangeRoute, "localhost:8100", Json(change));

        Assert.Equal(HttpStatusCode.OK, write.StatusCode);
    }

    [Theory]
    [InlineData(Tailnet)]
    [InlineData("MAC.Tail1234.TS.NET")]
    [InlineData("127.0.0.1:8100")]
    public async Task ConfiguredTailnetHost_IsHandled(string host)
    {
        using TempFactoryRoot root = new();
        using ServerFactory factory = new(root.Path, null, Tailnet);
        using HttpClient client = factory.CreateTokenClient();

        await AssertHandled(client, host);
    }

    [Theory]
    [InlineData("mac.tail1234.ts.net:443")]
    [InlineData("mac.tail1234.ts.net:8100")]
    [InlineData("other.tail1234.ts.net")]
    [InlineData("mac.tail1234.ts.net.evil.example")]
    [InlineData("evil.example")]
    public async Task OtherNames_AreRefusedWithATailnetHost(string host)
    {
        using TempFactoryRoot root = new();
        using ServerFactory factory = new(root.Path, null, Tailnet);
        using HttpClient client = factory.CreateTokenClient();

        foreach (string path in RefusedPaths())
        {
            using HttpResponseMessage response = await Send(client, HttpMethod.Get, path, host, null);
            await AssertRefused(response);
        }
    }

    [Theory]
    [InlineData("evil.example")]
    [InlineData("ts.net")]
    [InlineData("mac.ts.net")]
    [InlineData("mac.tail.ts.net:443")]
    [InlineData("*.tail.ts.net")]
    [InlineData("MAC.tail.ts.net")]
    [InlineData("mac..tail.ts.net")]
    [InlineData("-mac.tail.ts.net")]
    [InlineData("mac-.tail.ts.net")]
    [InlineData("mac.tail.ts.net.")]
    [InlineData("mac_x.tail.ts.net")]
    [InlineData("mac.tail.ts.net\n")]
    [InlineData("")]
    public void InvalidTailnetHost_StopsTheServerWithAFixedText(string value)
    {
        using TempTokenFolder tokens = new();
        using TempFactoryRoot root = new();
        StringWriter error = new();

        WebApplication? app = ServerApp.Create(
            ["--FactoryRoot", root.Path, "--TokenFile", tokens.TokenFile, "--TailnetHost", value], error);

        Assert.Null(app);
        Assert.Equal(HostCheck.InvalidTailnetText + Environment.NewLine, error.ToString());
        if (value.Length > 0)
        {
            // The fixed text itself names an example (which contains "ts.net"), so only the rest is checked.
            string rest = error.ToString().Replace(HostCheck.InvalidTailnetText, "", StringComparison.Ordinal);
            Assert.DoesNotContain(value, rest, StringComparison.Ordinal);
        }

        Assert.False(File.Exists(tokens.TokenFile));
    }

    [Fact]
    public void TailnetHostRule_AcceptsValidNamesOnly()
    {
        Assert.True(HostCheck.IsValidTailnetHost(Tailnet));
        Assert.True(HostCheck.IsValidTailnetHost("my-mac.tail-1.ts.net"));
        Assert.True(HostCheck.IsValidTailnetHost("a.b.c.ts.net"));
        Assert.True(HostCheck.IsValidTailnetHost(new string('a', 63) + ".tail.ts.net"));
        Assert.False(HostCheck.IsValidTailnetHost(new string('a', 64) + ".tail.ts.net"));
        Assert.False(HostCheck.IsValidTailnetHost(null));
    }

    [Fact]
    public async Task ValidTailnetHost_StartsTheServer()
    {
        using TempTokenFolder tokens = new();
        using TempFactoryRoot root = new();
        StringWriter error = new();

        WebApplication? app = ServerApp.Create(
            ["--FactoryRoot", root.Path, "--TokenFile", tokens.TokenFile, "--TailnetHost", Tailnet], error);

        Assert.NotNull(app);
        await app.DisposeAsync();
        Assert.Equal(AccessToken.CreatedText + Environment.NewLine, error.ToString());
    }

    [Fact]
    public async Task RefusedHost_IsNotLogged()
    {
        using TempFactoryRoot root = new();
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();

        using HttpResponseMessage page = await Send(client, HttpMethod.Get, "/", "rebind-marker.example", null);
        using HttpResponseMessage api = await Send(client, HttpMethod.Get, "/api/board", "rebind-marker.example", null);

        await AssertRefused(page);
        await AssertRefused(api);
        Assert.DoesNotContain(
            factory.Logs.Lines, line => line.Contains("rebind-marker", StringComparison.OrdinalIgnoreCase));
    }

    private static async Task AssertHandled(HttpClient client, string host)
    {
        using HttpResponseMessage page = await Send(client, HttpMethod.Get, "/", host, null);
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Equal("text/html", page.Content.Headers.ContentType?.MediaType);

        using HttpResponseMessage board = await Send(client, HttpMethod.Get, "/api/board", host, null);
        Assert.Equal(HttpStatusCode.OK, board.StatusCode);
        Assert.NotEqual(HostCheck.RefusedBody, await board.Content.ReadAsStringAsync());
    }

    private static async Task AssertRefused(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.MisdirectedRequest, response.StatusCode);
        Assert.Equal(HostCheck.RefusedBody, await response.Content.ReadAsStringAsync());
        Assert.Equal("application/json; charset=utf-8", response.Content.Headers.ContentType?.ToString());
        Assert.Equal("no-store", string.Join(",", response.Headers.GetValues("Cache-Control")));
        Assert.Equal(ExpectedPolicy, string.Join(",", response.Headers.GetValues("Content-Security-Policy")));
        Assert.Equal("nosniff", string.Join(",", response.Headers.GetValues("X-Content-Type-Options")));
        Assert.False(response.Headers.Contains("WWW-Authenticate"));
    }

    private static async Task<HttpResponseMessage> Send(
        HttpClient client, HttpMethod method, string path, string host, HttpContent? content)
    {
        using HttpRequestMessage request = new(method, path) { Content = content };
        // Without validation, so a value HttpClient would refuse (e.g. a port followed by a name) is sent as is.
        Assert.True(request.Headers.TryAddWithoutValidation("Host", host));
        return await client.SendAsync(request);
    }

    private static StringContent Json(string json)
    {
        return new StringContent(json, Encoding.UTF8, "application/json");
    }

    private static List<string> FolderListing(string folder)
    {
        List<string> entries = [];
        foreach (string entry in Directory.EnumerateFileSystemEntries(folder, "*", SearchOption.AllDirectories))
        {
            FileInfo info = new(entry);
            string hash = info.Exists ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(entry))) : "";
            entries.Add(Path.GetRelativePath(folder, entry) + "|" + info.LastWriteTimeUtc.Ticks + "|" + hash);
        }

        entries.Sort(StringComparer.Ordinal);
        return entries;
    }
}
