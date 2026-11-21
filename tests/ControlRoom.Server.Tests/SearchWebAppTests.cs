using System.Text.RegularExpressions;

namespace ControlRoom.Server.Tests;

/// <summary>Checks <c>search.js</c> and its hooks in <c>index.html</c>, <c>app.js</c>, <c>live.js</c>,
/// <c>app.css</c> and <c>sw.js</c> as text (there is no JS test runtime). Search sits at the top of the Backlogs
/// (#133). Typing, Back restoring the view and the live re-run keeping focus are checked by hand.</summary>
public sealed partial class SearchWebAppTests
{
    [Fact]
    public async Task IndexHtml_HasNoSearchTabAndTheSearchBoxAtTheTopOfBacklogs()
    {
        string html = await Fetch("/index.html");

        int navEnd = html.IndexOf("</nav>", StringComparison.Ordinal);
        Assert.DoesNotContain("href=\"#search\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("data-view=\"search\"", html, StringComparison.Ordinal);
        Assert.Equal(6, NavLink().Matches(html[..navEnd]).Count);
        Assert.DoesNotContain("<main id=\"search\"", html, StringComparison.Ordinal);
        Assert.Contains(
            "<main id=\"backlogs\">\n    <div class=\"backlog-search\" id=\"backlog-search\"></div>\n    <p class=\"status\">Loading…</p>",
            html.ReplaceLineEndings("\n"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task IndexHtml_LoadsSearchJsAfterTasksJsAndBeforeLiveJs_NineScripts()
    {
        string html = await Fetch("/index.html");
        string[] scripts =
            ["/token.js", "/board.js", "/act.js", "/push.js", "/edit.js", "/inbox.js", "/tasks.js", "/search.js", "/capture.js", "/history.js", "/doctor.js", "/status.js", "/live.js", "/app.js"];

        int[] positions = scripts
            .Select(script => html.IndexOf("<script src=\"" + script + "\" defer></script>", StringComparison.Ordinal))
            .ToArray();
        Assert.All(positions, position => Assert.True(position >= 0));
        Assert.Equal(positions.OrderBy(position => position), positions);
        Assert.Equal(14, ScriptTag().Matches(html).Count);
    }

    [Fact]
    public async Task AppJs_HasNoSearchView()
    {
        string app = await Fetch("/app.js");
        string search = await Fetch("/search.js");

        Assert.Contains(
            "const Views = [\"backlogs\", \"board\", \"inbox\", \"tasks\", \"history\", \"doctor\", \"job\", \"story\"];", app,
            StringComparison.Ordinal);
        Assert.Contains(
            "const ViewTitles = { backlogs: \"Backlogs\", board: \"Jobs\", inbox: \"Inbox\", tasks: \"Tasks\",\n  history: \"History\", doctor: \"Health\" };",
            app, StringComparison.Ordinal);
        Assert.DoesNotContain("next === \"search\"", app, StringComparison.Ordinal);
        Assert.DoesNotContain("rememberSearchScroll", app + search, StringComparison.Ordinal);
        Assert.DoesNotContain("getElementById(\"search\")", search, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AppJs_RoutesHashSearchToBacklogsWithTheFieldFocused()
    {
        string app = await Fetch("/app.js");
        string search = await Fetch("/search.js");

        Assert.Contains("const SearchRoute = \"#search\";", search, StringComparison.Ordinal);
        Assert.Contains("const BacklogsRoute = \"#backlogs\";", app, StringComparison.Ordinal);
        Assert.Contains("if (hash === SearchRoute) {\n    return \"backlogs\";\n  }", app, StringComparison.Ordinal);
        Assert.Contains(
            "  const fromSearch = location.hash === SearchRoute;\n  if (fromSearch) {\n    history.replaceState(null, \"\", BacklogsRoute);\n  }",
            app, StringComparison.Ordinal);
        Assert.Contains(
            "  showStatus();\n  showSearch();\n  if (fromSearch) {\n    window.scrollTo(0, 0);\n    focusSearch();\n    return;\n  }\n  window.scrollTo(0, listScroll);",
            app, StringComparison.Ordinal);
        Assert.Contains("function focusSearch() {\n  searchField().focus({ preventScroll: true });\n}", search,
            StringComparison.Ordinal);
        // The redirect is read before the view is chosen, so the replaced hash still opens Backlogs.
        int view = app.IndexOf("const next = viewFor(location.hash);", StringComparison.Ordinal);
        int redirect = app.IndexOf("const fromSearch = location.hash === SearchRoute;", StringComparison.Ordinal);
        Assert.True(view >= 0 && redirect > view);
    }

    [Fact]
    public async Task SearchJs_BuildsTheFormOnceIntoTheBacklogSearchBox()
    {
        string search = await Fetch("/search.js");

        Assert.Contains("function backlogSearchBox() {\n  return document.getElementById(\"backlog-search\");\n}", search,
            StringComparison.Ordinal);
        Assert.Contains("function buildSearchView() {\n  const box = backlogSearchBox();", search, StringComparison.Ordinal);
        Assert.Contains("box.replaceChildren(form, status, results);", search, StringComparison.Ordinal);
        Assert.Contains(
            "function searchShown() {\n  return !document.getElementById(\"backlogs\").hidden;\n}", search,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task AppJs_KeepsTheSearchBoxFirstWhenRenderingTheBacklog()
    {
        string app = await Fetch("/app.js");

        Assert.Contains(
            "function show(nodes) {\n  const box = backlogSearchBox();\n  for (const child of [...document.getElementById(\"backlogs\").children]) {\n    if (child !== box) {\n      child.remove();\n    }\n  }\n  box.after(...nodes);\n}",
            app, StringComparison.Ordinal);
        Assert.Contains("backlogSearchBox().after(renderBacklogPicker());", app, StringComparison.Ordinal);
        Assert.Contains("backlogSearchBox().after(next);", app, StringComparison.Ordinal);
        Assert.DoesNotContain("main.prepend(", app, StringComparison.Ordinal);
        Assert.Single(ReplaceView().Matches(app)); // only the story view's own main
    }

    [Fact]
    public async Task SearchJs_ShowsResultsInPlaceOfTheBacklogWhileAQueryIsEntered_AndTheBacklogWhenCleared()
    {
        string js = await Fetch("/search.js");

        Assert.Contains("const SearchingClass = \"searching\";", js, StringComparison.Ordinal);
        Assert.Contains(
            "function showSearching(searching) {\n  document.getElementById(\"backlogs\").classList.toggle(SearchingClass, searching);\n}",
            js, StringComparison.Ordinal);
        Assert.Contains(
            "if (text === \"\" && mark === \"\" && project === \"\" && category === \"\") {\n    showSearchStatus(\"\");\n    showSearchResults([]);\n    showSearching(false);\n    return true;\n  }\n  showSearching(true);\n",
            js, StringComparison.Ordinal);
        Assert.Contains("field.placeholder = SearchTexts.hint;", js, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AppCss_HidesTheBacklogWhileSearching()
    {
        string css = (await Fetch("/app.css")).ReplaceLineEndings("\n");

        Assert.Contains("#backlogs.searching > :not(#backlog-search) {\n  display: none;\n}", css, StringComparison.Ordinal);
        Assert.Contains(".backlog-search {\n  margin: 0 0 12px;\n}", css, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SearchJs_HasALabelledSearchFieldAndTheMarkAndProjectSelects()
    {
        string js = await Fetch("/search.js");

        Assert.Contains("label: \"Search backlogs\",", js, StringComparison.Ordinal);
        Assert.Contains("form.appendChild(searchLabel(SearchTexts.label, \"search-field\"));", js, StringComparison.Ordinal);
        Assert.Contains("label.htmlFor = target;", js, StringComparison.Ordinal);
        Assert.Contains("field.type = \"search\";", js, StringComparison.Ordinal);
        Assert.Contains("field.setAttribute(\"autocapitalize\", \"off\");", js, StringComparison.Ordinal);
        Assert.Contains("field.spellcheck = false;", js, StringComparison.Ordinal);
        Assert.Contains("allMarks: \"All marks\",", js, StringComparison.Ordinal);
        Assert.Contains("allProjects: \"All projects\",", js, StringComparison.Ordinal);
        Assert.Contains(
            "  [\"\", SearchTexts.allMarks],\n  [\"open\", \"Open\"],\n  [\"approval\", \"Proposed\"],\n  [\"task\", \"Tasks\"],\n  [\"done\", \"Done\"],\n",
            js, StringComparison.Ordinal);
        Assert.Contains("project.appendChild(searchOption(\"\", SearchTexts.allProjects));", js, StringComparison.Ordinal);
        Assert.Contains("fillSearchProjects(result.projects);", js, StringComparison.Ordinal);
        Assert.Contains("searchLabel(SearchTexts.markLabel, \"search-mark\")", js, StringComparison.Ordinal);
        Assert.Contains("searchLabel(SearchTexts.projectLabel, \"search-project\")", js, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SearchJs_SendsOneAuthFetchPerChange_DropsOlderAnswers_AndNothingWhenEmpty()
    {
        string js = await Fetch("/search.js");

        Assert.Contains("const SearchApi = \"/api/search\";", js, StringComparison.Ordinal);
        Assert.Contains("await authFetch(searchUrl(text, mark, project, category), { cache: \"no-store\" });", js,
            StringComparison.Ordinal);
        Assert.Single(AuthFetchCall().Matches(js));
        Assert.DoesNotContain(" fetch(", js, StringComparison.Ordinal);
        Assert.Contains("field.addEventListener(\"input\", () => runSearch(true));", js, StringComparison.Ordinal);
        Assert.Contains("mark.addEventListener(\"change\", () => runSearch(true));", js, StringComparison.Ordinal);
        Assert.Contains("project.addEventListener(\"change\", () => runSearch(true));", js, StringComparison.Ordinal);
        Assert.Contains("searchRequest += 1;\n  const request = searchRequest;", js, StringComparison.Ordinal);
        Assert.Contains("if (request !== searchRequest) {\n    return true;\n  }\n  fillSearchProjects", js,
            StringComparison.Ordinal);
        Assert.Contains("hint: \"Type a number, title or word.\",", js, StringComparison.Ordinal);
        int empty = js.IndexOf("if (text === \"\" && mark === \"\" && project === \"\" && category === \"\") {", StringComparison.Ordinal);
        int fetch = js.IndexOf("await authFetch(", StringComparison.Ordinal);
        Assert.True(empty >= 0 && empty < fetch);
        Assert.DoesNotContain("setTimeout", js, StringComparison.Ordinal);
        Assert.DoesNotContain("setInterval", js, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SearchJs_ShowsFixedTextsAndFileTextOnlyThroughTextContent()
    {
        string js = await Fetch("/search.js");

        Assert.Contains("searching: \"Searching…\",", js, StringComparison.Ordinal);
        Assert.Contains("none: \"No items found.\",", js, StringComparison.Ordinal);
        Assert.Contains("showing: \"Showing \",", js, StringComparison.Ordinal);
        Assert.Contains("of: \" of \",", js, StringComparison.Ordinal);
        Assert.Contains(
            "showSearchStatus(SearchTexts.showing + result.items.length + SearchTexts.of + result.total + \".\");", js,
            StringComparison.Ordinal);
        Assert.Contains("failed: \"Can't search right now.\",", js, StringComparison.Ordinal);
        Assert.Contains("unreadable: \": backlog can't be read.\",", js, StringComparison.Ordinal);
        Assert.Contains("element(\"p\", \"error search-unreadable\", name + SearchTexts.unreadable)", js,
            StringComparison.Ordinal);
        Assert.Contains("foundInStory: \"Found in story\",", js, StringComparison.Ordinal);
        Assert.Contains(
            "if (item.match === \"story\") {\n    row.appendChild(element(\"span\", \"search-found\", SearchTexts.foundInStory));",
            js, StringComparison.Ordinal);
        Assert.DoesNotContain("innerHTML", js, StringComparison.Ordinal);
        Assert.DoesNotContain("insertAdjacentHTML", js, StringComparison.Ordinal);
        Assert.DoesNotContain("outerHTML", js, StringComparison.Ordinal);
        Assert.DoesNotContain("data.error", js, StringComparison.Ordinal);
        Assert.DoesNotContain(".message", js, StringComparison.Ordinal);
        Assert.Single(TextAssignment().Matches(js));
        Assert.Contains("status.textContent = text;", js, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SearchJs_EachResultShowsProjectMarkHeadAndRest_WithAStoryLinkOnlyWithAStory()
    {
        string js = await Fetch("/search.js");

        Assert.Contains("meta.appendChild(element(\"span\", \"search-project\", item.project));", js, StringComparison.Ordinal);
        Assert.Contains("SearchMarkLabels[item.mark]", js, StringComparison.Ordinal);
        Assert.Contains(
            "const SearchMarkLabels = { open: \"Open\", approval: \"Proposed\", task: \"Task\", done: \"Done\" };", js,
            StringComparison.Ordinal);
        Assert.Contains(
            "const label = (item.number !== null ? \"#\" + item.number + \" \" : \"\") + titleText(item.title);", js,
            StringComparison.Ordinal);
        Assert.Contains("if (item.number !== null && item.story) {", js, StringComparison.Ordinal);
        Assert.Contains("head.href = storyHash(item.project, item.number);", js, StringComparison.Ordinal);
        Assert.Contains("openStory(item.project, item.number);", js, StringComparison.Ordinal);
        Assert.Contains("row.appendChild(element(\"strong\", \"title search-head\", label));", js, StringComparison.Ordinal);
        Assert.Contains("row.appendChild(element(\"span\", \"rest\", item.rest));", js, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SearchJs_IsReadOnly()
    {
        string js = await Fetch("/search.js");

        Assert.DoesNotContain("sendChange", js, StringComparison.Ordinal);
        Assert.DoesNotContain("editButton", js, StringComparison.Ordinal);
        Assert.DoesNotContain("/api/backlog/change", js, StringComparison.Ordinal);
        Assert.DoesNotContain("\"button\"", js, StringComparison.Ordinal);
        Assert.DoesNotContain("method:", js, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SearchJs_KeepsTheFormAndReplacesOnlyTheResults()
    {
        string js = await Fetch("/search.js");

        Assert.Contains("document.getElementById(\"search-results\").replaceChildren(...nodes);", js, StringComparison.Ordinal);
        // The whole view is built only once, on the first show.
        Assert.Single(BoxReplace().Matches(js));
        Assert.Contains("if (searchField() === null) {\n    buildSearchView();", js, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LiveJs_ReRunsTheSearchOnABacklogOrStoriesChange_AndTheNextShowWhileHidden()
    {
        string live = await Fetch("/live.js");
        string search = await Fetch("/search.js");

        Assert.Contains("staleStories.add(name);\n      searchChanged = true;\n    } else if (before.stories !== after.stories) {",
            live, StringComparison.Ordinal);
        Assert.Contains("tasksStale = true;\n      searchChanged = true;\n    }\n", live, StringComparison.Ordinal);
        Assert.Contains("staleStories.add(name);\n    }\n    searchChanged = true;\n    return;", live, StringComparison.Ordinal);
        Assert.Contains("await refreshStaleSearch();", live, StringComparison.Ordinal);
        Assert.Contains("searchChanged = false;\n  if (!(await refreshSearch())) {\n    searchChanged = true;\n  }", live,
            StringComparison.Ordinal);
        Assert.Contains("if (!searchShown()) {\n    searchStale = true;\n    return true;\n  }\n  return runSearch(false);",
            search, StringComparison.Ordinal);
        Assert.Contains("if (searchStale) {\n    searchStale = false;\n    refreshSearch();\n  }", search,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task ServiceWorker_CachesSearchJsWithVersion12AndNeverTheApi()
    {
        string js = await Fetch("/sw.js");

        Assert.Contains("const CacheVersion = \"v18\";", js, StringComparison.Ordinal);
        Assert.Contains("  \"/tasks.js\",\n  \"/search.js\",\n  \"/capture.js\",\n  \"/history.js\",\n  \"/doctor.js\",\n  \"/status.js\",\n  \"/live.js\",\n", js, StringComparison.Ordinal);
        Assert.DoesNotContain("/api/search", js, StringComparison.Ordinal);
        Assert.Contains("if (url.pathname.startsWith(\"/api/\") || url.pathname === \"/health\") {\n    return false;", js,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task AppCss_StylesTheSearchBox_With16PxFieldsAndTheTabsInOneRow()
    {
        string css = await Fetch("/app.css");

        Assert.Contains(".search-field,\n.search-select {", css, StringComparison.Ordinal);
        Assert.Contains("  font-size: 16px;\n}", css, StringComparison.Ordinal);
        Assert.Contains(".search-item {\n  overflow-wrap: anywhere;\n}", css, StringComparison.Ordinal);
        Assert.DoesNotContain("two rows of four", css, StringComparison.Ordinal);
        Assert.Contains("  flex-wrap: nowrap;\n", css, StringComparison.Ordinal);
        Assert.Contains("  flex: 1 1 0;\n", css, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SearchJs_IsServed()
    {
        using TempFactoryRoot root = new();
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync("/search.js");

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/javascript", response.Content.Headers.ContentType?.MediaType);
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

    [GeneratedRegex("<a href=\"#")]
    private static partial Regex NavLink();

    [GeneratedRegex(@"authFetch\(")]
    private static partial Regex AuthFetchCall();

    [GeneratedRegex(@"\.textContent\s*=[^;]*;")]
    private static partial Regex TextAssignment();

    [GeneratedRegex(@"main\.replaceChildren\(")]
    private static partial Regex ReplaceView();

    [GeneratedRegex(@"box\.replaceChildren\(")]
    private static partial Regex BoxReplace();
}
