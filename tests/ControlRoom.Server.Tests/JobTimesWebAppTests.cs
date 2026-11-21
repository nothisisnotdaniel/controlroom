using System.Text.RegularExpressions;

namespace ControlRoom.Server.Tests;

/// <summary>Checks the times on job cards and the job page's Agents list in <c>board.js</c>, <c>live.js</c> and
/// <c>app.css</c> as text (there is no JS test runtime, #136). The look is checked by hand at 375 px.</summary>
public sealed partial class JobTimesWebAppTests
{
    [Fact]
    public async Task BoardJs_HasTheFixedTimeTexts()
    {
        string texts = Between(await Fetch("/board.js"), "const BoardTexts = {", "\n};\n");

        foreach (string line in new[]
        {
            "  started: \"Started \",", "  ended: \" · ended \",", "  endedOnly: \"Ended \",", "  took: \" · took \",",
            "  running: \" · running \",", "  underMinute: \"< 1 min\",", "  minutes: \" min\",", "  hours: \" h\",",
            "  agents: \"Agents\",", "  tok: \" tok\",",
        })
        {
            Assert.Contains(line, texts, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task FormatDuration_WholeMinutesRoundedDown_UnderAMinuteHoursAndMinutes()
    {
        string body = Between(await Fetch("/board.js"), "function formatDuration(seconds) {", "\n}\n");

        Assert.Contains("const minutes = Math.floor(Math.max(0, seconds) / 60);", body, StringComparison.Ordinal);
        Assert.Contains("if (minutes < 1) {\n    return BoardTexts.underMinute;", body, StringComparison.Ordinal);
        Assert.Contains("if (minutes < 60) {\n    return minutes + BoardTexts.minutes;", body, StringComparison.Ordinal);
        Assert.Contains("const rest = minutes % 60;", body, StringComparison.Ordinal);
        Assert.Contains(
            "return Math.floor(minutes / 60) + BoardTexts.hours + (rest > 0 ? \" \" + rest + BoardTexts.minutes : \"\");",
            body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FormatTime_UsesTheBrowsersLocalTimeZone()
    {
        string js = await Fetch("/board.js");
        string body = Between(js, "function formatTime(date, timeOnly) {", "\n}\n");

        Assert.Contains("date.toLocaleTimeString([], time)", body, StringComparison.Ordinal);
        Assert.Contains("date.toLocaleString([], { month: \"short\", day: \"numeric\", ...time })", body,
            StringComparison.Ordinal);
        Assert.DoesNotContain("timeZone", js, StringComparison.Ordinal);
        Assert.DoesNotContain("toISOString", js, StringComparison.Ordinal);
        Assert.DoesNotContain("getUTC", js, StringComparison.Ordinal);
    }

    [Fact]
    public async Task JobTimes_RunningJobShowsStartAndRunningTimeFromNow()
    {
        string js = await Fetch("/board.js");
        string body = Between(js, "function jobTimes(job, className) {", "\n}\n");

        Assert.Contains("const RunningStages = [\"spike\", \"spec\", \"build\", \"review\", \"approve\"];", js,
            StringComparison.Ordinal);
        Assert.Contains("const running = RunningStages.includes(job.stage);", body, StringComparison.Ordinal);
        Assert.Contains("BoardTexts.started + formatTime(started, false)", body, StringComparison.Ordinal);
        Assert.Contains(
            "  if (running) {\n    const since = element(\"span\", null, runningText(started, Date.now()));\n"
            + "    since.dataset.runningSince = String(started.getTime());\n    line.appendChild(since);\n"
            + "    return line;\n  }", body, StringComparison.Ordinal);
        Assert.Contains("return BoardTexts.running + formatDuration((now - started.getTime()) / 1000);",
            Between(js, "function runningText(started, now) {", "\n}\n"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task JobTimes_FinishedJobShowsEndAndTook_OnlyWhenEndIsNotBeforeStart()
    {
        string body = Between(await Fetch("/board.js"), "function jobTimes(job, className) {", "\n}\n");

        Assert.Contains("started !== null && sameLocalDay(started, ended)\n      ? formatTime(ended, true)", body,
            StringComparison.Ordinal);
        Assert.Contains("(started !== null ? BoardTexts.ended : BoardTexts.endedOnly) + endText", body,
            StringComparison.Ordinal);
        Assert.Contains("if (started !== null && ended.getTime() >= started.getTime()) {", body, StringComparison.Ordinal);
        Assert.Contains("BoardTexts.took + formatDuration((ended.getTime() - started.getTime()) / 1000)", body,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task JobTimes_OldRecordWithoutTimesShowsNoLineAndNeverZero()
    {
        string js = await Fetch("/board.js");
        string body = Between(js, "function jobTimes(job, className) {", "\n}\n");
        string time = Between(js, "function recordTime(value) {", "\n}\n");

        Assert.Contains("if (started === null && (ended === null || running)) {\n    return null;\n  }", body,
            StringComparison.Ordinal);
        Assert.Contains("if (!text) {\n    return null;\n  }", time, StringComparison.Ordinal);
        Assert.Contains("return Number.isNaN(date.getTime()) ? null : date;", time, StringComparison.Ordinal);
        Assert.DoesNotContain("\"0 min\"", js, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CardAndJobPage_ShowTheTimesLine_AndTheJobPageTheAgents()
    {
        string js = await Fetch("/board.js");
        string card = Between(js, "function renderCard(job) {", "\n}\n");
        string page = Between(js, "function renderJobPage(data) {", "\n}\n");

        Assert.Contains("const times = jobTimes(job, \"card-times\");\n  if (times) {\n    card.appendChild(times);", card,
            StringComparison.Ordinal);
        Assert.True(card.IndexOf("\"card-times\"", StringComparison.Ordinal)
            < card.IndexOf("const foot = element(\"div\", \"card-foot\");", StringComparison.Ordinal));
        Assert.Contains("const times = jobTimes(job, \"job-times\");", page, StringComparison.Ordinal);
        Assert.Contains("const agents = renderAgents(asList(data && data.agents));", page, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RenderAgents_ShowsTokensAndTimeOnlyWhenKnown_AndNoListWhenEmpty()
    {
        string body = Between(await Fetch("/board.js"), "function renderAgents(agents) {", "\n}\n");

        Assert.Contains("if (rows.length === 0) {\n    return null;\n  }", body, StringComparison.Ordinal);
        Assert.Contains("section.appendChild(element(\"h2\", null, BoardTexts.agents));", body, StringComparison.Ordinal);
        Assert.Contains("const tokens = asNumber(agent.tokens);\n    if (tokens !== null) {\n"
            + "      parts.push(formatTokens(tokens) + BoardTexts.tok);", body, StringComparison.Ordinal);
        Assert.Contains("const seconds = asNumber(agent.seconds);\n    if (seconds !== null) {\n"
            + "      parts.push(formatDuration(seconds));", body, StringComparison.Ordinal);
        Assert.Contains("list.appendChild(element(\"li\", \"agent\", parts.join(\" · \")));", body, StringComparison.Ordinal);
        Assert.DoesNotContain("innerHTML", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunningTimes_AreRecomputedAfterEachLiveCheckWithoutANewTimer()
    {
        string board = await Fetch("/board.js");
        string live = await Fetch("/live.js");
        string update = Between(board, "function updateRunningTimes() {", "\n}\n");
        string shown = Between(live, "async function refreshShownViews() {", "\n}\n");

        Assert.Contains("document.querySelectorAll(\"[data-running-since]\")", update, StringComparison.Ordinal);
        Assert.Contains("span.textContent = runningText(new Date(since), now);", update, StringComparison.Ordinal);
        int visible = shown.IndexOf("if (boardViewsVisible()) {", StringComparison.Ordinal);
        int call = shown.IndexOf("updateRunningTimes();", StringComparison.Ordinal);
        int otherwise = shown.IndexOf("} else {\n    setBoardStatus(true);", StringComparison.Ordinal);
        Assert.True(visible >= 0 && call > visible && call < otherwise);
        Assert.Empty(Timer().Matches(board));
        Assert.Single(Interval().Matches(live));
    }

    [Fact]
    public async Task AppCss_StylesTheTimesAndAgents()
    {
        string css = await Fetch("/app.css");

        foreach (string rule in new[] { ".card-times {", ".job-times {", ".job-agents {", ".agent-list {", ".agent {" })
        {
            Assert.Contains(rule, css, StringComparison.Ordinal);
        }
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

    [GeneratedRegex(@"set(?:Interval|Timeout)\(")]
    private static partial Regex Timer();

    [GeneratedRegex(@"setInterval\(")]
    private static partial Regex Interval();
}
