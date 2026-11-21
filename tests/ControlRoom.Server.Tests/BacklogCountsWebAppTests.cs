using System.Text.Json;
using System.Text.RegularExpressions;

namespace ControlRoom.Server.Tests;

/// <summary>The two counts on the Backlogs tab (#135) in <c>counts.js</c>, <c>edit.js</c>, <c>live.js</c> and
/// <c>app.js</c>, read as text (there is no JS test runtime), plus the <c>/api/backlogs</c> groups they count, and the
/// old <c>#inbox</c> and <c>#tasks</c> links that now open the Backlogs.</summary>
public sealed class BacklogCountsWebAppTests
{
    [Fact]
    public async Task IndexHtml_LoadsCountsJsInPlaceOfInboxJsAndTasksJs()
    {
        string html = await Fetch("/index.html");

        Assert.Contains("<script src=\"/edit.js\" defer></script>\n  <script src=\"/counts.js\" defer></script>\n"
                        + "  <script src=\"/search.js\" defer></script>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("inbox.js", html, StringComparison.Ordinal);
        Assert.DoesNotContain("tasks.js", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<main id=\"inbox\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<main id=\"tasks\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CountsJs_CountsUserAndApprovalEntriesOfReadableProjectsFromTheBacklogsAnswer()
    {
        string js = await Fetch("/counts.js");

        string count = FunctionBody(js, "function countBacklogs(data) {");
        Assert.Contains("if (!project || project.readable !== true) {\n      continue;\n    }", count, StringComparison.Ordinal);
        Assert.Contains("tasks += asList(project.user).length;", count, StringComparison.Ordinal);
        Assert.Contains("approval += asList(project.approval).length;", count, StringComparison.Ordinal);
        string load = FunctionBody(js, "async function loadBacklogCounts() {");
        Assert.Contains("counts = countBacklogs(await fetchBacklogs());", load, StringComparison.Ordinal);
        // An older answer never overwrites a newer one, and a failure makes the counts unknown.
        Assert.Equal(2, Regex.Matches(load, Regex.Escape("if (request === backlogCountsRequest) {")).Count);
        Assert.Contains("backlogCounts = null;\n      showBacklogCounts();\n    }\n    return false;", load,
            StringComparison.Ordinal);
        Assert.DoesNotContain("authFetch(", js, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CountsJs_EachBadgeIsHiddenAtZeroOrUnknownAndOtherwiseShowsItsNumber()
    {
        string js = await Fetch("/counts.js");

        string badge = FunctionBody(js, "function showCountBadge(id, count, noun) {");
        Assert.Contains("const shown = count !== null && count > 0;", badge, StringComparison.Ordinal);
        Assert.Contains("if (!shown) {\n    badge.replaceChildren();\n    badge.hidden = true;\n    return;\n  }", badge,
            StringComparison.Ordinal);
        Assert.Contains("const number = element(\"span\", null, String(count));", badge, StringComparison.Ordinal);
        Assert.Contains("badge.hidden = false;", badge, StringComparison.Ordinal);
        string show = FunctionBody(js, "function showBacklogCounts() {");
        Assert.Contains("const tasks = backlogCounts === null ? null : backlogCounts.tasks;", show, StringComparison.Ordinal);
        Assert.Contains("showCountBadge(\"tasks-count\", tasks, ", show, StringComparison.Ordinal);
        Assert.Contains("showCountBadge(\"approval-count\", approval, CountTexts.unapproved);", show, StringComparison.Ordinal);
        Assert.DoesNotContain("innerHTML", js, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CountsJs_EachShownBadgeHasAnAccessibleNameAndTheNumberIsAriaHidden()
    {
        string js = await Fetch("/counts.js");

        Assert.Contains("  task: \" task\",\n  tasks: \" tasks\",\n  unapproved: \" unapproved\",\n", js, StringComparison.Ordinal);
        string badge = FunctionBody(js, "function showCountBadge(id, count, noun) {");
        Assert.Contains("number.setAttribute(\"aria-hidden\", \"true\");", badge, StringComparison.Ordinal);
        Assert.Contains("badge.replaceChildren(number, element(\"span\", \"visually-hidden\", \", \" + count + noun));", badge,
            StringComparison.Ordinal);
        Assert.Contains("tasks === 1 ? CountTexts.task : CountTexts.tasks", FunctionBody(js, "function showBacklogCounts() {"),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task CountsRefresh_AtLoadAfterATokenAfterEveryCommittedChangeAndOnALiveBacklogChange()
    {
        string app = await Fetch("/app.js");
        string edit = await Fetch("/edit.js");
        string live = await Fetch("/live.js");

        Assert.Contains("onTokenSaved(() => {\n  route();\n  load();\n  loadBacklogCounts();\n});", app, StringComparison.Ordinal);
        Assert.Contains("load();\nloadBacklogCounts();\nstartLiveChecks();", app, StringComparison.Ordinal);
        string send = FunctionBody(edit, "async function sendChange(body) {");
        int committed = send.IndexOf("data.committed === true", StringComparison.Ordinal);
        int reload = send.IndexOf("loadBacklogCounts();\n    return true;", StringComparison.Ordinal);
        Assert.True(committed >= 0 && reload > committed);
        Assert.Single(Regex.Matches(send, "loadBacklogCounts\\(\\)"));

        Assert.Contains("      boardStale = true;\n      countsStale = true;\n      staleBacklogs.add(name);", live, StringComparison.Ordinal);
        Assert.Contains("    jobStale = true;\n    countsStale = true;\n", live, StringComparison.Ordinal);
        Assert.Contains("  await refreshStaleCounts();\n}", live, StringComparison.Ordinal);
        string refresh = FunctionBody(live, "async function refreshStaleCounts() {");
        Assert.Contains("if (!(await loadBacklogCounts())) {\n    countsStale = true;\n  }", refresh, StringComparison.Ordinal);
        foreach (string js in new[] { app, edit, live })
        {
            Assert.DoesNotContain("loadInbox", js, StringComparison.Ordinal);
            Assert.DoesNotContain("loadTasks", js, StringComparison.Ordinal);
            Assert.DoesNotContain("inboxStale", js, StringComparison.Ordinal);
            Assert.DoesNotContain("tasksStale", js, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task EditJs_ARefusedChangeShowsTheFixedTextForItsCode()
    {
        string edit = await Fetch("/edit.js");

        Assert.Contains("changed: \"Backlog changed since you loaded it. Reload and try again.\",", edit, StringComparison.Ordinal);
        Assert.Contains(
            "showEditNotice(Object.prototype.hasOwnProperty.call(ChangeErrors, code) ? ChangeErrors[code] : ChangeErrors.failed,",
            edit, StringComparison.Ordinal);
        Assert.Contains("if (changeBusy) {\n    return false;\n  }", edit, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AppJs_HashInboxAndHashTasksOpenTheBacklogsInPlace()
    {
        string js = await Fetch("/app.js");

        Assert.Contains("const InboxRoute = \"#inbox\";\nconst TasksRoute = \"#tasks\";\n", js, StringComparison.Ordinal);
        string viewFor = FunctionBody(js, "function viewFor(hash) {");
        Assert.Contains("if (hash === InboxRoute || hash === TasksRoute) {\n    return \"backlogs\";\n  }", viewFor,
            StringComparison.Ordinal);
        Assert.DoesNotContain("return \"inbox\"", js, StringComparison.Ordinal);
        Assert.DoesNotContain("return \"tasks\"", js, StringComparison.Ordinal);
        string route = FunctionBody(js, "function route() {");
        Assert.Contains("const fromOldTab = location.hash === InboxRoute || location.hash === TasksRoute;", route,
            StringComparison.Ordinal);
        const string Replace = "if (fromOldTab) {\n    history.replaceState(null, \"\", BacklogsRoute);\n  }";
        Assert.Contains(Replace, route, StringComparison.Ordinal);
        Assert.DoesNotContain("history.pushState", route, StringComparison.Ordinal);
        // The replacement happens before showView, so the Backlogs tab is the one marked current.
        Assert.True(route.IndexOf(Replace, StringComparison.Ordinal) < route.IndexOf("showView(next);", StringComparison.Ordinal));
        Assert.Contains("  if (fromOldTab) {\n    listScroll = 0;\n  }\n  showStatus();", route, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EditJs_ADecidedStoryGoesBackOrToTheBacklogs()
    {
        string edit = await Fetch("/edit.js");

        string leave = FunctionBody(edit, "function leaveDecidedStory() {");
        Assert.Contains("history.replaceState(null, \"\", BacklogsRoute);\n  route();", leave, StringComparison.Ordinal);
        Assert.DoesNotContain("InboxRoute", edit, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BacklogsApi_HoldsTheTasksAndUnapprovedLinesOfEveryProjectIncludingNone()
    {
        using TempFactoryRoot root = new();
        root.AddProject("Alpha", "- [h] First task\n- [?] Proposal one\n- [?] Proposal two\n- [ ] Open\n- [x] Done\n");
        root.AddProject("Beta", "- [h] Second task\n- [?] Proposal three\n");
        root.AddProject("Gamma", "- [ ] Only open\n- [x] Done too\n");
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();

        using JsonDocument json = JsonDocument.Parse(await client.GetStringAsync("/api/backlogs"));

        Dictionary<string, (int User, int Approval)> counts = json.RootElement.GetProperty("projects").EnumerateArray()
            .ToDictionary(
                project => project.GetProperty("name").GetString()!,
                project => (project.GetProperty("user").GetArrayLength(), project.GetProperty("approval").GetArrayLength()));
        Assert.Equal((1, 2), counts["Alpha"]);
        Assert.Equal((1, 1), counts["Beta"]);
        Assert.Equal((0, 0), counts["Gamma"]);
        Assert.Equal(2, counts.Values.Sum(pair => pair.User));
        Assert.Equal(3, counts.Values.Sum(pair => pair.Approval));
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
