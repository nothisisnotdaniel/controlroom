using System.Text.RegularExpressions;

namespace ControlRoom.Server.Tests;

/// <summary>Checks <c>history.js</c> and its hooks in <c>index.html</c>, <c>app.js</c>, <c>live.js</c>,
/// <c>app.css</c> and <c>sw.js</c> as text (there is no JS test runtime). The look and the chart are checked by hand
/// at 375 px in light and dark mode.</summary>
public sealed partial class HistoryWebAppTests
{
    [Fact]
    public async Task IndexHtml_HasTheHistoryLinkAfterCaptureAndTheHistoryView()
    {
        string html = await Fetch("/index.html");

        int search = html.IndexOf("<a href=\"#capture\" data-view=\"capture\">", StringComparison.Ordinal);
        int history = html.IndexOf("<a href=\"#history\" data-view=\"history\">", StringComparison.Ordinal);
        int navEnd = html.IndexOf("</nav>", StringComparison.Ordinal);
        Assert.True(search >= 0 && history > search && history < navEnd);
        Assert.Contains("<main id=\"history\" hidden></main>", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task IndexHtml_LoadsHistoryJsBetweenCaptureJsAndDoctorJs_ThirteenScripts()
    {
        string html = await Fetch("/index.html");
        string[] scripts =
        [
            "/token.js", "/board.js", "/act.js", "/push.js", "/edit.js", "/counts.js", "/search.js",
            "/capture.js", "/history.js", "/doctor.js", "/status.js", "/live.js", "/app.js",
        ];

        int[] positions = scripts
            .Select(script => html.IndexOf("<script src=\"" + script + "\" defer></script>", StringComparison.Ordinal))
            .ToArray();
        Assert.All(positions, position => Assert.True(position >= 0));
        Assert.Equal(positions.OrderBy(position => position), positions);
        Assert.Equal(13, ScriptTag().Matches(html).Count);
        Assert.Contains(
            "<script src=\"/capture.js\" defer></script>\n  <script src=\"/history.js\" defer></script>\n"
            + "  <script src=\"/doctor.js\" defer></script>", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AppJs_RoutesHashHistoryToTheHistoryViewTitledHistory()
    {
        string app = await Fetch("/app.js");
        string js = await Fetch("/history.js");

        Assert.Contains(
            "const Views = [\"backlogs\", \"board\", \"capture\", \"history\", \"doctor\", \"job\", \"story\"];",
            app, StringComparison.Ordinal);
        Assert.Contains(" history: \"History\", doctor: \"Health\" };", app, StringComparison.Ordinal);
        Assert.Contains("  if (hash === HistoryRoute) {\n    return \"history\";\n  }", app, StringComparison.Ordinal);
        Assert.Contains("  if (next === \"history\") {\n    showHistory();\n    return;\n  }", app, StringComparison.Ordinal);
        Assert.Contains("const HistoryRoute = \"#history\";", js, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HistoryJs_FetchesOnlyThroughAuthFetchWithNoStoreOnEveryShow()
    {
        string js = await Fetch("/history.js");

        Assert.Contains("const HistoryApi = \"/api/history\";", js, StringComparison.Ordinal);
        Assert.Contains("await authFetch(HistoryApi, { cache: \"no-store\" });", js, StringComparison.Ordinal);
        Assert.Single(AuthFetchCall().Matches(js));
        Assert.DoesNotContain(" fetch(", js, StringComparison.Ordinal);
        Assert.DoesNotContain("/api/backlog/change", js, StringComparison.Ordinal);
        Assert.DoesNotContain("method:", js, StringComparison.Ordinal);
        Assert.DoesNotContain("setInterval", js, StringComparison.Ordinal);
        Assert.DoesNotContain("setTimeout", js, StringComparison.Ordinal);
        string show = Between(js, "function showHistory() {", "\n}\n");
        Assert.Contains("loadHistory();", show, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HistoryJs_HasTheFixedTexts()
    {
        string js = await Fetch("/history.js");
        string texts = Between(js, "const HistoryTexts = {", "\n};\n") + "\n";

        Assert.Contains("  failed: \"Could not load history.\",\n", texts, StringComparison.Ordinal);
        Assert.Contains("  empty: \"No finished jobs yet.\",\n", texts, StringComparison.Ordinal);
        Assert.Contains("  emptyProject: \"No jobs for this project.\",\n", texts, StringComparison.Ordinal);
        Assert.Contains("  last7: \"Last 7 days\",\n", texts, StringComparison.Ordinal);
        Assert.Contains("  last30: \"Last 30 days\",\n", texts, StringComparison.Ordinal);
        Assert.Contains("  allTime: \"All time\",\n", texts, StringComparison.Ordinal);
        Assert.Contains("  projectLabel: \"Project\",\n", texts, StringComparison.Ordinal);
        Assert.Contains("  allProjects: \"All projects\",\n", texts, StringComparison.Ordinal);
        Assert.Contains("  unknown: \"unknown\",\n", texts, StringComparison.Ordinal);
        Assert.Contains("  round: \"Round \",\n", texts, StringComparison.Ordinal);
        Assert.Contains(
            "const HistoryOutcomes = { merged: \"Merged\", escalated: \"Escalated\", reverted: \"Reverted\", findings: \"Findings\" };",
            js, StringComparison.Ordinal);
        Assert.Contains("showHistoryStatus(HistoryTexts.failed, \"error\");", js, StringComparison.Ordinal);
        Assert.Contains("showHistoryStatus(HistoryTexts.empty, \"empty\");", js, StringComparison.Ordinal);
        Assert.Contains("rows.replaceChildren(element(\"p\", \"empty\", HistoryTexts.emptyProject));", js,
            StringComparison.Ordinal);
        Assert.DoesNotContain("data.error", js, StringComparison.Ordinal);
        Assert.DoesNotContain(".message", js, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HistoryJs_TotalsUseFormatTokensAndShowUnknownApart()
    {
        string js = await Fetch("/history.js");

        Assert.Contains("const known = formatTokens(total.tokens);", js, StringComparison.Ordinal);
        Assert.Contains(
            "return total.unknown > 0 ? known + \" + \" + total.unknown + \" \" + HistoryTexts.unknown : known;", js,
            StringComparison.Ordinal);
        Assert.Contains("renderHistoryWindow(\"history-last7\", HistoryTexts.last7, data, \"last7\")", js,
            StringComparison.Ordinal);
        Assert.Contains("renderHistoryWindow(\"history-last30\", HistoryTexts.last30, data, \"last30\")", js,
            StringComparison.Ordinal);
        Assert.Contains("renderHistoryTable(HistoryTexts.projects, data.projects, key, HistoryTexts.noProject)", js,
            StringComparison.Ordinal);
        Assert.Contains("renderHistoryTable(HistoryTexts.modes, data.modes, key, HistoryTexts.noMode)", js,
            StringComparison.Ordinal);
        int totals = js.IndexOf("nodes.push(renderHistoryWindow(\"history-last7\"", StringComparison.Ordinal);
        int chart = js.IndexOf("nodes.push(renderHistoryChart(data));", StringComparison.Ordinal);
        Assert.True(totals >= 0 && chart > totals);
        Assert.Contains("job.tokens === null ? HistoryTexts.unknown : formatTokens(job.tokens)", js,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task HistoryJs_ChartIsAnAccessibleSvgBuiltWithDomCalls()
    {
        string js = await Fetch("/history.js");

        Assert.Contains("const SvgNamespace = \"http://www.w3.org/2000/svg\";", js, StringComparison.Ordinal);
        Assert.Contains("document.createElementNS(SvgNamespace, tag);", js, StringComparison.Ordinal);
        Assert.Contains("role: \"img\",", js, StringComparison.Ordinal);
        Assert.Contains("svg.setAttribute(\"aria-label\", HistoryTexts.chartLabel + \": \"", js, StringComparison.Ordinal);
        Assert.Contains("svg.appendChild(svgElement(\"rect\", {", js, StringComparison.Ordinal);
        Assert.Contains("element(\"span\", \"history-legend-name\", historyName(mode.name, HistoryTexts.noMode))", js,
            StringComparison.Ordinal);
        Assert.Contains("element(\"span\", \"history-legend-total\", formatTokens(totals[index]))", js,
            StringComparison.Ordinal);
        Assert.DoesNotContain("DOMParser", js, StringComparison.Ordinal);
        Assert.DoesNotContain("<svg", js, StringComparison.Ordinal);
        Assert.DoesNotContain("style", js, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HistoryJs_SetsTextOnlyThroughTextContent()
    {
        string js = await Fetch("/history.js");

        Assert.DoesNotContain("innerHTML", js, StringComparison.Ordinal);
        Assert.DoesNotContain("insertAdjacentHTML", js, StringComparison.Ordinal);
        Assert.DoesNotContain("outerHTML", js, StringComparison.Ordinal);
        Assert.DoesNotContain("document.write", js, StringComparison.Ordinal);
        Assert.Contains("status.textContent = text;", js, StringComparison.Ordinal);
        Assert.Single(TextAssignment().Matches(js));
    }

    [Fact]
    public async Task HistoryJs_EachRowShowsTheJobFactsAndLinksTheJobPage()
    {
        string js = await Fetch("/history.js");
        string row = Between(js, "function renderHistoryJob(job) {", "\n}\n");

        Assert.Contains("element(\"span\", \"history-project\", historyName(job.project, HistoryTexts.noProject))", row,
            StringComparison.Ordinal);
        Assert.Contains("const title = element(\"a\", \"history-title\", titleText(job.title));", row,
            StringComparison.Ordinal);
        Assert.Contains("title.href = jobHash(job.id);", row, StringComparison.Ordinal);
        Assert.Contains("[job.mode, job.path, job.round === null ? null : HistoryTexts.round + job.round]", row,
            StringComparison.Ordinal);
        Assert.Contains("HistoryOutcomes[job.outcome]", row, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HistoryJs_ProjectSelectFiltersTheListAndAllTimeAndIsKept()
    {
        string js = await Fetch("/history.js");

        Assert.Contains("const HistoryProjectKey = \"controlroom-history-project\";", js, StringComparison.Ordinal);
        Assert.Contains("localStorage.setItem(HistoryProjectKey, name);", js, StringComparison.Ordinal);
        Assert.Contains("const wanted = select.options.length > 0 ? select.value : storedHistoryProject();", js,
            StringComparison.Ordinal);
        Assert.Contains("const options = [element(\"option\", \"\", HistoryTexts.allProjects)];", js,
            StringComparison.Ordinal);
        Assert.Contains("const jobs = historyData.jobs.filter((job) => chosen === \"\" || job.project === chosen);", js,
            StringComparison.Ordinal);
        string change = Between(js, "select.addEventListener(\"change\", () => {", "});");
        Assert.Contains("renderHistoryAll();", change, StringComparison.Ordinal);
        Assert.Contains("renderHistoryList();", change, StringComparison.Ordinal);
        Assert.DoesNotContain("loadHistory", change, StringComparison.Ordinal);
        Assert.DoesNotContain("renderHistory()", change, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HistoryJs_AllTimeComesAfterLast30AndBeforeTheChart()
    {
        string js = await Fetch("/history.js");
        string render = Between(js, "function renderHistory() {", "\n}\n");

        int last7 = render.IndexOf("renderHistoryWindow(\"history-last7\"", StringComparison.Ordinal);
        int last30 = render.IndexOf("renderHistoryWindow(\"history-last30\"", StringComparison.Ordinal);
        int noRecent = render.IndexOf("HistoryTexts.noRecent", StringComparison.Ordinal);
        int all = render.IndexOf("nodes.push(all);", StringComparison.Ordinal);
        int chart = render.IndexOf("nodes.push(renderHistoryChart(data));", StringComparison.Ordinal);
        Assert.True(last7 >= 0 && last30 > last7 && all > last30 && chart > all);
        Assert.True(noRecent >= 0 && all > noRecent);
        Assert.Contains("renderHistoryAll();", render, StringComparison.Ordinal);
        Assert.DoesNotContain("if (recent) {\n    nodes.push(all);", render, StringComparison.Ordinal);

        string block = Between(js, "function renderHistoryAll() {", "\n}\n");
        Assert.Contains("renderHistoryWindow(\"history-all\", HistoryTexts.allTime, shown, \"all\")", block,
            StringComparison.Ordinal);
        Assert.Contains("if (lifetime.projects.length === 0 && lifetime.modes.length === 0) {", block,
            StringComparison.Ordinal);
        Assert.Contains("all.hidden = true;", block, StringComparison.Ordinal);
        Assert.DoesNotContain("loadHistory", block, StringComparison.Ordinal);
        Assert.Contains("const section = element(\"section\", \"history-window\");", js, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HistoryJs_AllTimeFilterUsesTheProjectRowAndItsProjectModes()
    {
        string js = await Fetch("/history.js");
        string filter = Between(js, "function historyLifetimeFor(lifetime, chosen) {", "\n}\n");

        Assert.Contains("return { projects: lifetime.projects, modes: lifetime.modes };", filter,
            StringComparison.Ordinal);
        Assert.Contains("lifetime.projectModes.find((item) => item.name === chosen)", filter, StringComparison.Ordinal);
        Assert.Contains("projects: lifetime.projects.filter((item) => item.name === chosen),", filter,
            StringComparison.Ordinal);
        Assert.Contains("modes: entry ? entry.modes : [],", filter, StringComparison.Ordinal);
        Assert.Contains("const chosen = document.getElementById(\"history-project\").value;",
            Between(js, "function renderHistoryAll() {", "\n}\n"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task HistoryJs_ReadsLifetimeTolerantly()
    {
        string js = await Fetch("/history.js");
        string read = Between(js, "function readHistory(data) {", "\n}\n");
        string lifetime = Between(js, "function readHistoryLifetime(value) {", "\n}\n");

        Assert.DoesNotContain("data.lifetime", Between(read, "if (", ") {"), StringComparison.Ordinal);
        Assert.Contains("lifetime: readHistoryLifetime(data.lifetime),", read, StringComparison.Ordinal);
        Assert.Contains("const lifetime = value && typeof value === \"object\" ? value : {};", lifetime,
            StringComparison.Ordinal);
        Assert.Contains("projects: readHistoryLifetimeTotals(lifetime.projects),", lifetime, StringComparison.Ordinal);
        Assert.Contains("modes: readHistoryLifetimeTotals(lifetime.modes),", lifetime, StringComparison.Ordinal);
        Assert.Contains("asList(lifetime.projectModes).map(", lifetime, StringComparison.Ordinal);
        Assert.Contains("all: readHistoryWindow(item && item.all),", js, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LiveJs_RefetchesTheHistoryWhenTheJobsFingerprintChangesWhileShown()
    {
        string live = await Fetch("/live.js");

        Assert.Contains(
            "if (recovered || (previous !== null && previous.jobs !== next.jobs)) {\n    historyStale = true;\n  }", live,
            StringComparison.Ordinal);
        Assert.Contains("await refreshStaleHistory();", live, StringComparison.Ordinal);
        string refresh = Between(live, "async function refreshStaleHistory() {", "\n}\n");
        Assert.Contains("if (document.getElementById(\"history\").hidden) {\n    return;\n  }", refresh,
            StringComparison.Ordinal);
        Assert.Contains("if (!(await loadHistory())) {\n    historyStale = true;\n  }", refresh, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AppCss_FitsEightTabsInOneRowAndStylesTheView()
    {
        string css = await Fetch("/app.css");

        Assert.DoesNotContain("two rows of four", css, StringComparison.Ordinal);
        Assert.Contains("  flex: 1 1 0;\n", css, StringComparison.Ordinal);
        foreach (string rule in new[]
                 {
                     ".history-table {", ".history-svg {", ".history-legend {", ".history-select {", ".history-list {",
                     ".history-outcome-merged {", ".history-outcome-escalated {", ".history-outcome-reverted {",
                     ".history-outcome-findings {",
                 })
        {
            Assert.Contains(rule, css, StringComparison.Ordinal);
        }

        for (int index = 0; index < 6; index += 1)
        {
            Assert.Equal(2, Count(css, "  --chart-" + index + ": #"));
            Assert.Contains(".history-colour-" + index + " {\n  fill: var(--chart-" + index + ");", css,
                StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task ServiceWorker_CachesHistoryJsWithVersion15()
    {
        string js = await Fetch("/sw.js");

        Assert.Contains("const CacheVersion = \"v20\";", js, StringComparison.Ordinal);
        Assert.Contains("  \"/capture.js\",\n  \"/history.js\",\n  \"/doctor.js\",\n", js, StringComparison.Ordinal);
        Assert.DoesNotContain("/api/history", js, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HistoryJs_IsServed()
    {
        using TempFactoryRoot root = new();
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync("/history.js");

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/javascript", response.Content.Headers.ContentType?.MediaType);
    }

    private static string Between(string text, string start, string end)
    {
        int from = text.IndexOf(start, StringComparison.Ordinal);
        Assert.True(from >= 0, start);
        int to = text.IndexOf(end, from + start.Length, StringComparison.Ordinal);
        Assert.True(to >= 0, end);
        return text[(from + start.Length)..to];
    }

    private static int Count(string text, string part)
    {
        int count = 0;
        int at = text.IndexOf(part, StringComparison.Ordinal);
        while (at >= 0)
        {
            count += 1;
            at = text.IndexOf(part, at + part.Length, StringComparison.Ordinal);
        }

        return count;
    }

    private static async Task<string> Fetch(string path)
    {
        using TempFactoryRoot root = new();
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateClient();
        return await client.GetStringAsync(path);
    }

    [GeneratedRegex("<script ")]
    private static partial Regex ScriptTag();

    [GeneratedRegex(@"authFetch\(")]
    private static partial Regex AuthFetchCall();

    [GeneratedRegex(@"\.textContent\s*=[^;]*;")]
    private static partial Regex TextAssignment();
}
