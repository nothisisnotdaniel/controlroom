using System.Net;
using System.Text.RegularExpressions;

namespace ControlRoom.Server.Tests;

/// <summary>Checks <c>status.js</c> and its hooks in <c>index.html</c>, <c>app.js</c>, <c>live.js</c>,
/// <c>app.css</c> and <c>sw.js</c> as the served text (there is no JS test runtime, as in
/// <see cref="DoctorWebAppTests"/>). The look is checked by hand at 375 px in light and dark mode.</summary>
public sealed partial class StatusWebAppTests
{
    [Fact]
    public async Task IndexHtml_LoadsStatusJsAfterDoctorJsAndBeforeLiveJs()
    {
        string html = await Fetch("/index.html");

        Assert.Contains(
            "  <script src=\"/doctor.js\" defer></script>\n  <script src=\"/status.js\" defer></script>\n"
            + "  <script src=\"/releases.js\" defer></script>\n  <script src=\"/live.js\" defer></script>\n  <script src=\"/app.js\" defer></script>",
            html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StatusJs_IsServed()
    {
        using TempFactoryRoot root = new();
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync("/status.js");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/javascript", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task StatusJs_HasEveryFixedText()
    {
        string js = await Fetch("/status.js");

        foreach (string text in new[]
                 {
                     "failed: \"Status unavailable.\"", "success: \"CI passed\"", "failure: \"CI failed\"",
                     "timeout: \"CI timed out\"", "skipped: \"CI skipped\"", "none: \"No CI recorded\"",
                     "aheadOne: \"1 commit on develop not on main\"", "aheadMany: \" commits on develop not on main\"",
                     "level: \"develop is level with main\"", "gitUnknown: \"Git status unavailable\"",
                 })
        {
            Assert.Contains(text, js, StringComparison.Ordinal);
        }

        Assert.Contains("const StatusTexts = {", js, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StatusJs_AheadTextUsesSingularForOneLevelForZeroAndUnavailableForNull()
    {
        string js = await Fetch("/status.js");
        string helper = Between(js, "function statusAheadText(ahead) {", "\n}\n");

        Assert.Contains(
            "if (typeof ahead !== \"number\" || !Number.isInteger(ahead) || ahead < 0) {\n    return StatusTexts.gitUnknown;",
            helper, StringComparison.Ordinal);
        Assert.Contains("if (ahead === 0) {\n    return StatusTexts.level;", helper, StringComparison.Ordinal);
        Assert.Contains("if (ahead === 1) {\n    return StatusTexts.aheadOne;", helper, StringComparison.Ordinal);
        Assert.Contains("return String(ahead) + StatusTexts.aheadMany;", helper, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StatusJs_CiTextLinkAndBadClass()
    {
        string js = await Fetch("/status.js");

        Assert.Contains("const StatusCiStates = [\"success\", \"failure\", \"timeout\", \"skipped\"];", js, StringComparison.Ordinal);
        Assert.Contains("const StatusBadCi = [\"failure\", \"timeout\"];", js, StringComparison.Ordinal);
        Assert.Contains("ciPart.classList.add(\"ci-bad\");", js, StringComparison.Ordinal);
        Assert.Contains("link.target = \"_blank\";", js, StringComparison.Ordinal);
        Assert.Contains("link.rel = \"noopener noreferrer\";", js, StringComparison.Ordinal);
        Assert.Contains("value.startsWith(\"https://github.com/\")", js, StringComparison.Ordinal);
        Assert.Contains("toLocaleString(undefined, { dateStyle: \"medium\", timeStyle: \"short\" })", js, StringComparison.Ordinal);
        Assert.Contains("element(\"span\", \"status-time\", time)", js, StringComparison.Ordinal);
        Assert.Contains("line.replaceChildren(element(\"span\", \"status-failed\", StatusTexts.failed));", js, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StatusJs_SetsTextOnlyAndFetchesOnlyThroughAuthFetchNeverOnATimer()
    {
        string js = await Fetch("/status.js");

        Assert.DoesNotContain("innerHTML", js, StringComparison.Ordinal);
        Assert.DoesNotContain("insertAdjacentHTML", js, StringComparison.Ordinal);
        Assert.DoesNotContain("outerHTML", js, StringComparison.Ordinal);
        Assert.DoesNotContain("setInterval", js, StringComparison.Ordinal);
        Assert.DoesNotContain("setTimeout", js, StringComparison.Ordinal);
        Assert.DoesNotContain(" fetch(", js, StringComparison.Ordinal);
        Assert.Contains("const StatusApi = \"/api/status\";", js, StringComparison.Ordinal);
        Assert.Contains("await authFetch(StatusApi, { cache: \"no-store\" });", js, StringComparison.Ordinal);
        Assert.Single(AuthFetchCall().Matches(js));
    }

    [Fact]
    public async Task StatusJs_RefreshReplacesOnlyStatusLinesAndSkipsAFocusedOne()
    {
        string js = await Fetch("/status.js");
        string apply = Between(js, "function applyStatusLines() {", "\n}\n");

        Assert.Contains("document.querySelectorAll(\"#backlogs section.project > div.project-status\")", apply, StringComparison.Ordinal);
        Assert.Contains("if (line.contains(document.activeElement)) {", apply, StringComparison.Ordinal);
        Assert.Contains("fillStatusLine(line);", apply, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AppJs_PutsTheStatusLineBetweenHeadAndPlanAndFetchesOnShow()
    {
        string app = await Fetch("/app.js");

        Assert.Contains(
            "  if (name !== FactoryName) {\n    section.appendChild(statusLine(name));\n    section.appendChild(releaseBlock(name));\n  }\n  section.appendChild(renderPlan(name, project));",
            app, StringComparison.Ordinal);
        int unreadable = app.IndexOf("section.appendChild(element(\"p\", \"error\", Texts.unreadable));", StringComparison.Ordinal);
        int line = app.IndexOf("section.appendChild(statusLine(name));", StringComparison.Ordinal);
        Assert.True(unreadable >= 0 && line > unreadable);
        Assert.Contains("  showStatus();\n  showReleases();\n  showSearch();\n  if (fromSearch) {\n    window.scrollTo(0, 0);\n    focusSearch();\n    return;\n  }\n  window.scrollTo(0, listScroll);\n}", app, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LiveJs_RefetchesStatusAfterChangedJobsOrBacklogWhileShownOnly()
    {
        string js = await Fetch("/live.js");
        string inputs = Between(js, "function statusInputsChanged(previous, next) {", "\n}\n");
        string refresh = Between(js, "async function refreshStaleStatus() {", "\n}\n");

        Assert.Contains("previous.jobs !== next.jobs", inputs, StringComparison.Ordinal);
        Assert.Contains("before.backlog !== after.backlog", inputs, StringComparison.Ordinal);
        Assert.DoesNotContain("stories", inputs, StringComparison.Ordinal);
        Assert.Contains("if (recovered || statusInputsChanged(previous, next)) {\n    statusStale = true;", js, StringComparison.Ordinal);
        Assert.Contains("if (document.getElementById(\"backlogs\").hidden) {\n    return;\n  }", refresh, StringComparison.Ordinal);
        Assert.Contains("if (!(await loadStatus())) {\n    statusStale = true;", refresh, StringComparison.Ordinal);
        Assert.Contains("  await refreshStaleStatus();\n", js, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AppCss_StatusLineWrapsAndMarksBadCi()
    {
        string css = await Fetch("/app.css");
        string rule = Between(css, ".project-status {", "}");

        Assert.Contains("flex-wrap: wrap;", rule, StringComparison.Ordinal);
        Assert.Contains("overflow-wrap: anywhere;", rule, StringComparison.Ordinal);
        Assert.Contains(".project-status .ci-bad", css, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ServiceWorker_CachesStatusJsWithVersion16AndNeverTheApi()
    {
        string js = await Fetch("/sw.js");
        string room = await Fetch("/room.js");

        Assert.Contains("const CacheVersion = \"v23\";", room, StringComparison.Ordinal);
        Assert.Contains("  \"/doctor.js\",\n  \"/status.js\",\n  \"/releases.js\",\n  \"/live.js\",\n", room, StringComparison.Ordinal);
        Assert.DoesNotContain("/api/status", js, StringComparison.Ordinal);
        Assert.DoesNotContain("/api/status", room, StringComparison.Ordinal);
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
