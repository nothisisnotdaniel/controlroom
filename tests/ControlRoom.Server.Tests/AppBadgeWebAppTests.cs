using System.Text.RegularExpressions;

namespace ControlRoom.Server.Tests;

/// <summary>The app badge (#476): <c>app.js</c> sets the iPhone app badge to the blocked job count through the
/// <c>jobCounts</c> host hook and RoomKit's <c>setRoomKitBadge</c>, read as text (there is no JS test runtime).</summary>
public sealed class AppBadgeWebAppTests
{
    private const string BadgeHook = "roomKitHooks.on(\"jobCounts\", (jobs) => {\n"
        + "  const count = appBadgeCount(jobs);\n"
        + "  if (count !== null) {\n"
        + "    return setRoomKitBadge(count);\n"
        + "  }\n"
        + "  return undefined;\n"
        + "});\n";

    private const string OpenJobHook = "roomKitHooks.on(\"jobCounts\", (jobs) => {\n  refreshOpenJob(jobs);\n});\n";

    [Fact]
    public async Task AppJs_AppBadgeCountIsTheBlockedCountOfCountJobsCappedAt999OrNull()
    {
        string app = await Fetch("/app.js");

        string body = FunctionBody(app, "function appBadgeCount(jobs) {");
        Assert.Contains("const counts = countJobs(jobs);\n  if (counts === null) {\n    return null;\n  }\n"
                        + "  return Math.min(counts.blocked, 999);", body, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(app, Regex.Escape("function appBadgeCount(jobs) {")));
        // The same count as the #blocked-count tab badge: RoomKit's countJobs, over every project.
        string counts = await Fetch("/counts.js");
        Assert.Contains("function countJobs(jobs) {", counts, StringComparison.Ordinal);
        Assert.DoesNotContain("selectedProject", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AppJs_RegistersTheBadgeHookDirectlyAfterTheOpenJobHookAtLoadTime()
    {
        string app = await Fetch("/app.js");

        Assert.Contains(OpenJobHook + BadgeHook + "roomKitHooks.on(\"liveChecked\", ", app, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(app, Regex.Escape(BadgeHook)));
        Assert.Equal(2, Regex.Matches(app, Regex.Escape("roomKitHooks.on(\"jobCounts\"")).Count);
        // At load time: top level, before the first counts read.
        int hook = app.IndexOf(BadgeHook, StringComparison.Ordinal);
        Assert.Equal('\n', app[hook - 1]);
        Assert.True(hook < app.LastIndexOf("\nloadJobCounts();", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PushJs_TheRoomKitPackageDefinesSetRoomKitBadge()
    {
        string push = await Fetch("/push.js");

        Assert.Contains("async function setRoomKitBadge(count) {", push, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AppFiles_CallNoBadgingApiDirectlyAndSetRoomKitBadgeOnlyOnceInAppJs()
    {
        string folder = Path.Combine(AccessTokenTests.SourceFolder(), "wwwroot");
        string[] files = [.. Directory.GetFiles(folder, "*.js").Select(Path.GetFileName).OfType<string>()
            .Order(StringComparer.Ordinal)];
        Assert.Contains("app.js", files);
        using TempFactoryRoot root = new();
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateClient();
        foreach (string file in files)
        {
            string js = await client.GetStringAsync("/" + file);
            Assert.False(js.Contains("navigator.setAppBadge", StringComparison.Ordinal), file);
            Assert.False(js.Contains("navigator.clearAppBadge", StringComparison.Ordinal), file);
            Assert.False(Regex.IsMatch(js, @"(?<![A-Za-z])setAppBadge\("), file);
            int calls = Regex.Matches(js, Regex.Escape("setRoomKitBadge(")).Count;
            Assert.True(calls == (file == "app.js" ? 1 : 0), file + " calls setRoomKitBadge " + calls + " times");
            Assert.False(Regex.IsMatch(js, @"(^|[^.\w])(setRoomKitBadge|countJobs|roomKitHooks)\s*=[^=]"), file);
        }
    }

    [Fact]
    public async Task IndexHtml_LoadsPushAndCountsBeforeAppAllDeferred()
    {
        string html = await Fetch("/index.html");

        int push = html.IndexOf("<script src=\"/push.js\" defer></script>", StringComparison.Ordinal);
        int counts = html.IndexOf("<script src=\"/counts.js\" defer></script>", StringComparison.Ordinal);
        int app = html.IndexOf("<script src=\"/app.js\" defer></script>", StringComparison.Ordinal);
        Assert.True(push >= 0 && counts >= 0 && app >= 0);
        Assert.True(push < app && counts < app);
    }

    [Fact]
    public void ClaudeMd_DescribesTheBadgeItsIosLimitsAndThatPushesCarryNoBadge()
    {
        string text = File.ReadAllText(Path.Combine(RepoCopy.RepoFolder(), "CLAUDE.md"))
            .Replace("\r\n", "\n", StringComparison.Ordinal);

        Assert.Contains("`setRoomKitBadge`", text, StringComparison.Ordinal);
        Assert.Contains("`appBadgeCount`", text, StringComparison.Ordinal);
        Assert.Contains("999", text, StringComparison.Ordinal);
        Assert.Contains("Home Screen", text, StringComparison.Ordinal);
        Assert.Contains("notification permission", text, StringComparison.Ordinal);
        Assert.Contains("pushes do not carry `badge` yet", text, StringComparison.Ordinal);
    }

    private static string FunctionBody(string js, string start)
    {
        int from = js.IndexOf(start, StringComparison.Ordinal);
        Assert.True(from >= 0, start + " not found");
        int end = js.IndexOf("\n}\n", from, StringComparison.Ordinal);
        Assert.True(end > from);
        return js[from..end];
    }

    private static async Task<string> Fetch(string path)
    {
        using TempFactoryRoot root = new();
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateClient();
        string text = await client.GetStringAsync(path);
        return text.Replace("\r\n", "\n", StringComparison.Ordinal);
    }
}
