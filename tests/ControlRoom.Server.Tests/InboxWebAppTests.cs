using System.Text.RegularExpressions;

namespace ControlRoom.Server.Tests;

/// <summary>Checks <c>inbox.js</c> and its hooks in <c>index.html</c>, <c>app.js</c>, <c>live.js</c>,
/// <c>app.css</c> and <c>sw.js</c> as text (there is no JS test runtime yet, see #59). Approving, dropping,
/// cancelling a drop and the count following each change are checked by hand.</summary>
public sealed partial class InboxWebAppTests
{
    [Fact]
    public async Task IndexHtml_HasTheInboxLinkWithACountAndTheInboxView()
    {
        string html = await Fetch("/index.html");

        Assert.Contains(
            "<a href=\"#inbox\" data-view=\"inbox\">Inbox<span class=\"nav-count\" id=\"inbox-count\" hidden></span></a>",
            html, StringComparison.Ordinal);
        int nav = html.IndexOf("<nav class=\"nav\"", StringComparison.Ordinal);
        int link = html.IndexOf("<a href=\"#inbox\" data-view=\"inbox\">", StringComparison.Ordinal);
        Assert.True(nav >= 0 && link > nav && link < html.IndexOf("</nav>", StringComparison.Ordinal));
        Assert.Contains("<main id=\"inbox\" hidden></main>", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task IndexHtml_LoadsInboxJsBetweenEditJsAndLiveJs()
    {
        string html = await Fetch("/index.html");
        string[] scripts = ["/token.js", "/board.js", "/push.js", "/edit.js", "/inbox.js", "/tasks.js", "/search.js", "/capture.js", "/live.js", "/app.js"];

        int[] positions = scripts
            .Select(script => html.IndexOf("<script src=\"" + script + "\" defer></script>", StringComparison.Ordinal))
            .ToArray();
        Assert.All(positions, position => Assert.True(position >= 0));
        Assert.Equal(positions.OrderBy(position => position), positions);
        Assert.Equal(10, ScriptTag().Matches(html).Count);
    }

    [Fact]
    public async Task AppJs_RoutesHashInboxToTheInboxViewTitledInbox()
    {
        string app = await Fetch("/app.js");
        string inbox = await Fetch("/inbox.js");

        Assert.Contains("const Views = [\"backlogs\", \"board\", \"inbox\", \"tasks\", \"search\", \"capture\", \"job\", \"story\"];", app, StringComparison.Ordinal);
        Assert.Contains("const ViewTitles = { backlogs: \"Backlogs\", board: \"Jobs\", inbox: \"Inbox\", tasks: \"Tasks\", search: \"Search\",\n  capture: \"Capture\" };", app,
            StringComparison.Ordinal);
        Assert.Contains("if (hash === InboxRoute) {\n    return \"inbox\";\n  }", app, StringComparison.Ordinal);
        Assert.Contains("if (next === \"inbox\") {\n    showInbox();\n    return;\n  }", app, StringComparison.Ordinal);
        Assert.Contains("if (currentView === \"inbox\" && next !== \"inbox\") {\n    rememberInboxScroll();", app,
            StringComparison.Ordinal);
        Assert.Contains("const InboxRoute = \"#inbox\";", inbox, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ServiceWorker_CachesInboxJsWithABumpedVersionAndNeverTheApi()
    {
        string js = await Fetch("/sw.js");

        Assert.Contains("const CacheVersion = \"v12\";", js, StringComparison.Ordinal);
        Assert.Contains("  \"/inbox.js\",\n", js, StringComparison.Ordinal);
        Assert.DoesNotContain("/api/inbox", js, StringComparison.Ordinal);
        Assert.Contains("if (url.pathname.startsWith(\"/api/\") || url.pathname === \"/health\") {\n    return false;", js,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task InboxJs_FetchesOnlyThroughAuthFetchWithNoStore()
    {
        string js = await Fetch("/inbox.js");

        Assert.Contains("const InboxApi = \"/api/inbox\";", js, StringComparison.Ordinal);
        Assert.Contains("await authFetch(InboxApi, { cache: \"no-store\" });", js, StringComparison.Ordinal);
        Assert.Single(AuthFetchCall().Matches(js));
        Assert.DoesNotContain("fetch(\"/api", js, StringComparison.Ordinal);
        Assert.DoesNotContain(" fetch(", js, StringComparison.Ordinal);
        Assert.DoesNotContain("setInterval", js, StringComparison.Ordinal);
        Assert.DoesNotContain("setTimeout", js, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InboxJs_RendersProjectTitleAndJobLinkAsTextOnly()
    {
        string js = await Fetch("/inbox.js");

        Assert.DoesNotContain("innerHTML", js, StringComparison.Ordinal);
        Assert.DoesNotContain("insertAdjacentHTML", js, StringComparison.Ordinal);
        Assert.DoesNotContain("outerHTML", js, StringComparison.Ordinal);
        Assert.Contains("row.appendChild(element(\"span\", \"inbox-project\", item.project));", js, StringComparison.Ordinal);
        Assert.Contains(
            "const label = (item.number !== null ? \"#\" + item.number + \" \" : \"\") + titleText(item.title);", js,
            StringComparison.Ordinal);
        Assert.Contains("proposedBy: \"Proposed by \",", js, StringComparison.Ordinal);
        Assert.Contains(
            "if (item.job !== null) {\n    const job = element(\"a\", \"inbox-job\", InboxTexts.proposedBy + item.job);\n    job.href = jobHash(item.job);",
            js, StringComparison.Ordinal);
        // The client keeps only ids that match the server's rule.
        Assert.Contains("const InboxJobId = /^[0-9]{1,6}-[a-z0-9]+(-[a-z0-9]+)*$/;", js, StringComparison.Ordinal);
        Assert.Contains("job: InboxJobId.test(job) ? job : null,", js, StringComparison.Ordinal);
        foreach (Match assignment in TextAssignment().Matches(js))
        {
            Assert.Contains("String(inboxCount)", assignment.Value, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task InboxJs_ANumberedItemOpensItsStoryThroughOpenStory()
    {
        string js = await Fetch("/inbox.js");
        string app = await Fetch("/app.js");

        Assert.Contains("head.href = storyHash(item.project, item.number);", js, StringComparison.Ordinal);
        Assert.Contains(
            "event.preventDefault();\n      event.stopPropagation();\n      openStory(item.project, item.number);", js,
            StringComparison.Ordinal);
        Assert.Contains(
            "if (event.target instanceof Element && event.target.closest(\"a, button\")) {\n        return;\n      }\n      openStory(item.project, item.number);",
            js, StringComparison.Ordinal);
        // openStory marks the story as opened from a list, so Back goes back in history to the inbox.
        Assert.Contains("function openStory(project, number) {\n  openedFromList = true;", app, StringComparison.Ordinal);
        Assert.Contains("if (openedFromList) {\n    history.back();", app, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InboxJs_ApproveAndDropSendTheInboxFieldsThroughSendChange()
    {
        string js = await Fetch("/inbox.js");

        Assert.Contains("const subject = item.number !== null ? \"#\" + item.number : titleText(item.title);", js,
            StringComparison.Ordinal);
        Assert.Contains("approve.setAttribute(\"aria-label\", EditTexts.approve + \" \" + subject);", js, StringComparison.Ordinal);
        Assert.Contains("drop.setAttribute(\"aria-label\", EditTexts.drop + \" \" + subject);", js, StringComparison.Ordinal);
        Assert.Contains("editButton(EditTexts.approve, \"edit-primary\", () => {\n    sendInboxChange({ project: item.project, version: item.version, line: item.line, action: \"approve\" });",
            js, StringComparison.Ordinal);
        Assert.Contains(
            "editButton(EditTexts.drop, \"edit-danger\", () => {\n    if (confirm(EditTexts.confirmDrop)) {\n      sendInboxChange({ project: item.project, version: item.version, line: item.line, action: \"drop\" });\n    }",
            js, StringComparison.Ordinal);
        Assert.Single(ConfirmCall().Matches(js));
        Assert.Contains("if (await sendChange(body)) {\n    loadInbox();\n  }", js, StringComparison.Ordinal);
        Assert.DoesNotContain("/api/backlog/change", js, StringComparison.Ordinal);
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
    public async Task InboxJs_ShowsOnlyFixedTextsForEmptyFailedAndUnreadable()
    {
        string js = await Fetch("/inbox.js");

        Assert.Contains("empty: \"Nothing waiting for approval.\",", js, StringComparison.Ordinal);
        Assert.Contains("failed: \"Can't load the inbox.\",", js, StringComparison.Ordinal);
        Assert.Contains("unreadable: \": backlog can't be read.\",", js, StringComparison.Ordinal);
        Assert.Contains("nodes.push(element(\"p\", \"error inbox-unreadable\", name + InboxTexts.unreadable));", js,
            StringComparison.Ordinal);
        Assert.Contains("nodes.push(element(\"p\", \"empty\", InboxTexts.empty));", js, StringComparison.Ordinal);
        Assert.Contains("showInboxContent([element(\"p\", \"error\", InboxTexts.failed)]);", js, StringComparison.Ordinal);
        Assert.DoesNotContain(".error)", js, StringComparison.Ordinal);
        Assert.DoesNotContain("data.error", js, StringComparison.Ordinal);
        Assert.DoesNotContain(".message", js, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InboxJs_CountIsHiddenAtZeroOrUnknownAndLabelsTheLink()
    {
        string js = await Fetch("/inbox.js");
        string app = await Fetch("/app.js");

        Assert.Contains("let inboxCount = null;", js, StringComparison.Ordinal);
        Assert.Contains("const shown = inboxCount !== null && inboxCount > 0;", js, StringComparison.Ordinal);
        Assert.Contains("badge.hidden = !shown;", js, StringComparison.Ordinal);
        Assert.Contains(
            "link.setAttribute(\"aria-label\", shown ? InboxTexts.link + \", \" + inboxCount + \" waiting\" : InboxTexts.link);",
            js, StringComparison.Ordinal);
        Assert.Contains("inboxCount = inbox.items.length;", js, StringComparison.Ordinal);
        // A load in progress never sets the count to 0: it changes only with a result or a failure.
        Assert.Equal(3, CountAssignment().Matches(js).Count);
        // Once per page load and after a token is saved (the inbox view fetches it itself).
        Assert.Contains("route();\nload();\nloadInboxCount();\nloadTasksCount();\nstartLiveChecks();", app, StringComparison.Ordinal);
        Assert.Contains("onTokenSaved(() => {\n  route();\n  load();\n  loadInboxCount();\n  loadTasksCount();\n});", app, StringComparison.Ordinal);
        Assert.Contains("function loadInboxCount() {\n  if (currentView !== \"inbox\") {\n    loadInbox();", js,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task InboxJs_LeavesTheListAloneWhileAChangeIsSent()
    {
        string js = await Fetch("/inbox.js");

        Assert.Contains("if (changeBusy) {\n    return false;\n  }\n  const scroll = window.scrollY;\n  renderInbox(inbox);", js,
            StringComparison.Ordinal);
        Assert.Contains("if (inboxShown() && !changeBusy) {", js, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LiveJs_RefetchesTheInboxWhenAnyBacklogOrStoriesFingerprintChanged()
    {
        string live = await Fetch("/live.js");

        Assert.Contains("if (backlogChanged) {\n      boardStale = true;\n      inboxStale = true;", live, StringComparison.Ordinal);
        Assert.Contains("} else if (before.stories !== after.stories) {\n      staleStories.add(name);\n      inboxStale = true;",
            live, StringComparison.Ordinal);
        Assert.Contains("if (recovered) {\n    boardStale = true;\n    jobStale = true;\n    inboxStale = true;", live,
            StringComparison.Ordinal);
        Assert.Contains("await refreshStaleInbox();\n}", live, StringComparison.Ordinal);
        Assert.Contains("inboxStale = false;\n  if (!(await loadInbox())) {\n    inboxStale = true;\n  }", live,
            StringComparison.Ordinal);
        // A Drop confirm is not pending work: the check condition is unchanged and names only livePending.
        Assert.Contains(
            "if (document.visibilityState !== \"visible\" || tokenViewShown() || livePending) {\n    return;\n  }", live,
            StringComparison.Ordinal);
        Assert.DoesNotContain("confirm(", live, StringComparison.Ordinal);
        Assert.Single(SetInterval().Matches(live));
    }

    [Fact]
    public async Task AppCss_StylesTheCountBadgeAndLetsTheNavWrap()
    {
        string css = await Fetch("/app.css");

        Assert.Contains(".nav {\n  display: flex;\n  flex-wrap: wrap;", css, StringComparison.Ordinal);
        Assert.Contains(".nav-count {", css, StringComparison.Ordinal);
        Assert.Contains(".nav a[aria-current=\"page\"] .nav-count {", css, StringComparison.Ordinal);
        Assert.Contains(".inbox-item {", css, StringComparison.Ordinal);
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

    [GeneratedRegex("\\bsetInterval\\(")]
    private static partial Regex SetInterval();

    [GeneratedRegex("\\bauthFetch\\(")]
    private static partial Regex AuthFetchCall();

    [GeneratedRegex("\\bconfirm\\(")]
    private static partial Regex ConfirmCall();

    [GeneratedRegex("\\.textContent = [^;]+;")]
    private static partial Regex TextAssignment();

    [GeneratedRegex("\\binboxCount = ")]
    private static partial Regex CountAssignment();
}
