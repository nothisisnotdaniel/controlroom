using System.Net;
using System.Text.RegularExpressions;

namespace ControlRoom.Server.Tests;

/// <summary>Checks <c>releases.js</c> and its hooks in <c>index.html</c>, <c>app.js</c>, <c>live.js</c>,
/// <c>app.css</c> and <c>sw.js</c> as the served text (there is no JS test runtime, as in
/// <see cref="StatusWebAppTests"/>). The look is checked by hand at 375 px in light and dark mode.</summary>
public sealed partial class ReleasesWebAppTests
{
    [Fact]
    public async Task IndexHtml_LoadsReleasesJsAfterStatusJsAndBeforeLiveJs()
    {
        string html = await Fetch("/index.html");

        Assert.Contains(
            "  <script src=\"/status.js\" defer></script>\n  <script src=\"/releases.js\" defer></script>\n"
            + "  <script src=\"/live.js\" defer></script>\n  <script src=\"/app.js\" defer></script>",
            html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReleasesJs_IsServed()
    {
        using TempFactoryRoot root = new();
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync("/releases.js");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/javascript", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task ReleasesJs_HasEveryFixedText()
    {
        string js = await Fetch("/releases.js");
        string texts = Between(js, "const ReleaseTexts = {", "\n};\n");

        foreach (string text in new[]
                 {
                     "failed: \"Releases unavailable.\"", "unavailable: \"Release info unavailable\"",
                     "lastRelease: \"Last release \"", "noRelease: \"No release yet\"",
                     "sinceOne: \"1 commit on develop since \"", "sinceMany: \" commits on develop since \"",
                     "nothingSince: \"Nothing on develop since \"", "allOne: \"1 commit on develop\"",
                     "allMany: \" commits on develop\"", "next: \"Next release: v\"", "minor: \" (new features)\"",
                     "patch: \" (fixes only)\"", "noJobs: \"No merged jobs to release\"",
                     "note: \"/factory release still checks for breaking changes\"", "commits: \"Commits (\"",
                     "commitsEnd: \")\"", "separator: \" · \"",
                 })
        {
            Assert.Contains(text, texts, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task ReleasesJs_TextRules()
    {
        string js = await Fetch("/releases.js");
        string tag = Between(js, "function releaseTagText(release) {", "\n}\n");
        string commits = Between(js, "function releaseCommitsText(commits, release) {", "\n}\n");
        string next = Between(js, "function releaseNextText(data) {", "\n}\n");

        Assert.Contains("return ReleaseTexts.noRelease;", tag, StringComparison.Ordinal);
        Assert.Contains("ReleaseTexts.lastRelease + release.tag + (date === \"\" ? \"\" : ReleaseTexts.separator + date)", tag, StringComparison.Ordinal);
        Assert.Contains("toLocaleDateString(undefined, { dateStyle: \"medium\" })", js, StringComparison.Ordinal);
        Assert.Contains("return commits === 1 ? ReleaseTexts.allOne : String(commits) + ReleaseTexts.allMany;", commits, StringComparison.Ordinal);
        Assert.Contains("if (commits === 0) {\n    return ReleaseTexts.nothingSince + release.tag;", commits, StringComparison.Ordinal);
        Assert.Contains("if (commits === 1) {\n    return ReleaseTexts.sinceOne + release.tag;", commits, StringComparison.Ordinal);
        Assert.Contains("return String(commits) + ReleaseTexts.sinceMany + release.tag;", commits, StringComparison.Ordinal);
        Assert.Contains("return ReleaseTexts.noJobs;", next, StringComparison.Ordinal);
        Assert.Contains(
            "ReleaseTexts.next + data.suggested + (data.bump === \"minor\" ? ReleaseTexts.minor : ReleaseTexts.patch)", next, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReleasesJs_FailedAndUnavailableShowOnlyTheirText()
    {
        string js = await Fetch("/releases.js");
        string fill = Between(js, "function fillReleaseBlock(block) {", "\n}\n");

        Assert.Contains("block.replaceChildren(element(\"p\", \"release-failed\", ReleaseTexts.failed));\n    return;", fill, StringComparison.Ordinal);
        Assert.Contains("if (!data || !data.available) {\n    block.replaceChildren(element(\"p\", \"release-unavailable\", ReleaseTexts.unavailable));\n    return;", fill, StringComparison.Ordinal);
        Assert.Contains("element(\"p\", \"release-note\", ReleaseTexts.note)", fill, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReleasesJs_LogIsAClosedDetailsWithTextRowsAndJobLinks()
    {
        string js = await Fetch("/releases.js");
        string log = Between(js, "function releaseLog(projectName, log) {", "\n}\n");

        Assert.Contains("const details = element(\"details\", \"release-log\");", log, StringComparison.Ordinal);
        Assert.Contains("details.open = releaseOpen.has(projectName);", log, StringComparison.Ordinal);
        Assert.Contains("ReleaseTexts.commits + String(log.length) + ReleaseTexts.commitsEnd", log, StringComparison.Ordinal);
        Assert.Contains("element(\"code\", \"release-sha\", row.sha)", log, StringComparison.Ordinal);
        Assert.Contains("link.href = \"#job/\" + row.job;", log, StringComparison.Ordinal);
        Assert.Contains("element(\"span\", \"release-subject\", row.subject)", log, StringComparison.Ordinal);
        Assert.Contains("const ReleaseJobPattern = /^[0-9]{1,6}-[a-z0-9-]{1,80}$/;", js, StringComparison.Ordinal);
        Assert.Contains("const ReleaseShaPattern = /^[0-9a-f]{7}$/;", js, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReleasesJs_SetsTextOnlyAndFetchesOnlyThroughAuthFetchNeverOnATimer()
    {
        string js = await Fetch("/releases.js");

        Assert.DoesNotContain("innerHTML", js, StringComparison.Ordinal);
        Assert.DoesNotContain("insertAdjacentHTML", js, StringComparison.Ordinal);
        Assert.DoesNotContain("outerHTML", js, StringComparison.Ordinal);
        Assert.DoesNotContain("setInterval", js, StringComparison.Ordinal);
        Assert.DoesNotContain("setTimeout", js, StringComparison.Ordinal);
        Assert.DoesNotContain(" fetch(", js, StringComparison.Ordinal);
        Assert.Contains("const ReleasesApi = \"/api/releases\";", js, StringComparison.Ordinal);
        Assert.Contains("await authFetch(ReleasesApi, { cache: \"no-store\" });", js, StringComparison.Ordinal);
        Assert.Single(AuthFetchCall().Matches(js));
    }

    [Fact]
    public async Task ReleasesJs_DropsOlderAnswersWithARequestCounter()
    {
        string js = await Fetch("/releases.js");
        string load = Between(js, "async function loadReleases() {", "\n}\n");

        Assert.Contains("let releaseRequest = 0;", js, StringComparison.Ordinal);
        Assert.Contains("releaseRequest += 1;\n  const request = releaseRequest;", load, StringComparison.Ordinal);
        Assert.Contains("if (request !== releaseRequest) {\n    return true;\n  }", load, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReleasesJs_RefreshReplacesOnlyReleaseBlocksSkipsAFocusedOneAndKeepsAnOpenLog()
    {
        string js = await Fetch("/releases.js");
        string apply = Between(js, "function applyReleaseBlocks() {", "\n}\n");
        string fill = Between(js, "function fillReleaseBlock(block) {", "\n}\n");

        Assert.Contains("document.querySelectorAll(\"#backlogs section.project > div.project-release\")", apply, StringComparison.Ordinal);
        Assert.Contains("if (block.contains(document.activeElement)) {", apply, StringComparison.Ordinal);
        Assert.Contains("fillReleaseBlock(block);", apply, StringComparison.Ordinal);
        Assert.Contains(
            "const old = block.querySelector(\"details.release-log\");\n  if (old !== null && old.open) {\n    releaseOpen.add(name);",
            fill, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AppJs_PutsTheReleaseBlockDirectlyAfterTheStatusLineAndFetchesOnShow()
    {
        string app = await Fetch("/app.js");

        Assert.Contains(
            "  if (name !== FactoryName) {\n    section.appendChild(statusLine(name));\n    section.appendChild(releaseBlock(name));\n  }\n"
            + "  section.appendChild(renderPlan(name, project));",
            app, StringComparison.Ordinal);
        Assert.Contains("  showStatus();\n  showReleases();\n  showSearch();\n", app, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LiveJs_RefetchesReleasesAfterChangedJobsOrBacklogWhileShownOnly()
    {
        string js = await Fetch("/live.js");
        string refresh = Between(js, "async function refreshStaleReleases() {", "\n}\n");

        Assert.Contains(
            "if (recovered || statusInputsChanged(previous, next)) {\n    statusStale = true;\n    releasesStale = true;",
            js, StringComparison.Ordinal);
        Assert.Contains("if (document.getElementById(\"backlogs\").hidden) {\n    return;\n  }", refresh, StringComparison.Ordinal);
        Assert.Contains("if (!(await loadReleases())) {\n    releasesStale = true;", refresh, StringComparison.Ordinal);
        Assert.Contains("  await refreshStaleStatus();\n  await refreshStaleReleases();\n", js, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AppCss_ReleaseBlockIsMutedAndWraps()
    {
        string css = await Fetch("/app.css");
        string rule = Between(css, ".project-release {", "}");

        Assert.Contains("color: var(--muted);", rule, StringComparison.Ordinal);
        Assert.Contains("overflow-wrap: anywhere;", rule, StringComparison.Ordinal);
        Assert.Contains(".project-release .release-log summary", css, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ServiceWorker_CachesReleasesJsWithVersion21AndNeverTheApi()
    {
        string js = await Fetch("/sw.js");
        string room = await Fetch("/room.js");

        Assert.Contains("const CacheVersion = \"v23\";", room, StringComparison.Ordinal);
        Assert.Contains("  \"/status.js\",\n  \"/releases.js\",\n  \"/live.js\",\n", room, StringComparison.Ordinal);
        Assert.DoesNotContain("/api/releases", js, StringComparison.Ordinal);
        Assert.DoesNotContain("/api/releases", room, StringComparison.Ordinal);
    }

    private static string Between(string text, string start, string end)
    {
        int from = text.IndexOf(start, StringComparison.Ordinal);
        Assert.True(from >= 0, start);
        int to = text.IndexOf(end, from + start.Length, StringComparison.Ordinal);
        Assert.True(to >= 0, end);
        return text[(from + start.Length)..to];
    }

    private static async Task<string> Fetch(string path)
    {
        using TempFactoryRoot root = new();
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateClient();
        return await client.GetStringAsync(path);
    }

    [GeneratedRegex(@"authFetch\(")]
    private static partial Regex AuthFetchCall();
}
