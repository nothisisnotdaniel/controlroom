using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ControlRoom.Server.Tests;

/// <summary>Checks the served web app files (<c>index.html</c>, <c>app.js</c>, <c>app.css</c>). Layout at 375 px and
/// the look in light and dark mode are checked by hand.</summary>
public sealed partial class WebAppTests
{
    [Fact]
    public async Task IndexHtml_HasViewportColorSchemeAndOnlySameOriginFiles()
    {
        string html = await Fetch("/index.html");

        Assert.Contains("<meta name=\"viewport\" content=\"width=device-width", html, StringComparison.Ordinal);
        Assert.Contains("<meta name=\"color-scheme\" content=\"light dark\">", html, StringComparison.Ordinal);
        Assert.Contains("<script src=\"/app.js\" defer></script>", html, StringComparison.Ordinal);
        Assert.Contains("<link rel=\"stylesheet\" href=\"/app.css\">", html, StringComparison.Ordinal);
        Assert.Equal(14, ScriptTag().Matches(html).Count);
        Assert.DoesNotContain("<style", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("style=", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(" on", AttributeStarts(html), StringComparison.Ordinal);
        Assert.DoesNotContain("//", string.Concat(SourceAttribute().Matches(html).Select(match => match.Value)),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task AppJs_FetchesBacklogsAndRendersGroupsInOrder()
    {
        string js = await Fetch("/app.js");

        Assert.Contains("authFetch(\"/api/backlogs\"", js, StringComparison.Ordinal);
        int open = js.IndexOf("label: \"Open\"", StringComparison.Ordinal);
        int approval = js.IndexOf("label: \"Waiting for approval\"", StringComparison.Ordinal);
        int user = js.IndexOf("label: \"For you\"", StringComparison.Ordinal);
        int done = js.IndexOf("label: \"Done\", collapsed: true", StringComparison.Ordinal);
        Assert.True(open >= 0 && open < approval && approval < user && user < done);
        Assert.Contains("element(\"details\", \"group\")", js, StringComparison.Ordinal);
        Assert.DoesNotContain(".open = true", js, StringComparison.Ordinal);
        Assert.DoesNotContain("setAttribute(\"open\"", js, StringComparison.Ordinal);
        Assert.Contains("group.label + \" (\" + entries.length + \")\"", js, StringComparison.Ordinal);
        Assert.Contains("element(\"strong\", \"title\"", js, StringComparison.Ordinal);
        Assert.Contains("if (rest !== \"\")", js, StringComparison.Ordinal);
        Assert.Contains("element(\"h2\", null, asText(project && project.name))", js, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AppJs_UsesOnlyFixedErrorTexts()
    {
        string js = await Fetch("/app.js");

        Assert.Contains("unreachable: \"Can't reach the server.\"", js, StringComparison.Ordinal);
        Assert.Contains("unreadable: \"Can't read backlog.md. Reload to try again.\"", js, StringComparison.Ordinal);
        Assert.DoesNotContain("unreadable: \"Can't read backlog.md.\"", js, StringComparison.Ordinal);
        Assert.Contains("noLines: \"No backlog lines.\"", js, StringComparison.Ordinal);
        Assert.Contains("element(\"p\", \"error\", Texts.unreadable)", js, StringComparison.Ordinal);
        Assert.Contains("element(\"p\", \"error\", Texts.unreachable)", js, StringComparison.Ordinal);
        Assert.Contains("element(\"p\", \"empty\", Texts.noLines)", js, StringComparison.Ordinal);
        Assert.Contains("catch {", js, StringComparison.Ordinal);
        Assert.DoesNotContain(".message", js, StringComparison.Ordinal);
        Assert.DoesNotContain("statusText", js, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AppJs_RendersNumberedItemsWithMarkWaitingAndDependencyLinks()
    {
        string js = await Fetch("/app.js");

        Assert.Contains("const Marks = { \" \": \"[ ]\", \"?\": \"[?]\", h: \"[h]\", x: \"[x]\" };", js,
            StringComparison.Ordinal);
        Assert.Contains("waiting: \"Waiting\"", js, StringComparison.Ordinal);
        Assert.Contains("if (entry.waiting === true)", js, StringComparison.Ordinal);
        Assert.Contains("element(\"span\", \"badge badge-waiting\", Texts.waiting)", js, StringComparison.Ordinal);
        Assert.Contains("element(\"span\", \"number\", \"#\" + number)", js, StringComparison.Ordinal);
        Assert.Contains("titleElement(entry.title)", js, StringComparison.Ordinal);
        Assert.Contains("const StoryRoute = \"#story/\";", js, StringComparison.Ordinal);
        Assert.Contains("return StoryRoute + encodeURIComponent(project) + \"/\" + number;", js,
            StringComparison.Ordinal);
        Assert.Contains("link.href = storyHash(target, number);", js, StringComparison.Ordinal);
        Assert.Contains("const target = asText(dependency.project) || projectName;", js, StringComparison.Ordinal);
        Assert.Contains("head.href = storyHash(projectName, number);", js, StringComparison.Ordinal);
        // Lines without a number keep the old rendering and get no story link.
        Assert.Contains("if (number !== null) {", js, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AppJs_ShowsTheStoryViewWithBackAndAFixedErrorText()
    {
        string js = await Fetch("/app.js");

        Assert.Contains("storyFailed: \"Can't load this story.\"", js, StringComparison.Ordinal);
        Assert.Contains("element(\"p\", \"error\", Texts.storyFailed)", js, StringComparison.Ordinal);
        Assert.Contains(
            "\"/api/story?project=\" + encodeURIComponent(target.project) + \"&number=\" + "
            + "encodeURIComponent(target.number)",
            js,
            StringComparison.Ordinal);
        Assert.Contains("window.addEventListener(\"hashchange\", route);", js, StringComparison.Ordinal);
        Assert.Contains("element(\"button\", \"back\", Texts.back)", js, StringComparison.Ordinal);
        Assert.Contains("history.back();", js, StringComparison.Ordinal);
        Assert.Contains(
            "asText(data && data.project) + \" #\" + asNumber(data && data.number) + \" \" + "
            + "asText(data && data.title)",
            js,
            StringComparison.Ordinal);
        Assert.Contains("parent.appendChild(element(\"code\", null, text));", js, StringComparison.Ordinal);
        Assert.Contains("parent.appendChild(element(\"span\", null, text));", js, StringComparison.Ordinal);
        Assert.Contains(
            "pre.appendChild(element(\"code\", null, asText(block.text)));", js, StringComparison.Ordinal);
        Assert.Contains("title.focus({ preventScroll: true });", js, StringComparison.Ordinal);
        Assert.DoesNotContain("createTextNode", js, StringComparison.Ordinal);
        Assert.DoesNotContain(".href = asText", js, StringComparison.Ordinal);
    }

    [Fact]
    public async Task IndexHtml_LinksManifestIconAndHomeScreenMetas()
    {
        string html = await Fetch("/index.html");

        Assert.Contains("<link rel=\"manifest\" href=\"/manifest.webmanifest\">", html, StringComparison.Ordinal);
        Assert.Contains("<link rel=\"apple-touch-icon\" href=\"/icons/apple-touch-icon.png\">", html,
            StringComparison.Ordinal);
        Assert.Contains("<meta name=\"apple-mobile-web-app-capable\" content=\"yes\">", html,
            StringComparison.Ordinal);
        Assert.Contains("<meta name=\"mobile-web-app-capable\" content=\"yes\">", html, StringComparison.Ordinal);
        Assert.Contains("<meta name=\"apple-mobile-web-app-title\" content=\"Control Room\">", html,
            StringComparison.Ordinal);
        Assert.Contains("<meta name=\"apple-mobile-web-app-status-bar-style\" content=\"default\">", html,
            StringComparison.Ordinal);
        Assert.Contains("<meta name=\"theme-color\" media=\"(prefers-color-scheme: light)\"", html,
            StringComparison.Ordinal);
        Assert.Contains("<meta name=\"theme-color\" media=\"(prefers-color-scheme: dark)\"", html,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Manifest_DescribesAStandaloneAppWithSameOriginIcons()
    {
        using TempFactoryRoot root = new();
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync("/manifest.webmanifest");
        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        JsonElement manifest = document.RootElement;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/manifest+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("Control Room", manifest.GetProperty("name").GetString());
        Assert.Equal("Control Room", manifest.GetProperty("short_name").GetString());
        Assert.Equal("/", manifest.GetProperty("start_url").GetString());
        Assert.Equal("/", manifest.GetProperty("scope").GetString());
        Assert.Equal("standalone", manifest.GetProperty("display").GetString());
        Assert.Equal("#ffffff", manifest.GetProperty("background_color").GetString());
        Assert.Equal("#ffffff", manifest.GetProperty("theme_color").GetString());
        JsonElement[] icons = manifest.GetProperty("icons").EnumerateArray().ToArray();
        Assert.Equal(2, icons.Length);
        Assert.Equal("/icons/icon-192.png", icons[0].GetProperty("src").GetString());
        Assert.Equal("192x192", icons[0].GetProperty("sizes").GetString());
        Assert.Equal("/icons/icon-512.png", icons[1].GetProperty("src").GetString());
        Assert.Equal("512x512", icons[1].GetProperty("sizes").GetString());
        foreach (JsonElement icon in icons)
        {
            Assert.Equal("image/png", icon.GetProperty("type").GetString());
            Assert.Equal("any", icon.GetProperty("purpose").GetString());
        }

        foreach (string? url in icons.Select(icon => icon.GetProperty("src").GetString())
                     .Append(manifest.GetProperty("start_url").GetString()))
        {
            Assert.NotNull(url);
            Assert.StartsWith("/", url, StringComparison.Ordinal);
            Assert.False(url.StartsWith("//", StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task AppCss_LightBackgroundMatchesTheManifestColours()
    {
        string css = await Fetch("/app.css");

        Assert.Contains("--bg: #ffffff;", css, StringComparison.Ordinal);
    }

    private static string Rule(string css, string selector)
    {
        Match match = Regex.Match(css, Regex.Escape(selector) + @"\s*\{([^}]*)\}");
        Assert.True(match.Success, selector);
        return match.Groups[1].Value;
    }

    [Fact]
    public async Task AppCss_DoneGroupSummaryHidesTheNativeMarkerAndDrawsAChevron()
    {
        string css = await Fetch("/app.css");

        string summary = string.Concat(Regex.Matches(css, @"(?m)^\.group summary \{([^}]*)\}")
            .Select(m => m.Groups[1].Value));
        Assert.Contains("list-style: none;", summary, StringComparison.Ordinal);
        Assert.Contains("min-height: 44px;", summary, StringComparison.Ordinal);
        Assert.Contains("cursor: pointer;", summary, StringComparison.Ordinal);
        Assert.Matches(@"\.group summary::-webkit-details-marker\s*\{\s*display: none;", css);
        string before = Rule(css, ".group summary::before");
        Assert.Contains("content: \"\";", before, StringComparison.Ordinal);
        Assert.DoesNotContain("url(", before, StringComparison.Ordinal);
        Assert.DoesNotContain("http", before, StringComparison.Ordinal);
        Assert.DoesNotContain("#", before, StringComparison.Ordinal);
        Assert.Contains("currentColor", before, StringComparison.Ordinal);
        string open = Rule(css, ".group[open] > summary::before");
        Assert.Contains("transform:", open, StringComparison.Ordinal);
        Assert.NotEqual(before.Trim(), open.Trim());
    }

    [Fact]
    public async Task AppCss_DoneGroupMarkerRulesStayScopedToGroupSummary()
    {
        string css = await Fetch("/app.css");

        Assert.Contains(".group h3,\n.group summary {", css, StringComparison.Ordinal);
        Assert.DoesNotContain(".stage > summary::before", css, StringComparison.Ordinal);
        Assert.DoesNotContain(".edit-add summary::before", css, StringComparison.Ordinal);
        Assert.DoesNotContain("\nsummary::before", css, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AppJs_DoneGroupStaysACollapsedDetailsWithASummary()
    {
        string js = await Fetch("/app.js");

        Assert.Contains("element(\"details\", \"group\")", js, StringComparison.Ordinal);
        Assert.Contains("element(\"summary\", null, heading)", js, StringComparison.Ordinal);
        Assert.Contains("details.open = groupOpen(projectName, key);", js, StringComparison.Ordinal);
        Assert.Contains("return !(group && group.collapsed === true);", js, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AppJs_RegistersTheServiceWorkerQuietlyAndNeverCachesData()
    {
        string js = await Fetch("/app.js");

        Assert.Contains("if (!(\"serviceWorker\" in navigator)) {", js, StringComparison.Ordinal);
        Assert.Contains("navigator.serviceWorker.register(\"/sw.js\").catch(() => undefined);", js,
            StringComparison.Ordinal);
        Assert.DoesNotContain("caches", js, StringComparison.Ordinal);
        Assert.Contains("authFetch(\"/api/backlogs\", { cache: \"no-store\" })", js, StringComparison.Ordinal);
        Assert.Equal(2, NoStore().Matches(js).Count);
        Assert.Contains("unreachable: \"Can't reach the server.\"", js, StringComparison.Ordinal);
        Assert.Contains("storyFailed: \"Can't load this story.\"", js, StringComparison.Ordinal);
    }

    [Fact]
    public async Task IndexHtml_HasAHiddenStoryView()
    {
        string html = await Fetch("/index.html");

        Assert.Contains("<main id=\"story\" hidden></main>", html, StringComparison.Ordinal);
        Assert.Contains("<header class=\"top\" id=\"top\">", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AppCss_StylesBadgesStoryAndAThumbSizedBack()
    {
        string css = await Fetch("/app.css");

        Assert.Contains("--waiting-bg:", css, StringComparison.Ordinal);
        Assert.Contains(".badge-waiting {", css, StringComparison.Ordinal);
        Assert.Contains(".story-bar {", css, StringComparison.Ordinal);
        Assert.Contains("min-height: 44px;", css, StringComparison.Ordinal);
        Assert.Contains("[hidden] {", css, StringComparison.Ordinal);
    }

    [Fact]
    public async Task IndexHtml_HasTheTabBarAndBoardAndJobViewsWithBoardJsBeforeAppJs()
    {
        string html = await Fetch("/index.html");

        Assert.Contains("<nav class=\"nav tab-bar\" aria-label=\"Sections\">", html, StringComparison.Ordinal);
        int jobs = html.IndexOf("<a href=\"#jobs\" data-view=\"board\">", StringComparison.Ordinal);
        int backlogs = html.IndexOf("<a href=\"#backlogs\" data-view=\"backlogs\">", StringComparison.Ordinal);
        Assert.True(jobs >= 0 && jobs < backlogs);
        Assert.Contains("<main id=\"board\" hidden>", html, StringComparison.Ordinal);
        Assert.Contains("<main id=\"job\" hidden></main>", html, StringComparison.Ordinal);
        Assert.Contains("<p class=\"live-status\" id=\"board-status\">", html, StringComparison.Ordinal);
        Assert.Contains("id=\"board-filter\"", html, StringComparison.Ordinal);
        Assert.Contains("<div id=\"board-content\"></div>", html, StringComparison.Ordinal);
        int board = html.IndexOf("<script src=\"/board.js\" defer></script>", StringComparison.Ordinal);
        int app = html.IndexOf("<script src=\"/app.js\" defer></script>", StringComparison.Ordinal);
        Assert.True(board >= 0 && board < app);
    }

    [Fact]
    public async Task AppJs_RoutesJobsJobStoryAndBacklogsAndKeepsScroll()
    {
        string js = await Fetch("/app.js");

        Assert.Contains("const Views = [\"backlogs\", \"board\", \"inbox\", \"tasks\", \"history\", \"doctor\", \"job\", \"story\"];", js, StringComparison.Ordinal);
        Assert.Contains("if (parseStoryHash(hash)) {", js, StringComparison.Ordinal);
        Assert.Contains("if (parseJobHash(hash) !== null) {", js, StringComparison.Ordinal);
        Assert.Contains("return hash === BoardRoute ? \"board\" : \"backlogs\";", js, StringComparison.Ordinal);
        Assert.Contains("showJob(parseJobHash(location.hash));", js, StringComparison.Ordinal);
        Assert.Contains("const target = parseStoryHash(location.hash);\n    if (target !== null) {\n      chooseBacklogProject(target.project);\n    }\n    loadStory(target);", js, StringComparison.Ordinal);
        Assert.Contains("rememberBoardScroll();", js, StringComparison.Ordinal);
        Assert.Contains("window.scrollTo(0, listScroll);", js, StringComparison.Ordinal);
        Assert.Contains("link.setAttribute(\"aria-current\", \"page\");", js, StringComparison.Ordinal);
        Assert.Contains("startLiveChecks();", js, StringComparison.Ordinal);
        Assert.Contains("document.body.classList.toggle(\"view-board\", !blocked && name === \"board\");", js,
            StringComparison.Ordinal);
        Assert.Contains("if (kind === \"table\") {", js, StringComparison.Ordinal);
        Assert.Contains("headRow.appendChild(renderInlines(element(\"th\"), cell));", js, StringComparison.Ordinal);
        Assert.Contains("line.appendChild(renderInlines(element(\"td\"), cell));", js, StringComparison.Ordinal);
    }

    [Fact]
    public async Task IndexHtml_EndsTheJobsViewWithAHiddenVersionLine()
    {
        string html = await Fetch("/index.html");

        int board = html.IndexOf("<main id=\"board\" hidden>", StringComparison.Ordinal);
        int push = html.IndexOf("<section class=\"push\" id=\"push\"", StringComparison.Ordinal);
        int version = html.IndexOf("<p class=\"app-version\" id=\"app-version\" hidden></p>", StringComparison.Ordinal);
        int end = html.IndexOf("</main>", board, StringComparison.Ordinal);
        Assert.True(board >= 0 && board < push && push < version && version < end);
        Assert.Equal("\n    <div class=\"auto-update\" id=\"auto-update\" hidden></div>\n  ",
            html[(version + "<p class=\"app-version\" id=\"app-version\" hidden></p>".Length)..end]);
    }

    [Fact]
    public async Task BoardJs_LoadsTheVersionOnceThroughAuthFetchAndSetsItAsText()
    {
        string js = await Fetch("/board.js");
        string app = await Fetch("/app.js");
        string css = await Fetch("/app.css");

        Assert.Contains("version: \"ControlRoom \"", js, StringComparison.Ordinal);
        Assert.Contains("authFetch(\"/api/version\", { cache: \"no-store\" })", js, StringComparison.Ordinal);
        Assert.Contains("line.textContent = BoardTexts.version + version;", js, StringComparison.Ordinal);
        Assert.Contains("line.hidden = false;", js, StringComparison.Ordinal);
        Assert.Contains("if (versionRequested || !hasToken())", js, StringComparison.Ordinal);
        int start = js.IndexOf("async function loadVersion()", StringComparison.Ordinal);
        int stop = js.IndexOf("\n}\n", start, StringComparison.Ordinal);
        string body = js[start..stop];
        Assert.DoesNotContain("innerHTML", body, StringComparison.Ordinal);
        Assert.DoesNotContain(".message", body, StringComparison.Ordinal);
        Assert.Contains("catch {", body, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(js, @"loadVersion\(\);"));
        Assert.Contains("setBoardStatus(true);\n    loadVersion();", js, StringComparison.Ordinal);
        Assert.DoesNotContain("loadVersion", app, StringComparison.Ordinal);
        Assert.Contains(".app-version {", css, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BoardJs_RoutesAndBackReturnsToTheBoardAtItsScroll()
    {
        string js = await Fetch("/board.js");

        Assert.Contains("const BoardRoute = \"#jobs\";", js, StringComparison.Ordinal);
        Assert.Contains("const JobRoute = \"#job/\";", js, StringComparison.Ordinal);
        Assert.Contains("return JobRoute + encodeURIComponent(id);", js, StringComparison.Ordinal);
        Assert.Contains("const JobIdPattern = /^[0-9]{1,6}-[a-z0-9-]{1,80}$/;", js, StringComparison.Ordinal);
        Assert.Contains("\"/api/job?id=\" + encodeURIComponent(id)", js, StringComparison.Ordinal);
        Assert.Contains("if (openedFromBoard) {", js, StringComparison.Ordinal);
        Assert.Contains("history.back();", js, StringComparison.Ordinal);
        Assert.Contains("history.replaceState(null, \"\", BoardRoute);", js, StringComparison.Ordinal);
        Assert.Contains("window.scrollTo(0, boardScroll);", js, StringComparison.Ordinal);
        Assert.Contains("boardScroll = window.scrollY;", js, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BoardJs_RendersStuckThenStagesInOrderWithCounts()
    {
        string js = await Fetch("/board.js");

        string[] stages =
        [
            "{ key: \"spike\", label: \"Spike\" }", "{ key: \"spec\", label: \"Spec\" }",
            "{ key: \"build\", label: \"Build\" }", "{ key: \"review\", label: \"Review\" }",
            "{ key: \"approve\", label: \"Approve\" }", "{ key: \"done\", label: \"Merged\", collapsed: true }",
        ];
        int[] positions = stages.Select(stage => js.IndexOf(stage, StringComparison.Ordinal)).ToArray();
        Assert.All(positions, position => Assert.True(position >= 0));
        Assert.Equal(positions.Order(), positions);
        Assert.Contains("needsYou: \"Needs you\"", js, StringComparison.Ordinal);
        Assert.Contains("const nodes = stuck.length > 0 ? [renderStuck(stuck)] : [];", js, StringComparison.Ordinal);
        Assert.Contains("return label + \" (\" + count + \")\";", js, StringComparison.Ordinal);
        Assert.Contains("job.stage === \"needs-human\" ? \"card card-human\" : \"card\"", js, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BoardJs_CardShowsModeRoundsNoteBranchTokensCiAndRightText()
    {
        string js = await Fetch("/board.js");

        Assert.Contains("text += \" · light\";", js, StringComparison.Ordinal);
        Assert.Contains("text += \" · spike\";", js, StringComparison.Ordinal);
        Assert.Contains("text += \" · high risk\";", js, StringComparison.Ordinal);
        Assert.Contains("const Reviewers = [\"security\", \"ux\", \"ui\", \"code\"];", js, StringComparison.Ordinal);
        Assert.Contains("if (value === \"skipped\") {", js, StringComparison.Ordinal);
        Assert.Contains("specCheck: \"spec check\",", js, StringComparison.Ordinal);
        Assert.Contains(
            "element(\"span\", chipClass(specReview), chipText(BoardTexts.specCheck, specReview, visible))", js,
            StringComparison.Ordinal);
        Assert.Contains("rounds.appendChild(roundRow(\"R\" + (index + 1), reviews, true, visible));",
            js, StringComparison.Ordinal);
        Assert.Contains("rounds.appendChild(roundRow(\"R\" + round, job.reviews, false, visible));",
            js, StringComparison.Ordinal);
        Assert.Contains("const rounds = reviewRounds(job, false);", js, StringComparison.Ordinal);
        Assert.Contains("element(\"span\", \"card-note\", asText(job.note))", js, StringComparison.Ordinal);
        Assert.Contains("element(\"span\", \"card-branch\", asText(job.branch))", js, StringComparison.Ordinal);
        Assert.Contains(
            "return count >= 1e6 ? (count / 1e6).toFixed(1) + \"M\" : Math.round(count / 1e3) + \"k\";", js,
            StringComparison.Ordinal);
        Assert.Contains("if (ci && ci !== \"skipped\") {", js, StringComparison.Ordinal);
        Assert.Contains("return \"rework \" + (round - 1);", js, StringComparison.Ordinal);
        Assert.Contains("return round ? \"round \" + round : \"\";", js, StringComparison.Ordinal);
        Assert.Contains("return asText(job.decision);", js, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BoardJs_FilterIsKeptInLocalStorageAndFallsBack()
    {
        string js = await Fetch("/board.js");

        Assert.Contains("const ProjectKey = \"controlroom.project\";", js, StringComparison.Ordinal);
        Assert.Contains("localStorage.getItem(ProjectKey)", js, StringComparison.Ordinal);
        Assert.Contains("localStorage.setItem(ProjectKey, name);", js, StringComparison.Ordinal);
        Assert.Contains("allProjects: \"All projects\"", js, StringComparison.Ordinal);
        Assert.Contains("filter.hidden = names.length < 2;", js, StringComparison.Ordinal);
        Assert.Contains("if (selectedProject && !names.includes(selectedProject)) {", js, StringComparison.Ordinal);
        Assert.Contains("boardJobs.filter((job) => job.project === selectedProject)", js, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BoardJs_PollsEveryFiveSecondsOnlyWhileVisibleAndRendersOnlyOnChange()
    {
        string js = await Fetch("/board.js");

        Assert.Contains("const PollInterval = 5000;", js, StringComparison.Ordinal);
        Assert.Contains(
            "if (document.visibilityState !== \"visible\" || tokenViewShown() || !boardViewsVisible() || boardPolling) {", js,
            StringComparison.Ordinal);
        Assert.Contains("authFetch(\"/api/board\", { cache: \"no-store\" })", js, StringComparison.Ordinal);
        Assert.Contains("if (text === boardText) {", js, StringComparison.Ordinal);
        Assert.Contains("window.scrollTo(0, scroll);", js, StringComparison.Ordinal);
        Assert.Contains("JSON.stringify(entry) !== openJobEntry", js, StringComparison.Ordinal);
        Assert.Contains("loadJob(openJobId, true);", js, StringComparison.Ordinal);
        Assert.Contains("live: \"Live · \"", js, StringComparison.Ordinal);
        Assert.Contains("status.textContent = BoardTexts.live + new Date().toLocaleTimeString();", js,
            StringComparison.Ordinal);
        Assert.Contains("unreachable: \"Can't reach the server.\"", js, StringComparison.Ordinal);
        Assert.Contains("status.className = \"live-status error\";", js, StringComparison.Ordinal);
        Assert.Contains("noJobs: \"No jobs yet.\"", js, StringComparison.Ordinal);
        Assert.Contains("element(\"p\", \"empty\", BoardTexts.noJobs)", js, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BoardJs_JobPageShowsFactsTabsVerdictAndFixedTexts()
    {
        string js = await Fetch("/board.js");

        Assert.Contains("job.mode + \" mode\"", js, StringComparison.Ordinal);
        Assert.Contains("job.path + \" path\"", js, StringComparison.Ordinal);
        Assert.Contains("job.risk + \" risk\"", js, StringComparison.Ordinal);
        Assert.Contains("asText(job.branch),", js, StringComparison.Ordinal);
        Assert.Contains("openJobRecord = names.length > 0 ? names[names.length - 1] : null;", js,
            StringComparison.Ordinal);
        Assert.Contains("tab.setAttribute(\"role\", \"tab\");", js, StringComparison.Ordinal);
        Assert.Contains("const Verdict = /^(?:VERDICT: (PASS|CHANGES)|(APPROVE|ESCALATE))$/;",
            js, StringComparison.Ordinal);
        Assert.Contains("recordUnreadable: \"Can't read this record.\"", js, StringComparison.Ordinal);
        Assert.Contains("chipClass(match[1] || match[2]) + \" verdict\"", js, StringComparison.Ordinal);
        Assert.Contains("element(\"p\", \"error\", BoardTexts.recordUnreadable)", js, StringComparison.Ordinal);
        Assert.Contains("jobFailed: \"Can't load this job.\"", js, StringComparison.Ordinal);
        Assert.Contains("element(\"p\", \"error\", BoardTexts.jobFailed)", js, StringComparison.Ordinal);
        Assert.Contains("catch {", js, StringComparison.Ordinal);
        Assert.DoesNotContain(".message", js, StringComparison.Ordinal);
        Assert.DoesNotContain("statusText", js, StringComparison.Ordinal);
        Assert.DoesNotContain("caches", js, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BoardJs_JobPageStateShowsStageAndRoundWithTheBoardLabels()
    {
        string js = await Fetch("/board.js");

        Assert.Contains("jobState: \"Job state\",", js, StringComparison.Ordinal);
        Assert.Contains("jobRound: \" · round \",", js, StringComparison.Ordinal);
        Assert.Contains("needsYou: \"Needs you\",", js, StringComparison.Ordinal);
        foreach (string stage in new[]
                 {
                     "{ key: \"spike\", label: \"Spike\" }", "{ key: \"spec\", label: \"Spec\" }",
                     "{ key: \"build\", label: \"Build\" }", "{ key: \"review\", label: \"Review\" }",
                     "{ key: \"approve\", label: \"Approve\" }",
                     "{ key: \"done\", label: \"Merged\", collapsed: true }",
                 })
        {
            Assert.Contains(stage, js, StringComparison.Ordinal);
        }

        string label = Body(js, "function stageLabel(stage) {");
        Assert.Contains("if (stage === \"needs-human\") {\n    return BoardTexts.needsYou;", label,
            StringComparison.Ordinal);
        Assert.Contains("const known = Stages.find((entry) => entry.key === stage);", label, StringComparison.Ordinal);
        Assert.Contains("return known ? known.label : stage;", label, StringComparison.Ordinal);

        string state = Body(js, "function renderJobState(job) {");
        Assert.Contains("section.setAttribute(\"aria-label\", BoardTexts.jobState);", state, StringComparison.Ordinal);
        Assert.Contains("const stage = asText(job.stage);\n  if (stage) {", state, StringComparison.Ordinal);
        Assert.Contains(
            "element(\"p\", \"job-stage\", stageLabel(stage) + (round ? BoardTexts.jobRound + round : \"\"))", state,
            StringComparison.Ordinal);
        Assert.Contains("const round = asNumber(job.round);", state, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BoardJs_JobPageStateReusesRoundRowWithVisibleValues()
    {
        string js = await Fetch("/board.js");

        Assert.Contains("return visible ? key + \" \" + value : key;", js, StringComparison.Ordinal);
        Assert.Contains("element(\"span\", chipClass(value), chipText(key, value, visible))", js,
            StringComparison.Ordinal);
        Assert.Contains("if (value === \"skipped\") {\n      continue;", Body(js, "function roundRow("),
            StringComparison.Ordinal);
        Assert.Contains("job.specRevised === true ? \"Spec·2\" : \"Spec\"", js, StringComparison.Ordinal);
        Assert.Contains("if (round && job.stage !== \"spec\" && job.stage !== \"build\") {", js,
            StringComparison.Ordinal);

        string state = Body(js, "function renderJobState(job) {");
        Assert.Contains("const rounds = reviewRounds(job, true);\n  if (rounds.childElementCount > 0) {", state,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task BoardJs_JobPageStateShowsDecisionCiAndNoteOnlyWhenSet()
    {
        string js = await Fetch("/board.js");

        Assert.Contains("ci: \"CI \",", js, StringComparison.Ordinal);
        string state = Body(js, "function renderJobState(job) {");
        Assert.Contains("const decision = asText(job.decision);\n  if (decision) {", state, StringComparison.Ordinal);
        Assert.Contains("outcome.appendChild(element(\"span\", chipClass(decision), decision));", state,
            StringComparison.Ordinal);
        // Every CI status is shown here, skipped included (the card hides skipped).
        Assert.Contains("const ci = asText(job.ciStatus);\n  if (ci) {", state, StringComparison.Ordinal);
        Assert.Contains("element(\"span\", \"job-ci\", BoardTexts.ci + ci)", state, StringComparison.Ordinal);
        Assert.Contains("if (outcome.childElementCount > 0) {", state, StringComparison.Ordinal);
        Assert.Contains("const note = asText(job.note);\n  if (note) {", state, StringComparison.Ordinal);
        Assert.Contains("element(\"p\", \"job-note\", note)", state, StringComparison.Ordinal);
        // Classes come only from fixed names and the fixed chipClass map.
        Assert.DoesNotContain("className", state, StringComparison.Ordinal);
        Assert.DoesNotContain("classList", state, StringComparison.Ordinal);
        Assert.DoesNotContain("innerHTML", js, StringComparison.Ordinal);
        Assert.DoesNotContain(".message", js, StringComparison.Ordinal);
        Assert.DoesNotContain("statusText", js, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BoardJs_JobPagePutsTheStateBetweenFactsAndTabs()
    {
        string js = await Fetch("/board.js");

        string page = Body(js, "function renderJobPage(data) {");
        int facts = page.IndexOf("article.appendChild(element(\"p\", \"job-facts\"", StringComparison.Ordinal);
        int state = page.IndexOf(
            "const state = renderJobState(job);\n  if (state.childElementCount > 0) {\n    article.appendChild(state);",
            StringComparison.Ordinal);
        int tabs = page.IndexOf("tabs = renderTabs(records, select);", StringComparison.Ordinal);
        Assert.True(facts >= 0 && state > facts && tabs > state, "facts, state, tabs");
        // A job with only an id still gets its title, facts and tabs: the state section is the only optional part.
        Assert.Contains("element(\"h1\", \"story-title\", asText(job.title) || asText(job.id))", page,
            StringComparison.Ordinal);
        Assert.Contains("const job = asMap(data && data.job);", page, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AppCss_StylesTheJobStateSoItWraps()
    {
        string css = await Fetch("/app.css");

        foreach (string selector in new[] { ".job-state {", ".job-stage {", ".job-outcome {", ".job-note {", ".job-ci {" })
        {
            Assert.Contains(selector, css, StringComparison.Ordinal);
        }

        Assert.Contains("flex-wrap: wrap;", Body(css, ".job-outcome {"), StringComparison.Ordinal);
        Assert.Contains("overflow-wrap: anywhere;", Body(css, ".job-note {"), StringComparison.Ordinal);
        Assert.Contains("white-space: normal;", Body(css, ".job-state .chip {"), StringComparison.Ordinal);
        Assert.DoesNotContain("overflow-x", Body(css, ".job-state {"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AppCss_StylesTheBoardChipsTabsAndTables()
    {
        string css = await Fetch("/app.css");

        foreach (string rule in new[]
                 {
                     ".tab-bar a {", ".stuck {", ".card-human {", ".chip-pass {", ".chip-changes {", ".tab {",
                     ".filter-button {", ".story-table {", "--human:", "--pass-fg:", "--changes-fg:",
                     ".view-board .top {",
                 })
        {
            Assert.Contains(rule, css, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("innerHTML")]
    [InlineData("outerHTML")]
    [InlineData("insertAdjacentHTML")]
    [InlineData("document.write")]
    [InlineData("eval(")]
    [InlineData("new Function")]
    [InlineData("http://")]
    [InlineData("https://")]
    [InlineData("createTextNode")]
    public async Task BoardJs_AvoidsHtmlInjectionApis(string forbidden)
    {
        // The one allowed URL: the fixed release-page prefix the update banner's link must start with.
        const string ReleasePrefix =
            "const UpdateReleasePrefix = \"https://github.com/nothisisnotdaniel/controlroom/releases/tag/\";";
        string js = await Fetch("/board.js");
        Assert.Equal(1, js.Split(ReleasePrefix).Length - 1);
        js = js.Replace(ReleasePrefix, "", StringComparison.Ordinal);

        Assert.DoesNotContain(forbidden, js, StringComparison.Ordinal);
        Assert.Contains("textContent", js, StringComparison.Ordinal);
    }

    [Fact]
    public async Task IndexHtml_LoadsEditJsBeforeAppJsAndHasTheNotice()
    {
        string html = await Fetch("/index.html");

        int edit = html.IndexOf("<script src=\"/edit.js\" defer></script>", StringComparison.Ordinal);
        int board = html.IndexOf("<script src=\"/board.js\" defer></script>", StringComparison.Ordinal);
        int app = html.IndexOf("<script src=\"/app.js\" defer></script>", StringComparison.Ordinal);
        Assert.True(board >= 0 && board < edit && edit < app);
        Assert.Contains("id=\"edit-notice\" role=\"status\"", html, StringComparison.Ordinal);
        Assert.Contains("<p class=\"edit-notice-text\" id=\"edit-notice-text\"></p>", html, StringComparison.Ordinal);
        Assert.Contains("id=\"edit-notice-close\" type=\"button\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EditJs_PostsChangesThroughAuthFetchWithTheVersionItSaw()
    {
        string js = await Fetch("/edit.js");

        Assert.Contains("authFetch(\"/api/backlog/change\", {", js, StringComparison.Ordinal);
        Assert.Contains("method: \"POST\"", js, StringComparison.Ordinal);
        Assert.Contains("headers: { \"Content-Type\": \"application/json\" }", js, StringComparison.Ordinal);
        Assert.Contains("body: JSON.stringify(body)", js, StringComparison.Ordinal);
        Assert.Contains("project: projectName, version: version, line: line", js, StringComparison.Ordinal);
        Assert.Contains("storyVersion: storyVersion", js, StringComparison.Ordinal);
        Assert.Contains("version: found.version", js, StringComparison.Ordinal);
        foreach (string action in new[] { "approve", "drop", "top", "place", "title", "add", "story" })
        {
            Assert.Contains("action: \"" + action + "\"", js, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("fetch(\"/api", js, StringComparison.Ordinal);
        Assert.Equal(2, AuthFetchCall().Matches(js).Count);
    }

    [Fact]
    public async Task EditJs_HasTheControlsAndConfirmsDrop()
    {
        string js = await Fetch("/edit.js");
        string app = await Fetch("/app.js");

        foreach (string label in new[]
                 {
                     "approve: \"Approve\"", "drop: \"Drop\"", "move: \"Move\"",
                     "toTop: \"To top\"", "editTitle: \"Edit title\"", "editStory: \"Edit story\"",
                     "save: \"Save\"", "add: \"Add item\"",
                 })
        {
            Assert.Contains(label, js, StringComparison.Ordinal);
        }

        Assert.Contains("if (mark === \"?\") {", js, StringComparison.Ordinal);
        Assert.Contains("if (mark === \"x\" || line === null || version === \"\") {", js, StringComparison.Ordinal);
        Assert.Contains("if (number !== null) {\n    row.appendChild(named(editButton(EditTexts.editTitle", js,
            StringComparison.Ordinal);
        Assert.Contains("const subject = number !== null ? \"#\" + number : titleText(entry.title);", js,
            StringComparison.Ordinal);
        Assert.Contains("button.setAttribute(\"aria-label\", button.textContent + \" \" + subject);", js,
            StringComparison.Ordinal);
        Assert.Contains("if (confirm(EditTexts.confirmDrop)) {", js, StringComparison.Ordinal);
        Assert.Contains("element(\"textarea\", \"edit-input edit-story-text\")", js, StringComparison.Ordinal);
        Assert.Contains("area.value = markdown;", js, StringComparison.Ordinal);
        Assert.Contains("[[\"top\", EditTexts.top], [\"bottom\", EditTexts.bottom]]", js, StringComparison.Ordinal);
        Assert.Contains("load();", js, StringComparison.Ordinal);
        Assert.Contains("reload();", js, StringComparison.Ordinal);
        Assert.Contains("head.appendChild(editToggle(name, section));", app, StringComparison.Ordinal);
        Assert.Contains("const row = editRow(projectName, version, entry);", app, StringComparison.Ordinal);
        Assert.Contains("const add = addForm(name, version);", app, StringComparison.Ordinal);
        Assert.Contains("storyEditButton(target, data, () => loadStory(target))", app, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EditJs_ShowsOnlyFixedTextsPerErrorCode()
    {
        string js = await Fetch("/edit.js");

        Assert.Contains("not_develop: \"Not on develop. Nothing was changed.\"", js, StringComparison.Ordinal);
        Assert.Contains("dirty: \"Backlog has uncommitted changes.\"", js, StringComparison.Ordinal);
        Assert.Contains("changed: \"Backlog changed since you loaded it. Reload and try again.\"", js,
            StringComparison.Ordinal);
        Assert.Contains("push_failed: \"Committed, but the push failed.\"", js, StringComparison.Ordinal);
        Assert.Contains("invalid: \"", js, StringComparison.Ordinal);
        Assert.Contains("commit_failed: \"", js, StringComparison.Ordinal);
        Assert.Contains("Object.prototype.hasOwnProperty.call(ChangeErrors, code) ? ChangeErrors[code] : ChangeErrors.failed",
            js, StringComparison.Ordinal);
        Assert.Contains("catch {", js, StringComparison.Ordinal);
        Assert.DoesNotContain(".message", js, StringComparison.Ordinal);
        Assert.DoesNotContain("statusText", js, StringComparison.Ordinal);
        Assert.DoesNotContain("textContent = data", js, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("innerHTML")]
    [InlineData("outerHTML")]
    [InlineData("insertAdjacentHTML")]
    [InlineData("document.write")]
    [InlineData("eval(")]
    [InlineData("new Function")]
    [InlineData("http://")]
    [InlineData("https://")]
    public async Task EditJs_AvoidsHtmlInjectionApis(string forbidden)
    {
        string js = await Fetch("/edit.js");

        Assert.DoesNotContain(forbidden, js, StringComparison.Ordinal);
        Assert.Contains("textContent", js, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AppCss_HidesEditControlsOutsideEditModeAndKeepsThemTapSized()
    {
        string css = await Fetch("/app.css");

        Assert.Contains(".project:not(.editing) .edit-only {\n  display: none;\n}", css, StringComparison.Ordinal);
        int button = css.IndexOf(".edit-button {", StringComparison.Ordinal);
        Assert.True(button >= 0);
        Assert.Contains("min-height: 44px;", css[button..css.IndexOf('}', button)], StringComparison.Ordinal);
        Assert.Contains("--danger:", css, StringComparison.Ordinal);
        Assert.Contains(".edit-notice {", css, StringComparison.Ordinal);
        Assert.Contains("body:has(#edit-notice:not([hidden])) {", css, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("innerHTML")]
    [InlineData("outerHTML")]
    [InlineData("insertAdjacentHTML")]
    [InlineData("document.write")]
    [InlineData("eval(")]
    [InlineData("new Function")]
    [InlineData("http://")]
    [InlineData("https://")]
    public async Task AppJs_AvoidsHtmlInjectionApis(string forbidden)
    {
        string js = await Fetch("/app.js");

        Assert.DoesNotContain(forbidden, js, StringComparison.Ordinal);
        Assert.Contains("textContent", js, StringComparison.Ordinal);
        Assert.Contains("document.createElement", js, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AppCss_SupportsLightAndDarkAndWrapsLongText()
    {
        string css = await Fetch("/app.css");

        Assert.Contains("@media (prefers-color-scheme: dark)", css, StringComparison.Ordinal);
        Assert.Contains("color-scheme: light dark;", css, StringComparison.Ordinal);
        Assert.Contains("overflow-wrap: anywhere;", css, StringComparison.Ordinal);
        Assert.Contains("box-sizing: border-box;", css, StringComparison.Ordinal);
        Assert.Contains("--error:", css, StringComparison.Ordinal);
        Assert.DoesNotContain("@import", css, StringComparison.Ordinal);
        Assert.DoesNotContain("url(", css, StringComparison.Ordinal);
        Assert.DoesNotMatch(FixedWideWidth(), css);
    }

    [Fact]
    public async Task AppJs_ShowsFixedPlaceholderForEmptyTitles()
    {
        string js = await Fetch("/app.js");

        Assert.Contains("noTitle: \"(no title)\"", js, StringComparison.Ordinal);
        Assert.Contains("function titleText(value) {\n  return asText(value).trim() !== \"\" ? asText(value) : Texts.noTitle;\n}",
            js.Replace("\r\n", "\n"), StringComparison.Ordinal);
        Assert.Contains("element(\"strong\", \"title\", titleText(value))", js, StringComparison.Ordinal);
        Assert.Contains("title.classList.add(\"title-empty\")", js, StringComparison.Ordinal);
        Assert.Contains("head.appendChild(titleElement(entry.title));", js, StringComparison.Ordinal);
        Assert.Contains("item.appendChild(titleElement(entry && entry.title));", js, StringComparison.Ordinal);
        Assert.DoesNotContain("innerHTML", js, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EditJs_NamesUnnumberedButtonsWithPlaceholderAndKeepsRealTitleInInput()
    {
        string js = await Fetch("/edit.js");

        Assert.Contains("number !== null ? \"#\" + number : titleText(entry.title)", js, StringComparison.Ordinal);
        Assert.Contains("input.value = asText(entry.title);", js, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AppCss_TitleEmptyIsMutedWithThemeVariableAfterTitleRule()
    {
        string css = await Fetch("/app.css");

        int title = css.IndexOf("\n.title {", StringComparison.Ordinal);
        int empty = css.IndexOf(".title.title-empty {", StringComparison.Ordinal);
        Assert.True(title >= 0 && empty > title);
        string rule = css[empty..css.IndexOf('}', empty)];
        Assert.Contains("color: var(--muted);", rule, StringComparison.Ordinal);
        Assert.Contains("font-weight: 400;", rule, StringComparison.Ordinal);
        Assert.Contains("font-style: italic;", rule, StringComparison.Ordinal);
        Assert.DoesNotContain("#", rule, StringComparison.Ordinal);
    }

    /// <summary>The text from <paramref name="start"/> to the next line that closes a top-level block.</summary>
    private static string Body(string text, string start)
    {
        text = text.Replace("\r\n", "\n", StringComparison.Ordinal);
        int from = text.IndexOf(start, StringComparison.Ordinal);
        Assert.True(from >= 0, start);
        int end = text.IndexOf("\n}\n", from, StringComparison.Ordinal);
        return end < 0 ? text[from..] : text[from..end];
    }

    [Fact]
    public async Task EditJs_HasTheWorktreeFailedText()
    {
        string js = await Fetch("/edit.js");

        Assert.Contains("worktree_failed: \"Could not prepare the backlog branch. Nothing was changed.\"", js,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task AppJs_ShowsTheFixedPendingTextOnlyForAPendingProject()
    {
        string js = await Fetch("/app.js");

        Assert.Contains("pending: \"Edits wait for the running job and go into develop after it.\"", js,
            StringComparison.Ordinal);
        Assert.Contains(
            "  section.appendChild(head);\n"
            + "  // Edits go to the backlog branch while a job runs; a fixed text, never git output.\n"
            + "  if (project && project.pending === true) {\n"
            + "    section.appendChild(element(\"p\", \"backlog-pending\", Texts.pending));\n"
            + "  }\n",
            js,
            StringComparison.Ordinal);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(js, @"Texts\.pending\b"));
    }

    private static async Task<string> Fetch(string path)
    {
        using TempFactoryRoot root = new();
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateClient();
        return await client.GetStringAsync(path);
    }

    private static string AttributeStarts(string html)
    {
        return string.Concat(AttributeName().Matches(html).Select(match => match.Value.ToLowerInvariant()));
    }

    [GeneratedRegex("\\{ cache: \"no-store\" \\}")]
    private static partial Regex NoStore();

    [GeneratedRegex("authFetch\\(")]
    private static partial Regex AuthFetchCall();

    [GeneratedRegex("<script\\b", RegexOptions.IgnoreCase)]
    private static partial Regex ScriptTag();

    [GeneratedRegex("(src|href)=\"[^\"]*\"", RegexOptions.IgnoreCase)]
    private static partial Regex SourceAttribute();

    [GeneratedRegex("\\s[a-z-]+=", RegexOptions.IgnoreCase)]
    private static partial Regex AttributeName();

    [GeneratedRegex("(?<!max-)width:\\s*([4-9]\\d\\d|\\d{4,})px")]
    private static partial Regex FixedWideWidth();
}
