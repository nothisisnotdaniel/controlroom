using System.Net;
using System.Text.RegularExpressions;

namespace ControlRoom.Server.Tests;

/// <summary>Checks <c>sw.js</c>: it caches only the fixed app shell, never API data. The real install, standalone
/// launch and the offline text are checked by hand on the iPhone.</summary>
public sealed partial class ServiceWorkerTests
{
    private static readonly string[] ShellPaths =
    [
        "/", "/index.html", "/app.css", "/app.js", "/board.js", "/act.js", "/edit.js", "/counts.js", "/search.js", "/capture.js", "/history.js", "/doctor.js", "/status.js", "/releases.js", "/live.js", "/push.js", "/token.js", "/room.js", "/manifest.webmanifest", "/icons/apple-touch-icon.png",
        "/icons/icon-192.png", "/icons/icon-512.png",
    ];

    [Fact]
    public async Task Shell_IsExactlyTheFixedList()
    {
        string js = await Fetch("/room.js");

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
        string room = await Fetch("/room.js");

        Assert.Matches(VersionConstant(), room);
        Assert.Contains("const CacheName = \"controlroom-shell-\" + CacheVersion;", room, StringComparison.Ordinal);
        Assert.Contains("cache.addAll(Shell)", js, StringComparison.Ordinal);
        int activate = js.IndexOf("addEventListener(\"activate\"", StringComparison.Ordinal);
        Assert.True(activate >= 0);
        string onActivate = js[activate..];
        Assert.Contains("caches.keys()", onActivate, StringComparison.Ordinal);
        Assert.Contains("name !== CacheName", onActivate, StringComparison.Ordinal);
        Assert.Contains("caches.delete(name)", onActivate, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cache_VersionIsBumpedForTheEditScript()
    {
        string js = await Fetch();
        string room = await Fetch("/room.js");

        Assert.Contains("const CacheVersion = \"v23\";", room, StringComparison.Ordinal);
        Assert.Contains("  \"/edit.js\",\n", room, StringComparison.Ordinal);
        Assert.Contains("  \"/counts.js\",\n", room, StringComparison.Ordinal);
        Assert.Contains("  \"/live.js\",\n", room, StringComparison.Ordinal);
        Assert.Contains("  \"/push.js\",\n", room, StringComparison.Ordinal);
        Assert.Contains("  \"/board.js\",\n", room, StringComparison.Ordinal);
        Assert.Contains("  \"/token.js\",\n", room, StringComparison.Ordinal);
        Assert.DoesNotContain("Authorization", js, StringComparison.Ordinal);
        Assert.DoesNotContain("Authorization", room, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Fetch_AnswersOnlySameOriginGetShellPathsAndNeverApi()
    {
        string js = await Fetch();

        Assert.Contains("if (request.method !== \"GET\") {", js, StringComparison.Ordinal);
        Assert.Contains("if (url.origin !== self.location.origin) {", js, StringComparison.Ordinal);
        Assert.Contains("if (url.pathname.startsWith(\"/api/\") || url.pathname === \"/health\") {", js, StringComparison.Ordinal);
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

        Assert.Contains("const key = new URL(event.request.url).pathname;", js, StringComparison.Ordinal);
        Assert.Contains("const cached = await cache.match(key);", js, StringComparison.Ordinal);
        Assert.Contains("await cache.put(key, response.clone());", js, StringComparison.Ordinal);
        Assert.Contains("const network = fetch(event.request)", js, StringComparison.Ordinal);
        int ok = js.IndexOf("if (response.ok) {", StringComparison.Ordinal);
        int put = js.IndexOf("cache.put(", StringComparison.Ordinal);
        Assert.True(ok >= 0 && ok < put);
        Assert.Single(Put().Matches(js));
        Assert.Contains("return cached;", js, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Push_ShowsANotificationFromFixedTitlesAndPayloadFields()
    {
        string js = await Fetch();

        int push = js.IndexOf("self.addEventListener(\"push\", (event) => {", StringComparison.Ordinal);
        Assert.True(push >= 0);
        Assert.Contains("event.waitUntil(self.registration.showNotification(note.title, note.options));", js,
            StringComparison.Ordinal);
        Assert.Contains("\"needs-human\": \"Needs you\",", js, StringComparison.Ordinal);
        Assert.Contains("\"merge-conflict\": \"Merge conflict\",", js, StringComparison.Ordinal);
        Assert.Contains("\"ci-failed\": \"CI failed after merge\",", js, StringComparison.Ordinal);
        Assert.Contains("done: \"Done\",", js, StringComparison.Ordinal);
        Assert.Contains("approval: \"New items to approve\",", js, StringComparison.Ordinal);
        Assert.Contains("test: \"Test notification\",", js, StringComparison.Ordinal);
        Assert.Contains("update: \"Update available\",", js, StringComparison.Ordinal);
        Assert.Contains("tag = \"update:\" + version;", js, StringComparison.Ordinal);
        Assert.Contains("title: kind ? PushTitles[kind] : PushFallbackTitle,", js, StringComparison.Ordinal);
        Assert.Contains("tag = \"approval:\" + project;", js, StringComparison.Ordinal);
        Assert.Contains("tag = kind + \":\" + job;", js, StringComparison.Ordinal);
        Assert.DoesNotContain("data.reason", js, StringComparison.Ordinal);
        Assert.DoesNotContain("innerHTML", js, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NotificationClick_OpensTheJobOrBacklogsAndFetchesNothing()
    {
        string js = await Fetch();

        int click = js.IndexOf("self.addEventListener(\"notificationclick\", (event) => {", StringComparison.Ordinal);
        Assert.True(click >= 0);
        Assert.Contains("url = \"/#job/\" + job;", js, StringComparison.Ordinal);
        Assert.Contains("url = \"/#backlogs\";", js, StringComparison.Ordinal);
        Assert.Contains("const PushJobPattern = /^[0-9]{1,6}-[a-z0-9-]{1,80}(\\.[0-9a-f]{32})?$/;", js, StringComparison.Ordinal);
        string onClick = js[click..];
        Assert.Contains("self.clients.openWindow(url)", onClick, StringComparison.Ordinal);
        Assert.Contains("data.url.startsWith(\"/#\")", onClick, StringComparison.Ordinal);
        int push = js.IndexOf("// Push:", StringComparison.Ordinal);
        Assert.True(push >= 0);
        Assert.DoesNotContain("fetch(", js[push..], StringComparison.Ordinal);
        Assert.DoesNotContain("/api/", js[push..], StringComparison.Ordinal);
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

    private static async Task<string> Fetch(string path = "/sw.js")
    {
        using TempFactoryRoot root = new();
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateClient();
        return await client.GetStringAsync(path);
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
