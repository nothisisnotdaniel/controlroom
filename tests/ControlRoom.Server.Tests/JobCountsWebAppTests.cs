using System.Globalization;
using System.Text.RegularExpressions;

namespace ControlRoom.Server.Tests;

/// <summary>The running and blocked job counts on the Jobs tab (#138) in <c>index.html</c>, <c>counts.js</c>,
/// <c>board.js</c>, <c>live.js</c>, <c>app.js</c> and <c>app.css</c>, read as text (there is no JS test
/// runtime).</summary>
public sealed class JobCountsWebAppTests
{
    [Fact]
    public async Task IndexHtml_TheJobsLinkHoldsExactlyTheTwoHiddenJobBadgesAfterItsLabel()
    {
        string html = await Fetch("/index.html");

        int from = html.IndexOf("<a href=\"#jobs\" data-view=\"board\">", StringComparison.Ordinal);
        Assert.True(from >= 0);
        string link = html[from..html.IndexOf("</a>", from, StringComparison.Ordinal)];
        Assert.EndsWith(
            "<span class=\"tab-label\">Jobs</span>\n"
            + "      <span class=\"nav-count nav-count-running\" id=\"running-count\" hidden></span>\n"
            + "      <span class=\"nav-count nav-count-blocked\" id=\"blocked-count\" hidden></span>\n    ",
            link, StringComparison.Ordinal);
        Assert.Equal(2, Regex.Matches(link, "class=\"nav-count").Count);
        Assert.Single(Regex.Matches(html, "id=\"running-count\""));
        Assert.Single(Regex.Matches(html, "id=\"blocked-count\""));
    }

