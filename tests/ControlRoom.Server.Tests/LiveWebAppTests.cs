using System.Text.RegularExpressions;

namespace ControlRoom.Server.Tests;

/// <summary>Checks <c>live.js</c> and its hooks in <c>app.js</c>, <c>board.js</c>, <c>index.html</c>, <c>app.css</c>
/// and <c>sw.js</c> as text (there is no JS test runtime yet, see #59). The live behaviour (views changing without a
/// reload, the notice appearing and going away) is checked by hand.</summary>
public sealed partial class LiveWebAppTests
{
    private static readonly string[] Scripts = ["/token.js", "/board.js", "/act.js", "/push.js", "/edit.js", "/counts.js", "/search.js", "/capture.js", "/history.js", "/doctor.js", "/status.js", "/live.js", "/app.js"];

    [Fact]
    public async Task IndexHtml_LoadsLiveJsAfterEditJsAndBeforeAppJs()
    {
        string html = await Fetch("/index.html");

        int[] positions = Scripts
            .Select(script => html.IndexOf("<script src=\"" + script + "\" defer></script>", StringComparison.Ordinal))
            .ToArray();
        Assert.All(positions, position => Assert.True(position >= 0));
        Assert.Equal(positions.OrderBy(position => position), positions);
    }

    [Fact]
    public async Task LiveJs_AsksChangesThroughAuthFetchEveryFiveSecondsWithOneTimer()
    {
        string live = await Fetch("/live.js");
        string board = await Fetch("/board.js");

        Assert.Contains("const ChangesRoute = \"/api/changes\";", live, StringComparison.Ordinal);
        Assert.Contains("await authFetch(ChangesRoute, { cache: \"no-store\"", live, StringComparison.Ordinal);
        Assert.Contains("liveTimer = setInterval(checkChanges, PollInterval);", live, StringComparison.Ordinal);
        Assert.Contains("const PollInterval = 5000;", board, StringComparison.Ordinal);
        Assert.Contains("if (liveTimer !== null) {", live, StringComparison.Ordinal);
        Assert.DoesNotContain("fetch(\"/api", live, StringComparison.Ordinal);
        Assert.DoesNotContain("EventSource", live, StringComparison.Ordinal);
        Assert.DoesNotContain("setTimeout", live, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WebApp_UsesSetIntervalExactlyOnce()
    {
        int count = 0;
        foreach (string script in Scripts)
        {
            count += SetInterval().Matches(await Fetch(script)).Count;
        }

        Assert.Equal(1, count);
        Assert.Single(SetInterval().Matches(await Fetch("/live.js")));
    }

    [Fact]
    public async Task LiveJs_ChecksOnlyWhileVisibleWithoutTokenViewAndNothingPending()
    {
        string live = await Fetch("/live.js");

        Assert.Contains(
            "if (document.visibilityState !== \"visible\" || tokenViewShown() || livePending) {\n    return;\n  }\n  livePending = true;",
            live, StringComparison.Ordinal);
        Assert.Contains("} finally {\n    livePending = false;\n  }", live, StringComparison.Ordinal);
        int listener = live.IndexOf("document.addEventListener(\"visibilitychange\", () => {", StringComparison.Ordinal);
        Assert.True(listener >= 0);
        Assert.Contains("if (document.visibilityState === \"visible\") {\n      checkChanges();", live[listener..],
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task LiveJs_FetchesTheBoardOnlyWhenJobsOrABacklogChanged()
    {
        string live = await Fetch("/live.js");
        string board = await Fetch("/board.js");

        Assert.Contains("if (previous === null) {\n    return;\n  }", live, StringComparison.Ordinal);
        Assert.Contains("if (previous.jobs !== next.jobs) {\n    boardStale = true;", live, StringComparison.Ordinal);
        Assert.Contains("const backlogChanged = !before || !after || before.backlog !== after.backlog;", live,
            StringComparison.Ordinal);
        Assert.Contains("classList.contains(\"error\")) {\n      if (await pollBoard()) {\n        boardStale = false;", live,
            StringComparison.Ordinal);
        Assert.Single(PollBoardCall().Matches(live));
        Assert.DoesNotContain("startBoardPolling", board, StringComparison.Ordinal);
        Assert.DoesNotContain("setInterval", board, StringComparison.Ordinal);
        Assert.Contains(
            "if (document.visibilityState !== \"visible\" || tokenViewShown() || !boardViewsVisible() || boardPolling) {\n    return false;",
            board, StringComparison.Ordinal);
        Assert.Contains("if (text === boardText) {\n      return true;", board, StringComparison.Ordinal);
        Assert.Contains("const scroll = window.scrollY;\n  content.replaceChildren(...nodes);", board,
            StringComparison.Ordinal);
        Assert.Contains("selectedProject = readSavedProject();", board, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AppJs_RefreshesOnlyChangedBacklogSectionsInPlace()
    {
        string app = await Fetch("/app.js");
        string live = await Fetch("/live.js");

        Assert.Contains("section.dataset.project = name;", app, StringComparison.Ordinal);
        Assert.Contains("if (old && (!changed || sectionBusy(old))) {", app, StringComparison.Ordinal);
        Assert.Contains("keepOpenGroups(old, next);\n      old.replaceWith(next);", app, StringComparison.Ordinal);
        Assert.Contains("group.open = before.get(group.dataset.group);", app, StringComparison.Ordinal);
        Assert.Contains("return section.querySelector(\".edit-title-form\") !== null", app, StringComparison.Ordinal);
        Assert.Contains("|| section.querySelector(\"details.edit-add[open]\") !== null", app, StringComparison.Ordinal);
        Assert.Contains("|| section.contains(document.activeElement);", app, StringComparison.Ordinal);
        Assert.Contains("restoreListScroll(main, scroll);\n  return left;", app, StringComparison.Ordinal);
        Assert.Contains("window.scrollTo(0, scroll);", app, StringComparison.Ordinal);
        // A section left alone stays stale and is refreshed at a later check.
        Assert.Contains("for (const name of await refreshBacklogs(names)) {\n      staleBacklogs.add(name);", live,
            StringComparison.Ordinal);
        Assert.Contains("staleBacklogs.add(name);", live, StringComparison.Ordinal);
        Assert.Contains("liveRouteChanged();", app, StringComparison.Ordinal);
        Assert.Contains("const editingProjects = new Set();", await Fetch("/edit.js"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AppJs_RefreshesTheOpenStoryInPlaceWithoutLoadingOrMovingFocus()
    {
        string app = await Fetch("/app.js");
        string live = await Fetch("/live.js");

        int start = app.IndexOf("async function refreshStory() {", StringComparison.Ordinal);
        Assert.True(start >= 0);
        string refresh = app[start..app.IndexOf("\n}\n", start, StringComparison.Ordinal)];
        Assert.DoesNotContain("Texts.loading", refresh, StringComparison.Ordinal);
        Assert.DoesNotContain("showStoryContent", refresh, StringComparison.Ordinal);
        Assert.Contains("document.querySelector(\"#story .edit-story-form\") !== null", refresh, StringComparison.Ordinal);
        Assert.Contains("old.replaceWith(article);", refresh, StringComparison.Ordinal);
        Assert.Contains("if (focusedTitle) {", refresh, StringComparison.Ordinal);
        Assert.Contains("if (focusedButton) {", refresh, StringComparison.Ordinal);
        Assert.Contains("window.scrollTo(0, scroll);", refresh, StringComparison.Ordinal);
        Assert.Contains("} else if (before.stories !== after.stories) {\n      staleStories.add(name);", live,
            StringComparison.Ordinal);
        Assert.Contains("const wanted = project !== null && staleStories.has(project);", live, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LiveJs_ShowsOneFixedConnectionNoticeAndRefreshesOnRecovery()
    {
        string live = await Fetch("/live.js");
        string board = await Fetch("/board.js");
        string html = await Fetch("/index.html");

        Assert.Contains("lost: \"Connection lost. Reconnecting…\",", live, StringComparison.Ordinal);
        Assert.Contains("const shown = liveLost && !onBoard;", live, StringComparison.Ordinal);
        Assert.Contains("notice.textContent = shown ? LiveTexts.lost : \"\";", live, StringComparison.Ordinal);
        Assert.Contains("status.textContent = LiveTexts.lost;", live, StringComparison.Ordinal);
        Assert.Contains("status.textContent = connectionLost() ? LiveTexts.lost : BoardTexts.unreachable;", board,
            StringComparison.Ordinal);
        Assert.Contains("if (!tokenViewShown()) {\n        setConnectionLost(true);", live, StringComparison.Ordinal);
        Assert.Contains("const recovered = liveLost;\n    setConnectionLost(false);\n    noteChanges(next, recovered);", live,
            StringComparison.Ordinal);
        Assert.Contains("if (recovered) {\n    boardStale = true;\n    jobStale = true;", live, StringComparison.Ordinal);
        Assert.Contains("if (was && !lost) {\n    setBoardStatus(true);", live, StringComparison.Ordinal);

        int header = html.IndexOf("</header>", StringComparison.Ordinal);
        int notice = html.IndexOf("<p class=\"live-notice\" id=\"live-notice\" role=\"status\" hidden></p>",
            StringComparison.Ordinal);
        Assert.True(header >= 0 && notice > header);
        Assert.True(notice < html.IndexOf("<main id=\"backlogs\">", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AppCss_StylesTheNoticeAndHidesItBehindTheTokenView()
    {
        string css = await Fetch("/app.css");

        Assert.Contains(".live-notice {", css, StringComparison.Ordinal);
        Assert.Contains("body:has(#token:not([hidden])) .live-notice {\n  display: none;", css, StringComparison.Ordinal);
        Assert.Contains(".top[hidden] + .live-notice {\n  margin-top: max(8px, env(safe-area-inset-top));", css,
            StringComparison.Ordinal);
        int rule = css.IndexOf(".live-notice {", StringComparison.Ordinal);
        string block = css[rule..css.IndexOf('}', rule)];
        Assert.DoesNotContain("display", block, StringComparison.Ordinal);
        Assert.Contains("color: var(--error);", block, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LiveJs_RendersOnlyFixedTextsAndNoFingerprint()
    {
        string live = await Fetch("/live.js");

        Assert.DoesNotContain("innerHTML", live, StringComparison.Ordinal);
        Assert.DoesNotContain("insertAdjacentHTML", live, StringComparison.Ordinal);
        Assert.DoesNotContain("outerHTML", live, StringComparison.Ordinal);
        foreach (Match assignment in TextAssignment().Matches(live))
        {
            Assert.Contains("LiveTexts.", assignment.Value, StringComparison.Ordinal);
        }

        Assert.Equal(2, TextAssignment().Matches(live).Count);
        Assert.DoesNotContain("console.", live, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ServiceWorker_CachesLiveJsButNeverTheChangesRoute()
    {
        string js = await Fetch("/sw.js");

        Assert.Contains("const CacheVersion = \"v19\";", js, StringComparison.Ordinal);
        Assert.DoesNotContain("const CacheVersion = \"v9\";", js, StringComparison.Ordinal);
        Assert.Contains("  \"/edit.js\",\n  \"/counts.js\",\n  \"/search.js\",\n  \"/capture.js\",\n  \"/history.js\",\n  \"/doctor.js\",\n  \"/status.js\",\n  \"/live.js\",\n", js, StringComparison.Ordinal);
        Assert.DoesNotContain("/api/changes", js, StringComparison.Ordinal);
        Assert.Contains("return Shell.includes(url.pathname);", js, StringComparison.Ordinal);
    }

    private static async Task<string> Fetch(string path)
    {
        using TempFactoryRoot root = new();
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateClient();
        return await client.GetStringAsync(path);
    }

    [GeneratedRegex("\\bsetInterval\\(")]
    private static partial Regex SetInterval();

    [GeneratedRegex("\\bpollBoard\\(\\)")]
    private static partial Regex PollBoardCall();

    [GeneratedRegex("\\.textContent = [^;]+;")]
    private static partial Regex TextAssignment();
}
