using System.Text.RegularExpressions;

namespace ControlRoom.Server.Tests;

/// <summary>Checks <c>search.js</c> and its hooks in <c>index.html</c>, <c>app.js</c>, <c>live.js</c>,
/// <c>app.css</c> and <c>sw.js</c> as text (there is no JS test runtime). Typing, Back restoring the view and the
/// live re-run keeping focus are checked by hand.</summary>
public sealed partial class SearchWebAppTests
{
    [Fact]
    public async Task IndexHtml_HasTheSearchLinkAfterTasksAndTheSearchView()
    {
        string html = await Fetch("/index.html");

        int tasks = html.IndexOf("<a href=\"#tasks\" data-view=\"tasks\">", StringComparison.Ordinal);
        int search = html.IndexOf("<a href=\"#search\" data-view=\"search\">Search</a>", StringComparison.Ordinal);
        int navEnd = html.IndexOf("</nav>", StringComparison.Ordinal);
        Assert.True(tasks >= 0 && search > tasks && search < navEnd);
        Assert.Equal(6, NavLink().Matches(html[..navEnd]).Count);
        Assert.Contains("<main id=\"search\" hidden></main>", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task IndexHtml_LoadsSearchJsAfterTasksJsAndBeforeLiveJs_NineScripts()
    {
        string html = await Fetch("/index.html");
        string[] scripts =
            ["/token.js", "/board.js", "/push.js", "/edit.js", "/inbox.js", "/tasks.js", "/search.js", "/capture.js", "/live.js", "/app.js"];

        int[] positions = scripts
            .Select(script => html.IndexOf("<script src=\"" + script + "\" defer></script>", StringComparison.Ordinal))
            .ToArray();
        Assert.All(positions, position => Assert.True(position >= 0));
        Assert.Equal(positions.OrderBy(position => position), positions);
        Assert.Equal(10, ScriptTag().Matches(html).Count);
    }

    [Fact]
    public async Task AppJs_RoutesHashSearchToTheSearchViewTitledSearch_AndKeepsItsScroll()
    {
        string app = await Fetch("/app.js");
        string search = await Fetch("/search.js");

        Assert.Contains(
            "const Views = [\"backlogs\", \"board\", \"inbox\", \"tasks\", \"search\", \"capture\", \"job\", \"story\"];", app,
            StringComparison.Ordinal);
        Assert.Contains(
            "const ViewTitles = { backlogs: \"Backlogs\", board: \"Jobs\", inbox: \"Inbox\", tasks: \"Tasks\", search: \"Search\",\n  capture: \"Capture\" };",
            app, StringComparison.Ordinal);
        Assert.Contains("if (hash === SearchRoute) {\n    return \"search\";\n  }", app, StringComparison.Ordinal);
        Assert.Contains("if (next === \"search\") {\n    showSearch();\n    return;\n  }", app, StringComparison.Ordinal);
        Assert.Contains("if (currentView === \"search\" && next !== \"search\") {\n    rememberSearchScroll();", app,
            StringComparison.Ordinal);
        Assert.Contains("const SearchRoute = \"#search\";", search, StringComparison.Ordinal);
        Assert.Contains("searchScroll = window.scrollY;", search, StringComparison.Ordinal);
        Assert.Contains("window.scrollTo(0, searchScroll);", search, StringComparison.Ordinal);
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
        Assert.Contains("await authFetch(searchUrl(text, mark, project), { cache: \"no-store\" });", js,
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
        Assert.Contains(
            "if (text === \"\" && mark === \"\" && project === \"\") {\n    showSearchStatus(\"\");\n    showSearchResults([element(\"p\", \"empty\", SearchTexts.hint)]);\n    return true;\n  }",
            js, StringComparison.Ordinal);
        int empty = js.IndexOf("if (text === \"\" && mark === \"\" && project === \"\") {", StringComparison.Ordinal);
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
        Assert.Single(ReplaceView().Matches(js));
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

        Assert.Contains("const CacheVersion = \"v12\";", js, StringComparison.Ordinal);
        Assert.Contains("  \"/tasks.js\",\n  \"/search.js\",\n  \"/capture.js\",\n  \"/live.js\",\n", js, StringComparison.Ordinal);
        Assert.DoesNotContain("/api/search", js, StringComparison.Ordinal);
        Assert.Contains("if (url.pathname.startsWith(\"/api/\") || url.pathname === \"/health\") {\n    return false;", js,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task AppCss_StylesTheSearchView_With16PxFieldsAndSixNavLinksInTwoRows()
    {
        string css = await Fetch("/app.css");

        Assert.Contains(".search-field,\n.search-select {", css, StringComparison.Ordinal);
        Assert.Contains("  font-size: 16px;\n}", css, StringComparison.Ordinal);
        Assert.Contains(".search-item {\n  overflow-wrap: anywhere;\n}", css, StringComparison.Ordinal);
        Assert.Contains("/* Six nav links fit in two rows of three at iPhone width (375 px). */", css, StringComparison.Ordinal);
        Assert.Contains("    flex-wrap: wrap;\n", css, StringComparison.Ordinal);
        Assert.Contains("    flex: 1 1 calc((100% - 8px) / 3);\n", css, StringComparison.Ordinal);
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
}
