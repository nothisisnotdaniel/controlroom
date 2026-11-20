using System.Net;

namespace ControlRoom.Server.Tests;

/// <summary>Checks the Project picker on Backlogs in <c>app.js</c> and <c>app.css</c> as text (there is no JS test
/// runtime): the picker's markup, the stored choice and its fallback, the hidden state of each section, the change
/// listener, the story route hook and the live refresh rule. The layout is checked by hand.</summary>
public sealed class BacklogPickerWebAppTests
{
    [Fact]
    public async Task AppJs_PickerIsALabelledSelectOfReadableProjectsWithoutAnAllOption()
    {
        string app = await Fetch("/app.js");
        string picker = Between(app, "function renderBacklogPicker() {", "\n}\n");
        string fill = Between(app, "function fillBacklogPicker(select) {", "\n}\n");
        string names = Between(app, "function readableNames(projects) {", "\n}\n");

        Assert.Contains("  project: \"Project\",\n", app, StringComparison.Ordinal);
        Assert.Contains("const picker = element(\"div\", \"backlog-picker\");", picker, StringComparison.Ordinal);
        Assert.Contains("const label = element(\"label\", null, Texts.project);", picker, StringComparison.Ordinal);
        Assert.Contains("label.htmlFor = \"backlog-project\";", picker, StringComparison.Ordinal);
        Assert.Contains("select.id = \"backlog-project\";", picker, StringComparison.Ordinal);
        Assert.Contains("project && project.readable === true", names, StringComparison.Ordinal);
        Assert.Contains("readableProjects.map((name) => {", fill, StringComparison.Ordinal);
        Assert.Contains("option.value = name;", fill, StringComparison.Ordinal);
        Assert.Contains("option.textContent = name;", fill, StringComparison.Ordinal);
        Assert.DoesNotContain("All", fill, StringComparison.Ordinal);
        Assert.DoesNotContain("All", picker, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AppJs_PickerComesFirstOnlyWhenAProjectIsReadable()
    {
        string app = await Fetch("/app.js");
        string showProjects = Between(app, "function showProjects(projects) {", "\n}\n");
        string load = Between(app, "async function load() {", "\n}\n");

        Assert.Contains("readableProjects = readableNames(projects);", showProjects, StringComparison.Ordinal);
        Assert.Contains(
            "show(readableProjects.length > 0 ? [renderBacklogPicker(), ...sections] : sections);", showProjects,
            StringComparison.Ordinal);
        Assert.Contains("showProjects(projects);", load, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AppJs_RenderProjectHidesAllButTheChosenSection()
    {
        string app = await Fetch("/app.js");
        string render = Between(app, "function renderProject(project) {", "\nfunction show(");
        string hidden = Between(app, "function backlogSectionHidden(name) {", "\n}\n");

        int set = render.IndexOf("section.hidden = backlogSectionHidden(name);", StringComparison.Ordinal);
        int unreadable = render.IndexOf("Texts.unreadable", StringComparison.Ordinal);
        Assert.True(set >= 0 && unreadable > set);
        Assert.Contains("const chosen = chosenBacklogProject();", hidden, StringComparison.Ordinal);
        Assert.Contains("return chosen !== null && name !== chosen;", hidden, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AppJs_ChoiceIsStoredSafelyAndFallsBackWithoutStoring()
    {
        string app = await Fetch("/app.js");
        string read = Between(app, "function storedBacklogProject() {", "\n}\n");
        string write = Between(app, "function storeBacklogProject(name) {", "\n}\n");
        string chosen = Between(app, "function chosenBacklogProject() {", "\n}\n");

        Assert.Contains("const BacklogProjectKey = \"controlroom-backlog-project\";", app, StringComparison.Ordinal);
        Assert.Contains("try {\n    return localStorage.getItem(BacklogProjectKey);\n  } catch {\n    return null;",
            read, StringComparison.Ordinal);
        Assert.Contains("try {\n    localStorage.setItem(BacklogProjectKey, name);\n  } catch {", write,
            StringComparison.Ordinal);
        Assert.Equal(1, Count(read, "localStorage."));
        Assert.Equal(1, Count(write, "localStorage."));
        Assert.Equal(2, Count(app, "BacklogProjectKey)") + Count(app, "BacklogProjectKey, "));
        Assert.Contains("if (stored !== null && readableProjects.includes(stored)) {", chosen, StringComparison.Ordinal);
        Assert.Contains("return readableProjects.length > 0 ? readableProjects[0] : null;", chosen,
            StringComparison.Ordinal);
        Assert.DoesNotContain("setItem", chosen, StringComparison.Ordinal);
        Assert.DoesNotContain("storeBacklogProject", chosen, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AppJs_ChangeStoresTheNameAndTogglesSectionsWithoutAFetch()
    {
        string app = await Fetch("/app.js");
        string listener = Between(app, "select.addEventListener(\"change\", () => {", "});");
        string apply = Between(app, "function applyBacklogChoice() {", "\n}\n");

        Assert.Contains("storeBacklogProject(select.value);", listener, StringComparison.Ordinal);
        Assert.Contains("applyBacklogChoice();", listener, StringComparison.Ordinal);
        Assert.Contains("for (const section of document.querySelectorAll(\"#backlogs > section.project\")) {", apply,
            StringComparison.Ordinal);
        Assert.Contains("section.hidden = backlogSectionHidden(section.dataset.project);", apply,
            StringComparison.Ordinal);
        Assert.DoesNotContain("fetchBacklogs(", listener + apply, StringComparison.Ordinal);
        Assert.DoesNotContain("renderProject(", listener + apply, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AppJs_StoryRouteChoosesItsProjectWhenReadable()
    {
        string app = await Fetch("/app.js");
        string route = Between(app, "function route() {", "\nfunction registerServiceWorker(");
        string choose = Between(app, "function chooseBacklogProject(name) {", "\n}\n");

        Assert.Contains(
            "if (next === \"story\") {\n    window.scrollTo(0, 0);\n    const target = parseStoryHash(location.hash);\n"
            + "    if (target !== null) {\n      chooseBacklogProject(target.project);\n    }\n    loadStory(target);",
            route, StringComparison.Ordinal);
        int check = choose.IndexOf("if (!readableProjects.includes(name)) {\n    return;", StringComparison.Ordinal);
        int store = choose.IndexOf("storeBacklogProject(name);", StringComparison.Ordinal);
        Assert.True(check >= 0 && store > check);
        Assert.Contains("applyBacklogChoice();", choose, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AppJs_LiveRefreshKeepsTheChoiceAndRebuildsOptionsOnlyWhenNamesChangeAndNotWhileFocused()
    {
        string app = await Fetch("/app.js");
        string refresh = Between(app, "async function refreshBacklogs(names) {", "\nfunction restoreListScroll(");
        string update = Between(app, "function updateBacklogPicker(main) {", "\n}\n");

        Assert.Contains("readableProjects = readableNames(projects);", refresh, StringComparison.Ordinal);
        Assert.Contains("updateBacklogPicker(main);\n    applyBacklogChoice();", refresh, StringComparison.Ordinal);
        Assert.Contains("main.querySelector(\":scope > div.backlog-picker\").after(next);", refresh,
            StringComparison.Ordinal);
        int focus = update.IndexOf("if (document.activeElement === select) {\n    return;", StringComparison.Ordinal);
        int compare = update.IndexOf("if (JSON.stringify(names) !== JSON.stringify(readableProjects)) {",
            StringComparison.Ordinal);
        int fill = update.IndexOf("fillBacklogPicker(select);", StringComparison.Ordinal);
        Assert.True(focus >= 0 && compare > focus && fill > compare);
        Assert.Contains("document.activeElement !== select", Between(app, "function applyBacklogChoice() {", "\n}\n"),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task AppJs_StoryLinksStayBuiltWithStoryHash()
    {
        string app = await Fetch("/app.js");

        Assert.Contains("function storyHash(project, number) {\n  return StoryRoute + encodeURIComponent(project) + \"/\" + number;",
            app, StringComparison.Ordinal);
        Assert.Contains("link.href = storyHash(projectName, number);", app, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AppJsAndCss_PickerIsTextOnlyAnd16pxWithThemeColours()
    {
        using TempFactoryRoot root = new();
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateClient();
        using HttpResponseMessage response = await client.GetAsync("/app.js");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        string app = await response.Content.ReadAsStringAsync();
        string css = await client.GetStringAsync("/app.css");
        string rule = Between(css, ".backlog-picker select {", "}");

        Assert.DoesNotContain("innerHTML", app, StringComparison.Ordinal);
        Assert.Contains("font-size: 16px;", rule, StringComparison.Ordinal);
        Assert.Contains("background: var(--bg);", rule, StringComparison.Ordinal);
        Assert.Contains("color: var(--fg);", rule, StringComparison.Ordinal);
        Assert.Contains("border: 1px solid var(--line);", rule, StringComparison.Ordinal);
        Assert.Contains(".backlog-picker {", css, StringComparison.Ordinal);
    }

    private static int Count(string text, string part)
    {
        int count = 0;
        for (int at = text.IndexOf(part, StringComparison.Ordinal); at >= 0;
             at = text.IndexOf(part, at + part.Length, StringComparison.Ordinal))
        {
            count += 1;
        }
        return count;
    }

    private static string Between(string text, string start, string end)
    {
        int from = text.IndexOf(start, StringComparison.Ordinal);
        Assert.True(from >= 0, start);
        int to = text.IndexOf(end, from + start.Length, StringComparison.Ordinal);
        Assert.True(to > from, end);
        return text[from..to];
    }

    private static async Task<string> Fetch(string path)
    {
        using TempFactoryRoot root = new();
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateClient();
        return await client.GetStringAsync(path);
    }
}