    [Fact]
    public async Task CountsJs_CountsRunningAndNeedsHumanStagesOverEveryJobAndNullWithoutAJobsList()
    {
        string js = await Fetch("/counts.js");
        string board = await Fetch("/board.js");

        Assert.Contains("const RunningStages = [\"spike\", \"spec\", \"build\", \"review\", \"approve\"];", board,
            StringComparison.Ordinal);
        string count = FunctionBody(js, "function countJobs(jobs) {");
        Assert.Contains("if (!Array.isArray(jobs)) {\n    return null;\n  }", count, StringComparison.Ordinal);
        Assert.Contains("for (const job of jobs) {", count, StringComparison.Ordinal);
        Assert.Contains("if (RunningStages.includes(job.stage)) {\n      running += 1;\n    } else if (job.stage === \"needs-human\") {\n"
                        + "      blocked += 1;\n    }", count, StringComparison.Ordinal);
        Assert.Contains("return { running: running, blocked: blocked };", count, StringComparison.Ordinal);
        // Every project: the board's project filter is never read.
        Assert.DoesNotContain("selectedProject", js, StringComparison.Ordinal);
        Assert.DoesNotContain("\"done\"", count, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CountsJs_EachJobBadgeUsesShowCountBadgeWithItsFixedText()
    {
        string js = await Fetch("/counts.js");

        Assert.Contains("  running: \" running\",\n  blocked: \" blocked\",\n};", js, StringComparison.Ordinal);
        Assert.Contains("let jobCounts = null;\nlet jobCountsRequest = 0;\n", js, StringComparison.Ordinal);
        string show = FunctionBody(js, "function showJobCounts() {");
        Assert.Contains("showCountBadge(\"running-count\", jobCounts === null ? null : jobCounts.running, CountTexts.running);",
            show, StringComparison.Ordinal);
        Assert.Contains("showCountBadge(\"blocked-count\", jobCounts === null ? null : jobCounts.blocked, CountTexts.blocked);",
            show, StringComparison.Ordinal);
        // The shared badge: hidden at 0 or unknown, the number aria-hidden plus a visually hidden ", N running".
        string badge = FunctionBody(js, "function showCountBadge(id, count, noun) {");
        Assert.Contains("const shown = count !== null && count > 0;", badge, StringComparison.Ordinal);
        Assert.Contains("number.setAttribute(\"aria-hidden\", \"true\");", badge, StringComparison.Ordinal);
        Assert.Contains("element(\"span\", \"visually-hidden\", \", \" + count + noun)", badge, StringComparison.Ordinal);
        Assert.DoesNotContain("innerHTML", js, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CountsJs_LoadJobCountsGoesThroughFetchBoardAndAFailureMakesTheCountsUnknown()
    {
        string js = await Fetch("/counts.js");

        string load = FunctionBody(js, "async function loadJobCounts() {");
        Assert.Contains("jobCountsRequest += 1;\n  const request = jobCountsRequest;", load, StringComparison.Ordinal);
        Assert.Contains("const data = JSON.parse(await fetchBoard());\n    jobs = data && data.jobs;\n    counts = countJobs(jobs);\n"
                        + "    if (counts === null) {\n      throw new Error(\"data\");\n    }", load, StringComparison.Ordinal);
        Assert.Equal(2, Regex.Matches(load, Regex.Escape("if (request === jobCountsRequest) {")).Count);
        Assert.Contains("jobCounts = null;\n      showJobCounts();\n    }\n    return false;", load, StringComparison.Ordinal);
        Assert.Contains("jobCounts = counts;\n    showJobCounts();\n    await runRoomKitHooks(\"jobCounts\", jobs);\n  }\n  return true;",
            load, StringComparison.Ordinal);
        Assert.DoesNotContain("authFetch(", js, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BoardJs_FetchBoardHoldsTheOnlyBoardRequestAndPollBoardSetsTheJobCounts()
    {
        string js = await Fetch("/board.js");

        Assert.Single(Regex.Matches(js, Regex.Escape("authFetch(\"/api/board\"")));
        string fetch = FunctionBody(js, "async function fetchBoard() {");
        Assert.Contains("const response = await authFetch(\"/api/board\", { cache: \"no-store\" });", fetch,
            StringComparison.Ordinal);
        Assert.Contains("if (!response.ok) {\n    throw new Error(\"status\");\n  }\n  return response.text();", fetch,
            StringComparison.Ordinal);
        string poll = FunctionBody(js, "async function pollBoard() {");
        Assert.DoesNotContain("authFetch(", poll, StringComparison.Ordinal);
        Assert.Contains("text = await fetchBoard();", poll, StringComparison.Ordinal);
        // A failure makes the counts unknown; every answer read sets them, before the unchanged-answer shortcut.
        Assert.Contains("} catch {\n      setJobCounts(null);\n      setBoardStatus(false);\n      return false;\n    }", poll,
            StringComparison.Ordinal);
        int set = poll.IndexOf("setJobCounts(countJobs(data && data.jobs));", StringComparison.Ordinal);
        Assert.True(set >= 0 && set < poll.IndexOf("if (text === boardText) {", StringComparison.Ordinal));

        string counts = await Fetch("/counts.js");
        string setCounts = FunctionBody(counts, "function setJobCounts(counts) {");
        Assert.Contains("jobCountsRequest += 1;\n  jobCounts = counts;\n  showJobCounts();", setCounts, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AppJs_LoadsTheJobCountsAtPageLoadAndAfterATokenNextToTheBacklogCounts()
    {
        string app = await Fetch("/app.js");

        Assert.Contains("  loadBacklogCounts();\n  loadJobCounts();\n});", app, StringComparison.Ordinal);
        Assert.Contains("load();\nloadBacklogCounts();\nloadJobCounts();\nstartLiveChecks();", app, StringComparison.Ordinal);
        Assert.Equal(2, Regex.Matches(app, Regex.Escape("loadJobCounts();")).Count);
    }

    [Fact]
    public async Task LiveJs_AJobsChangeOrARecoveredConnectionRefetchesTheJobCountsOnEveryView()
    {
        string live = await Fetch("/live.js");

        Assert.Contains("let jobCountsStale = false;\n", live, StringComparison.Ordinal);
        string note = FunctionBody(live, "function noteChanges(next, recovered) {");
        Assert.Contains("  if (recovered) {\n    boardStale = true;\n    jobStale = true;\n    countsStale = true;\n"
                        + "    jobCountsStale = true;\n", note, StringComparison.Ordinal);
        Assert.Contains("  if (previous.jobs !== next.jobs) {\n    boardStale = true;\n    jobCountsStale = true;\n  }", note,
            StringComparison.Ordinal);
        // Only those two places: not on the baseline, not on a backlog-only change.
        Assert.Equal(2, Regex.Matches(note, Regex.Escape("jobCountsStale = true;")).Count);
        int baseline = note.IndexOf("if (previous === null) {\n    return;\n  }", StringComparison.Ordinal);
        Assert.True(baseline >= 0 && baseline < note.IndexOf("if (previous.jobs !== next.jobs) {\n    boardStale", StringComparison.Ordinal));

        string refresh = FunctionBody(live, "async function refreshShownViews() {");
        int call = refresh.IndexOf("  await refreshStaleJobCounts();", StringComparison.Ordinal);
        Assert.True(call >= 0);
        // Outside the boardViewsVisible() block, so it runs on every view.
        Assert.True(call > refresh.IndexOf("  jobStale = false;\n", StringComparison.Ordinal));

        string retry = FunctionBody(live, "async function refreshStaleJobCounts() {");
        Assert.Contains("if (!jobCountsStale) {\n    return;\n  }\n  jobCountsStale = false;\n"
                        + "  if (!(await loadJobCounts())) {\n    jobCountsStale = true;\n  }", retry, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AppCss_JobBadgesSitApartInDistinctReadableColoursWithARedBlockedBadge()
    {
        string css = await Fetch("/app.css");

        string running = Block(css, ".nav-count-running {");
        string blocked = Block(css, ".nav-count-blocked {");
        Assert.Contains("  right: calc(50% + 6px);\n  background: var(--count-running-bg);\n  color: var(--count-running-fg);\n",
            running, StringComparison.Ordinal);
        Assert.Contains("  left: calc(50% + 6px);\n  background: var(--count-blocked-bg);\n  color: var(--count-blocked-fg);\n",
            blocked, StringComparison.Ordinal);

        string light = Block(css, ":root {");
        int dark = css.IndexOf("@media (prefers-color-scheme: dark) {\n  :root {", StringComparison.Ordinal);
        Assert.True(dark >= 0);
        string darkRoot = css[dark..(css.IndexOf("\n  }\n", dark, StringComparison.Ordinal) + 1)];
        foreach (string scheme in new[] { light, darkRoot })
        {
            string[] kinds = ["tasks", "approval", "running", "blocked"];
            string[] backgrounds = kinds.Select(kind => Variable(scheme, "--count-" + kind + "-bg")).ToArray();
            Assert.Equal(kinds.Length, backgrounds.Distinct(StringComparer.OrdinalIgnoreCase).Count());
            foreach (string kind in kinds)
            {
                double ratio = Contrast(Variable(scheme, "--count-" + kind + "-bg"), Variable(scheme, "--count-" + kind + "-fg"));
                Assert.True(ratio >= 4.5, kind + " contrast " + ratio.ToString("F2", CultureInfo.InvariantCulture));
            }
            double hue = Hue(Variable(scheme, "--count-blocked-bg"));
            Assert.True(hue >= 345 || hue <= 15, "blocked hue " + hue.ToString("F0", CultureInfo.InvariantCulture));
        }
    }

    private static (double R, double G, double B) Rgb(string hex) =>
        (int.Parse(hex[1..3], NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0,
            int.Parse(hex[3..5], NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0,
            int.Parse(hex[5..7], NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0);

    private static double Luminance(string hex)
    {
        static double Channel(double c) => c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        (double r, double g, double b) = Rgb(hex);
        return 0.2126 * Channel(r) + 0.7152 * Channel(g) + 0.0722 * Channel(b);
    }

    private static double Contrast(string a, string b)
    {
        double la = Luminance(a);
        double lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    private static double Hue(string hex)
    {
        (double r, double g, double b) = Rgb(hex);
        double max = Math.Max(r, Math.Max(g, b));
        double delta = max - Math.Min(r, Math.Min(g, b));
        Assert.True(delta > 0, hex + " is grey");
        double hue = max == r ? 60 * ((g - b) / delta) : max == g ? 60 * ((b - r) / delta + 2) : 60 * ((r - g) / delta + 4);
        return hue < 0 ? hue + 360 : hue;
    }

    private static string Variable(string block, string name)
    {
        Match match = Regex.Match(block, "\n\\s+" + Regex.Escape(name) + ": (?<value>#[0-9a-fA-F]{6});\n");
        Assert.True(match.Success, name + " not found");
        return match.Groups["value"].Value;
    }

    // The rule block that starts exactly at a line with this selector, up to its closing brace.
    private static string Block(string css, string selector)
    {
        int from = css.StartsWith(selector, StringComparison.Ordinal)
            ? 0
            : css.IndexOf("\n" + selector, StringComparison.Ordinal) + 1;
        Assert.True(from > 0 || css.StartsWith(selector, StringComparison.Ordinal), selector + " not found");
        int end = css.IndexOf("\n}\n", from, StringComparison.Ordinal);
        Assert.True(end > from);
        return css[from..(end + 1)];
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
