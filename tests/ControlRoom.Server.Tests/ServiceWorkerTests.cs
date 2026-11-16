using System.Net;
using System.Text.RegularExpressions;

namespace ControlRoom.Server.Tests;

/// <summary>Checks <c>sw.js</c>: it caches only the fixed app shell, never API data. The real install, standalone
/// launch and the offline text are checked by hand on the iPhone.</summary>
public sealed partial class ServiceWorkerTests
{
    private static readonly string[] ShellPaths =
    [
        "/", "/index.html", "/app.css", "/app.js", "/manifest.webmanifest", "/icons/apple-touch-icon.png",
        "/icons/icon-192.png", "/icons/icon-512.png",
    ];

    [Fact]
    public async Task Shell_IsExactlyTheFixedList()
    {
        string js = await Fetch();

        Match list = ShellList().Match(js);
        Assert.True(list.Success);
        string[] paths = QuotedString().Matches(list.Groups[1].Value).Select(match => match.Groups[1].Value)
            .ToArray();
        Assert.Equal(ShellPaths, paths);
    }

    [Fact]
    public async Task Cache_IsVersionedAndOldCachesAreDeletedOnActivate()
    {
        string js = await Fetch();

        Assert.Matches(VersionConstant(), js);
        Assert.Contains("const CacheName = \"controlroom-shell-\" + CacheVersion;", js, StringComparison.Ordinal);
        Assert.Contains("cache.addAll(Shell)", js, StringComparison.Ordinal);
        int activate = js.IndexOf("addEventListener(\"activate\"", StringComparison.Ordinal);
        Assert.True(activate >= 0);
        string onActivate = js[activate..];
        Assert.Contains("caches.keys()", onActivate, StringComparison.Ordinal);
        Assert.Contains("name !== CacheName", onActivate, StringComparison.Ordinal);
        Assert.Contains("caches.delete(name)", onActivate, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Fetch_AnswersOnlySameOriginGetShellPathsAndNeverApi()
    {
        string js = await Fetch();

        Assert.Contains("if (request.method !== \"GET\") {", js, StringComparison.Ordinal);
        Assert.Contains("if (url.origin !== self.location.origin) {", js, StringComparison.Ordinal);
        Assert.Contains("if (url.pathname.startsWith(\"/api/\")) {", js, StringComparison.Ordinal);
        Assert.Contains("return Shell.includes(url.pathname);", js, StringComparison.Ordinal);
        Assert.Single(ApiString().Matches(js));
        // respondWith is reached only after the shell check; anything else falls through to the network.
        int check = js.IndexOf("if (!isShellRequest(event.request)) {", StringComparison.Ordinal);
        int respond = js.IndexOf("event.respondWith(", StringComparison.Ordinal);
        Assert.True(check >= 0 && check < respond);
        Assert.Single(RespondWith().Matches(js));
    }

    [Fact]
    public async Task Fetch_IsStaleWhileRevalidateAndStoresOnlyOkResponses()
    {
        string js = await Fetch();

        Assert.Contains("const cached = await cache.match(", js, StringComparison.Ordinal);
        Assert.Contains("const network = fetch(event.request)", js, StringComparison.Ordinal);
        int ok = js.IndexOf("if (response.ok) {", StringComparison.Ordinal);
        int put = js.IndexOf("cache.put(", StringComparison.Ordinal);
        Assert.True(ok >= 0 && ok < put);
        Assert.Single(Put().Matches(js));
        Assert.Contains("return cached;", js, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EveryShellPath_IsServed()
    {
        using TempFactoryRoot root = new();
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateClient();

        foreach (string path in ShellPaths)
        {
            using HttpResponseMessage response = await client.GetAsync(path);
            Assert.True(response.StatusCode == HttpStatusCode.OK, path);
        }
    }

    private static async Task<string> Fetch()
    {
        using TempFactoryRoot root = new();
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateClient();
        return await client.GetStringAsync("/sw.js");
    }

    [GeneratedRegex("const Shell = \\[([^\\]]*)\\];")]
    private static partial Regex ShellList();

    [GeneratedRegex("\"([^\"]*)\"")]
    private static partial Regex QuotedString();

    [GeneratedRegex("const CacheVersion = \"v\\d+\";")]
    private static partial Regex VersionConstant();

    [GeneratedRegex("/api/")]
    private static partial Regex ApiString();

    [GeneratedRegex("respondWith\\(")]
    private static partial Regex RespondWith();

    [GeneratedRegex("\\.put\\(")]
    private static partial Regex Put();
}
