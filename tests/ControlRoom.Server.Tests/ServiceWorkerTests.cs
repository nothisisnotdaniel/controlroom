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
        int activate = js.IndexOf("addEventListener(\"activate\"", StringComparison.Ordinal);
        Assert.True(activate >= 0);
        string onActivate = js[activate..js.IndexOf("function isShellRequest(", StringComparison.Ordinal)];
        Assert.Contains("caches.keys()", onActivate, StringComparison.Ordinal);
        Assert.Contains("name !== CacheName", onActivate, StringComparison.Ordinal);
        Assert.Contains("caches.delete(name)", onActivate, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Install_PrecachesTheShellPastTheHttpCacheAndTakesOverAtOnce()
    {
        string js = await Fetch();

        int install = js.IndexOf("self.addEventListener(\"install\", (event) => {", StringComparison.Ordinal);
        int activate = js.IndexOf("self.addEventListener(\"activate\", (event) => {", StringComparison.Ordinal);
        Assert.True(install >= 0 && install < activate);
        string onInstall = js[install..activate];
        Assert.Contains("event.waitUntil(caches.open(CacheName)", onInstall, StringComparison.Ordinal);
        Assert.Contains("cache.addAll(Shell.map((path) => new Request(path, { cache: \"no-cache\" })))", onInstall,
            StringComparison.Ordinal);
        int addAll = onInstall.IndexOf("cache.addAll(", StringComparison.Ordinal);
        int skip = onInstall.IndexOf(".then(() => self.skipWaiting())", StringComparison.Ordinal);
        Assert.True(addAll >= 0 && addAll < skip);
        Assert.DoesNotContain("cache.addAll(Shell)", js, StringComparison.Ordinal);
        Assert.Single(SkipWaiting().Matches(js));
    }

    [Fact]
    public async Task Activate_DeletesEveryOtherCacheThenClaimsTheOpenPages()
    {
        string js = await Fetch();

        int activate = js.IndexOf("self.addEventListener(\"activate\", (event) => {", StringComparison.Ordinal);
        int end = js.IndexOf("function isShellRequest(", StringComparison.Ordinal);
        Assert.True(activate >= 0 && activate < end);
        string onActivate = js[activate..end];
        Assert.Contains("event.waitUntil(", onActivate, StringComparison.Ordinal);
        Assert.Contains("names.filter((name) => name !== CacheName).map((name) => caches.delete(name))", onActivate,
            StringComparison.Ordinal);
        int delete = onActivate.IndexOf("caches.delete(name)", StringComparison.Ordinal);
        int claim = onActivate.IndexOf(".then(() => self.clients.claim())", StringComparison.Ordinal);
        Assert.True(delete >= 0 && delete < claim);
        Assert.Single(ClientsClaim().Matches(js));
    }

    [Fact]
    public async Task Cache_VersionIsBumpedForNetworkFirst()
    {
        string js = await Fetch();
        string room = await Fetch("/room.js");

        Assert.Contains("const CacheVersion = \"v25\";", room, StringComparison.Ordinal);
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
    public async Task Fetch_IsNetworkFirstAndStoresOnlyOkResponses()
    {
        string js = await Fetch();

        int start = js.IndexOf("async function networkFirst(event) {", StringComparison.Ordinal);
        int end = js.IndexOf("self.addEventListener(\"fetch\"", StringComparison.Ordinal);
        Assert.True(start >= 0 && start < end);
        string body = js[start..end];
        Assert.Contains("event.respondWith(networkFirst(event));", js, StringComparison.Ordinal);
        Assert.DoesNotContain("staleWhileRevalidate", js, StringComparison.Ordinal);
        Assert.Contains("const key = new URL(event.request.url).pathname;", body, StringComparison.Ordinal);

        // The network is asked first, past the HTTP cache.
        int network = body.IndexOf("response = await fetch(event.request, { cache: \"no-cache\" });", StringComparison.Ordinal);
        int rejected = body.IndexOf("} catch {", StringComparison.Ordinal);
        int match = body.IndexOf("const cached = await cache.match(key);", StringComparison.Ordinal);
        int fallback = body.IndexOf("return cached || Response.error();", StringComparison.Ordinal);
        int ok = body.IndexOf("if (response.ok) {", StringComparison.Ordinal);
        int put = body.IndexOf("await cache.put(key, response.clone());", StringComparison.Ordinal);
        int answer = body.IndexOf("return response;", StringComparison.Ordinal);
        Assert.True(network >= 0 && network < rejected, "fetch comes first");
        // cache.match is read only inside the rejection branch, which returns before any ok/put code.
        Assert.True(rejected < match && match < fallback && fallback < ok, "cache only when the fetch rejects");
        Assert.True(ok < put && put < answer, "only ok responses are stored, every answer is returned");
        Assert.Single(CacheMatch().Matches(js));
        Assert.Single(Put().Matches(js));
        Assert.Single(FetchCall().Matches(js));
        Assert.DoesNotContain("return cached;", js, StringComparison.Ordinal);
        Assert.Equal(1, CountOf(body, "return cached"));
        Assert.Equal(2, CountOf(body, "return "));
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

    [GeneratedRegex("\\.match\\(")]
    private static partial Regex CacheMatch();

    [GeneratedRegex("\\bfetch\\(")]
    private static partial Regex FetchCall();

    [GeneratedRegex("self\\.skipWaiting\\(\\)")]
    private static partial Regex SkipWaiting();

    [GeneratedRegex("self\\.clients\\.claim\\(\\)")]
    private static partial Regex ClientsClaim();

    private static int CountOf(string text, string part)
    {
        int count = 0;
        for (int at = text.IndexOf(part, StringComparison.Ordinal); at >= 0;
             at = text.IndexOf(part, at + part.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }
}
