using System.Text.RegularExpressions;

namespace ControlRoom.Server.Tests;

/// <summary>Checks <c>tasks.js</c> and its hooks in <c>index.html</c>, <c>app.js</c>, <c>live.js</c>,
/// <c>app.css</c> and <c>sw.js</c> as text (there is no JS test runtime). The tap flow, the confirm and the count
/// update are checked by hand.</summary>
public sealed partial class TasksWebAppTests
{
    [Fact]
    public async Task IndexHtml_HasTheTasksLinkWithACountAfterInboxAndTheTasksView()
    {
        string html = await Fetch("/index.html");

        Assert.Contains(
            "<a href=\"#tasks\" data-view=\"tasks\">Tasks<span class=\"nav-count\" id=\"tasks-count\" hidden></span></a>",
            html, StringComparison.Ordinal);
        int inbox = html.IndexOf("<a href=\"#inbox\" data-view=\"inbox\">", StringComparison.Ordinal);
        int tasks = html.IndexOf("<a href=\"#tasks\" data-view=\"tasks\">", StringComparison.Ordinal);
        int navEnd = html.IndexOf("</nav>", StringComparison.Ordinal);
        Assert.True(inbox >= 0 && tasks > inbox && tasks < navEnd);
        Assert.Contains("<main id=\"tasks\" hidden></main>", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task IndexHtml_LoadsTasksJsBetweenInboxJsAndLiveJs()
    {
        string html = await Fetch("/index.html");
        string[] scripts = ["/token.js", "/board.js", "/act.js", "/push.js", "/edit.js", "/inbox.js", "/tasks.js", "/search.js", "/capture.js", "/history.js", "/doctor.js", "/live.js", "/app.js"];

        int[] positions = scripts
            .Select(script => html.IndexOf("<script src=\"" + script + "\" defer></script>", StringComparison.Ordinal))
            .ToArray();
        Assert.All(positions, position => Assert.True(position >= 0));
        Assert.Equal(positions.OrderBy(position => position), positions);
        Assert.Equal(13, ScriptTag().Matches(html).Count);
    }

    [Fact]
    public async Task AppJs_RoutesHashTasksToTheTasksViewTitledTasks()
    {
        string app = await Fetch("/app.js");
        string tasks = await Fetch("/tasks.js");

        Assert.Contains("const Views = [\"backlogs\", \"board\", \"inbox\", \"tasks\", \"search\", \"capture\", \"history\", \"doctor\", \"job\", \"story\"];", app,
            StringComparison.Ordinal);
        Assert.Contains(
            "const ViewTitles = { backlogs: \"Backlogs\", board: \"Jobs\", inbox: \"Inbox\", tasks: \"Tasks\", search: \"Search\",\n  capture: \"Capture\", history: \"History\", doctor: \"Health\" };", app,
            StringComparison.Ordinal);
        Assert.Contains("if (hash === TasksRoute) {\n    return \"tasks\";\n  }", app, StringComparison.Ordinal);
        Assert.Contains("if (next === \"tasks\") {\n    showTasks();\n    return;\n  }", app, StringComparison.Ordinal);
        Assert.Contains("if (currentView === \"tasks\" && next !== \"tasks\") {\n    rememberTasksScroll();", app,
            StringComparison.Ordinal);
        Assert.Contains("const TasksRoute = \"#tasks\";", tasks, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TasksJs_FetchesOnlyThroughAuthFetchWithNoStore()
    {
        string js = await Fetch("/tasks.js");

        Assert.Contains("const TasksApi = \"/api/tasks\";", js, StringComparison.Ordinal);
        Assert.Contains("await authFetch(TasksApi, { cache: \"no-store\" });", js, StringComparison.Ordinal);
        Assert.Single(AuthFetchCall().Matches(js));
        Assert.DoesNotContain(" fetch(", js, StringComparison.Ordinal);
        Assert.DoesNotContain("/api/backlog/change", js, StringComparison.Ordinal);
        Assert.DoesNotContain("setInterval", js, StringComparison.Ordinal);
        Assert.DoesNotContain("setTimeout", js, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TasksJs_RendersReadyAndWaitingSectionsWithFixedTexts()
    {
        string js = await Fetch("/tasks.js");

        Assert.Contains("ready: \"Ready now\",", js, StringComparison.Ordinal);
        Assert.Contains("waiting: \"Waiting\",", js, StringComparison.Ordinal);
        Assert.Contains("readyEmpty: \"Nothing to do right now.\",", js, StringComparison.Ordinal);
        Assert.Contains("waitingEmpty: \"Nothing waiting.\",", js, StringComparison.Ordinal);
        Assert.Contains("loading: \"Loading…\",", js, StringComparison.Ordinal);
        Assert.Contains("failed: \"Can't load the tasks.\",", js, StringComparison.Ordinal);
        Assert.Contains(
            "renderTaskSection(\"tasks-ready\", TasksTexts.ready, TasksTexts.readyEmpty, tasks.ready, true)", js,
            StringComparison.Ordinal);
        Assert.Contains(
            "renderTaskSection(\"tasks-waiting\", TasksTexts.waiting, TasksTexts.waitingEmpty, tasks.waiting, false)", js,
            StringComparison.Ordinal);
        Assert.Contains("section.appendChild(element(\"p\", \"empty\", emptyText));", js, StringComparison.Ordinal);
        Assert.Contains("showTasksContent([element(\"p\", \"error\", TasksTexts.failed)]);", js, StringComparison.Ordinal);
        Assert.Contains("showTasksContent([element(\"p\", \"status\", TasksTexts.loading)]);", js, StringComparison.Ordinal);
        Assert.DoesNotContain("data.error", js, StringComparison.Ordinal);
        Assert.DoesNotContain(".message", js, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TasksJs_SetsFileTextOnlyThroughTextContent()
    {
        string js = await Fetch("/tasks.js");

        Assert.DoesNotContain("innerHTML", js, StringComparison.Ordinal);
        Assert.DoesNotContain("insertAdjacentHTML", js, StringComparison.Ordinal);
        Assert.DoesNotContain("outerHTML", js, StringComparison.Ordinal);
        Assert.Contains("row.appendChild(element(\"span\", \"tasks-project\", item.project));", js, StringComparison.Ordinal);
        Assert.Contains("row.appendChild(element(\"span\", \"rest\", item.rest));", js, StringComparison.Ordinal);
        foreach (Match assignment in TextAssignment().Matches(js))
        {
            Assert.Contains("String(tasksCount)", assignment.Value, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task TasksJs_ANumberedItemLinksItsStory()
    {
        string js = await Fetch("/tasks.js");

        Assert.Contains(
            "const label = (item.number !== null ? \"#\" + item.number + \" \" : \"\") + titleText(item.title);", js,
            StringComparison.Ordinal);
        Assert.Contains("head.href = storyHash(item.project, item.number);", js, StringComparison.Ordinal);
        Assert.Contains("openStory(item.project, item.number);", js, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TasksJs_AWaitingItemLinksEachUnmetDependencyByProject()
    {
        string js = await Fetch("/tasks.js");

        Assert.Contains("waitsFor: \"Waits for\",", js, StringComparison.Ordinal);
        Assert.Contains("line.appendChild(element(\"span\", \"after-label\", TasksTexts.waitsFor));", js,
            StringComparison.Ordinal);
        Assert.Contains("const own = dependency.project === item.project;", js, StringComparison.Ordinal);
        Assert.Contains("const label = (own ? \"\" : dependency.project) + \"#\" + dependency.number;", js,
            StringComparison.Ordinal);
        Assert.Contains("link.href = storyHash(dependency.project, dependency.number);", js, StringComparison.Ordinal);
        Assert.Contains("if (item.after.length > 0) {\n    row.appendChild(renderTaskWaits(item));", js,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task TasksJs_OnlyReadyItemsHaveDoneWhichConfirmsThenSendsThroughSendChange()
    {
        string js = await Fetch("/tasks.js");

        Assert.Contains("if (ready) {\n    row.appendChild(taskActions(item));\n  }", js, StringComparison.Ordinal);
        Assert.Contains("done: \"Done\",", js, StringComparison.Ordinal);
        Assert.Contains("confirmDone: \"Mark this task done? Its line becomes [x] in one commit.\",", js,
            StringComparison.Ordinal);
        Assert.Contains("const subject = item.number !== null ? \"#\" + item.number : titleText(item.title);", js,
            StringComparison.Ordinal);
        Assert.Contains("done.setAttribute(\"aria-label\", TasksTexts.done + \" \" + subject);", js, StringComparison.Ordinal);
        Assert.Contains(
            "if (confirm(TasksTexts.confirmDone)) {\n      sendTaskChange({ project: item.project, version: item.version, line: item.line, action: \"done\" });\n    }",
            js, StringComparison.Ordinal);
        Assert.Single(ConfirmCall().Matches(js));
        Assert.Contains("if (await sendChange(body)) {\n    loadTasks();\n  }", js, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TasksJs_LeavesTheListAloneWhileAChangeIsSent()
    {
        string js = await Fetch("/tasks.js");

        Assert.Contains("if (changeBusy) {\n    return false;\n  }\n  const scroll = window.scrollY;\n  renderTasks(tasks);", js,
            StringComparison.Ordinal);
        Assert.Contains("if (tasksShown() && !changeBusy) {", js, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TasksJs_CountIsTheReadyCountHiddenAtZeroOrUnknown_AndFetchedAtLoadAndAfterSave()
    {
        string js = await Fetch("/tasks.js");
        string app = await Fetch("/app.js");

        Assert.Contains("let tasksCount = null;", js, StringComparison.Ordinal);
        Assert.Contains("const shown = tasksCount !== null && tasksCount > 0;", js, StringComparison.Ordinal);
        Assert.Contains("badge.hidden = !shown;", js, StringComparison.Ordinal);
        Assert.Contains("tasksCount = tasks.ready.length;", js, StringComparison.Ordinal);
        // A load in progress never sets the count to 0: it changes only with a result or a failure.
        Assert.Equal(3, CountAssignment().Matches(js).Count);
        Assert.Contains("loadInboxCount();\nloadTasksCount();\nstartLiveChecks();", app, StringComparison.Ordinal);
        Assert.Contains("onTokenSaved(() => {\n  route();\n  load();\n  loadInboxCount();\n  loadTasksCount();\n});", app,
            StringComparison.Ordinal);
        Assert.Contains("function loadTasksCount() {\n  if (currentView !== \"tasks\") {\n    loadTasks();", js,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task LiveJs_RefetchesTheTasksWhenAnyBacklogOrStoriesFingerprintChanged()
    {
        string live = await Fetch("/live.js");

        Assert.Contains("inboxStale = true;\n      tasksStale = true;\n      staleBacklogs.add(name);", live,
            StringComparison.Ordinal);
        Assert.Contains("staleStories.add(name);\n      inboxStale = true;\n      tasksStale = true;", live,
            StringComparison.Ordinal);
        Assert.Contains("inboxStale = true;\n    tasksStale = true;\n    for (const name of names) {", live,
            StringComparison.Ordinal);
        Assert.Contains("await refreshStaleTasks();", live, StringComparison.Ordinal);
        Assert.Contains("tasksStale = false;\n  if (!(await loadTasks())) {\n    tasksStale = true;\n  }", live,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task ServiceWorker_CachesTasksJsWithTheCurrentVersionAndNeverTheApi()
    {
        string js = await Fetch("/sw.js");

        Assert.Contains("const CacheVersion = \"v15\";", js, StringComparison.Ordinal);
        Assert.Contains("  \"/inbox.js\",\n  \"/tasks.js\",\n  \"/search.js\",\n  \"/capture.js\",\n  \"/history.js\",\n  \"/doctor.js\",\n  \"/live.js\",\n", js, StringComparison.Ordinal);
        Assert.DoesNotContain("/api/tasks", js, StringComparison.Ordinal);
        Assert.Contains("if (url.pathname.startsWith(\"/api/\") || url.pathname === \"/health\") {\n    return false;", js,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task AppCss_StylesTheTasksView()
    {
        string css = await Fetch("/app.css");

        Assert.Contains(".tasks-section {", css, StringComparison.Ordinal);
        Assert.Contains(".tasks-item {", css, StringComparison.Ordinal);
        Assert.Contains(".tasks-after .dependency {", css, StringComparison.Ordinal);
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

    [GeneratedRegex(@"confirm\(")]
    private static partial Regex ConfirmCall();

    [GeneratedRegex(@"\.textContent\s*=[^;]*;")]
    private static partial Regex TextAssignment();

    [GeneratedRegex(@"tasksCount = ")]
    private static partial Regex CountAssignment();
}
