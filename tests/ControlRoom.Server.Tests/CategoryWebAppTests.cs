using System.Text.RegularExpressions;

namespace ControlRoom.Server.Tests;

/// <summary>Checks the category chip, its colours, the story's Category select and the search's Category filter in
/// <c>app.js</c>, <c>edit.js</c>, <c>search.js</c> and <c>app.css</c> as text (there is no JS test runtime). The look,
/// contrast and VoiceOver are checked by hand.</summary>
public sealed class CategoryWebAppTests
{
    private static readonly string[] Names = ["feature", "bug", "security", "ux", "ui", "tidy-up", "test", "ci", "docs"];

    [Fact]
    public async Task AppJs_HasTheFixedListOfNineInJobPyOrder()
    {
        string app = await Fetch("/app.js");

        Assert.Contains(
            "const Categories = [\"feature\", \"bug\", \"security\", \"ux\", \"ui\", \"tidy-up\", \"test\", \"ci\", \"docs\"];",
            app, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AppJs_ChipIsTextOnlyAndItsClassComesOnlyFromTheList()
    {
        string app = await Fetch("/app.js");
        string chip = Between(app, "function categoryChip(value) {", "\n}\n");

        int check = chip.IndexOf("if (typeof value !== \"string\" || !Categories.includes(value)) {", StringComparison.Ordinal);
        int className = chip.IndexOf("chip.className = \"category-chip category-\" + value;", StringComparison.Ordinal);
        Assert.True(check >= 0 && check < className);
        Assert.Contains("return null;", chip, StringComparison.Ordinal);
        Assert.Contains("chip.textContent = value;", chip, StringComparison.Ordinal);
        Assert.DoesNotContain("innerHTML", chip, StringComparison.Ordinal);
        Assert.DoesNotContain("insertAdjacentHTML", app, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AppJs_ChipComesAfterTheTitleAndBeforeTheWaitingBadge()
    {
        string app = await Fetch("/app.js");
        string line = Between(app, "function renderNumberedLine(projectName, version, entry, number, position) {", "\n}\n");

        int title = line.IndexOf("head.appendChild(titleElement(entry.title));", StringComparison.Ordinal);
        int chip = line.IndexOf("const chip = categoryChip(entry.category);", StringComparison.Ordinal);
        int append = line.IndexOf("head.appendChild(chip);", StringComparison.Ordinal);
        int waiting = line.IndexOf("Texts.waiting", StringComparison.Ordinal);
        int headDone = line.IndexOf("item.appendChild(head);", StringComparison.Ordinal);
        Assert.True(title >= 0 && title < chip && chip < append && append < waiting && waiting < headDone);
        Assert.Contains("if (chip !== null) {", line, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AppJs_StoryShowsTheChipRightUnderTheTitle()
    {
        string app = await Fetch("/app.js");
        string article = Between(app, "function renderStoryArticle(data) {", "\n}\n");

        int title = article.IndexOf("article.appendChild(title);", StringComparison.Ordinal);
        int chip = article.IndexOf("const chip = categoryChip(data && data.category);", StringComparison.Ordinal);
        int append = article.IndexOf("article.appendChild(chip);", StringComparison.Ordinal);
        int blocks = article.IndexOf("for (const block of asList(data && data.blocks)) {", StringComparison.Ordinal);
        Assert.True(title >= 0 && title < chip && chip < append && append < blocks);
    }

    [Fact]
    public async Task AppCss_EveryCategoryHasItsOwnColourPairInLightAndDark()
    {
        string css = await Fetch("/app.css");
        string light = Between(css, ":root {", "\n}\n");
        string dark = Between(Between(css, "@media (prefers-color-scheme: dark) {", "\n}\n"), ":root {", "\n  }");

        foreach (string scheme in new[] { light, dark })
        {
            List<string> backgrounds = [];
            foreach (string name in Names)
            {
                backgrounds.Add(Colour(scheme, "--category-" + name + "-bg"));
                Colour(scheme, "--category-" + name + "-fg");
            }

            Assert.Equal(Names.Length, backgrounds.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        }

        foreach (string name in Names)
        {
            string rule = Between(css, "\n.category-" + name + " {", "}");
            Assert.Contains("background: var(--category-" + name + "-bg);", rule, StringComparison.Ordinal);
            Assert.Contains("color: var(--category-" + name + "-fg);", rule, StringComparison.Ordinal);
        }

        Assert.Contains(".category-chip {", css, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EditJs_CategorySelectHasAVisibleLabelTheNineAndADisabledNoCategory()
    {
        string edit = await Fetch("/edit.js");
        string texts = Between(edit, "const EditTexts = {", "};");
        string control = Between(edit, "function storyCategoryControl(target, data) {", "\n}\n");

        Assert.Contains("  category: \"Category\",\n", texts, StringComparison.Ordinal);
        Assert.Contains("  noCategory: \"No category\",\n", texts, StringComparison.Ordinal);
        Assert.Contains("const label = element(\"label\", \"story-category\");", control, StringComparison.Ordinal);
        Assert.Contains("label.appendChild(element(\"span\", \"story-category-label\", EditTexts.category));", control,
            StringComparison.Ordinal);
        Assert.Contains("const select = element(\"select\", \"story-category-select\");", control, StringComparison.Ordinal);
        Assert.Contains("label.appendChild(select);", control, StringComparison.Ordinal);
        int none = control.IndexOf("if (current === \"\") {", StringComparison.Ordinal);
        int noneText = control.IndexOf("element(\"option\", null, EditTexts.noCategory)", StringComparison.Ordinal);
        int disabled = control.IndexOf("none.disabled = true;", StringComparison.Ordinal);
        int selected = control.IndexOf("none.selected = true;", StringComparison.Ordinal);
        int names = control.IndexOf("for (const name of Categories) {", StringComparison.Ordinal);
        Assert.True(none >= 0 && none < noneText && noneText < disabled && disabled < selected && selected < names);
        Assert.Contains("option.selected = name === current;", control, StringComparison.Ordinal);
        Assert.Contains("Categories.includes(data.category)", control, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EditJs_ChoosingANameSendsCategoryWithTheLineLookedUpAgainThenReloads()
    {
        string edit = await Fetch("/edit.js");
        string control = Between(edit, "function storyCategoryControl(target, data) {", "\n}\n");
        string change = Between(control, "select.addEventListener(\"change\", async () => {", "\n  });\n");

        Assert.Contains("const storyVersion = asText(data && data.storyVersion);", control, StringComparison.Ordinal);
        Assert.Contains("select.disabled = changeBusy;", control, StringComparison.Ordinal);
        int busy = change.IndexOf("if (changeBusy || storyFormOpen() || !Categories.includes(chosen)", StringComparison.Ordinal);
        int find = change.IndexOf("found = await findStoryLine(target.project, number);", StringComparison.Ordinal);
        int send = change.IndexOf("const ok = await sendChange({", StringComparison.Ordinal);
        int reload = change.IndexOf("loadStory(target);", send, StringComparison.Ordinal);
        Assert.True(busy >= 0 && busy < find && find < send && send < reload);
        string body = Between(change, "const ok = await sendChange({", "});");
        foreach (string field in new[]
                 {
                     "project: target.project,", "version: found.version,", "action: \"category\",",
                     "line: found.line,", "storyVersion: storyVersion,", "category: chosen,",
                 })
        {
            Assert.Contains(field, body, StringComparison.Ordinal);
        }

        Assert.Contains("ChangeErrors.unreachable", change, StringComparison.Ordinal);
        Assert.Contains("ChangeErrors.changed", change, StringComparison.Ordinal);
        Assert.DoesNotContain("innerHTML", edit, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EditJs_SelectOnlyWhileNotDone_AndHiddenWithTheOtherStoryButtons()
    {
        string edit = await Fetch("/edit.js");
        string decision = Between(edit, "async function showStoryDecision(target, data) {", "\n}\n");
        string show = Between(edit, "function showStoryCategory(bar, target, data, found) {", "\n}\n");
        string buttons = Between(edit, "function showStoryButtons(visible) {", "\n}\n");
        string control = Between(edit, "function storyCategoryControl(target, data) {", "\n}\n");

        int find = decision.IndexOf("found = await findStoryLine(target.project, number);", StringComparison.Ordinal);
        int category = decision.IndexOf("showStoryCategory(bar, target, data, found);", StringComparison.Ordinal);
        int approval = decision.IndexOf("found.group !== \"approval\"", StringComparison.Ordinal);
        Assert.True(find >= 0 && find < category && category < approval);
        Assert.Contains("if (found === null) {", show, StringComparison.Ordinal);
        Assert.Contains("old.remove();", show, StringComparison.Ordinal);
        Assert.Contains("#story .story-category", buttons, StringComparison.Ordinal);
        Assert.Contains("#story .edit-story-button", buttons, StringComparison.Ordinal);
        Assert.Contains("#story .add-note-button", buttons, StringComparison.Ordinal);
        Assert.Contains("label.hidden = storyFormOpen();", control, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SearchJs_CategorySelectBesideMarkAndProject_BuiltOnceWithTheForm()
    {
        string search = await Fetch("/search.js");
        string texts = Between(search, "const SearchTexts = {", "};");
        string build = Between(search, "function buildSearchView() {", "\n}\n");

        Assert.Contains("  categoryLabel: \"Category\",\n", texts, StringComparison.Ordinal);
        Assert.Contains("  allCategories: \"All categories\",\n", texts, StringComparison.Ordinal);
        int all = build.IndexOf("category.appendChild(searchOption(\"\", SearchTexts.allCategories));", StringComparison.Ordinal);
        int names = build.IndexOf("for (const name of Categories) {", all, StringComparison.Ordinal);
        int option = build.IndexOf("category.appendChild(searchOption(name, name));", names, StringComparison.Ordinal);
        Assert.True(all >= 0 && all < names && names < option);
        Assert.Contains("category.id = \"search-category\";", build, StringComparison.Ordinal);
        Assert.Contains("category.addEventListener(\"change\", () => runSearch(true));", build, StringComparison.Ordinal);
        Assert.Contains("searchLabel(SearchTexts.categoryLabel, \"search-category\")", build, StringComparison.Ordinal);
        Assert.Contains("filters.append(markBox, projectBox, categoryBox);", build, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SearchJs_SendsTheCategoryOnlyWhenSetAndShowsTheChip()
    {
        string search = await Fetch("/search.js");
        string url = Between(search, "function searchUrl(text, mark, project, category) {", "\n}\n");
        string run = Between(search, "async function runSearch(progress) {", "\n}\n");
        string item = Between(search, "function renderSearchItem(item) {", "\n}\n");
        string read = Between(search, "function readSearchItem(item) {", "\n}\n");

        Assert.Contains("if (category !== \"\") {\n    url += \"&category=\" + encodeURIComponent(category);\n  }", url,
            StringComparison.Ordinal);
        Assert.Contains("const category = searchCategory().value;", run, StringComparison.Ordinal);
        Assert.Contains("if (text === \"\" && mark === \"\" && project === \"\" && category === \"\") {", run,
            StringComparison.Ordinal);
        Assert.Contains("authFetch(searchUrl(text, mark, project, category),", run, StringComparison.Ordinal);
        Assert.Contains("Categories.includes(item.category) ? item.category : null", read, StringComparison.Ordinal);
        Assert.Contains("const chip = categoryChip(item.category);", item, StringComparison.Ordinal);
        Assert.Contains("meta.appendChild(chip);", item, StringComparison.Ordinal);
    }

    private static string Colour(string block, string property)
    {
        Match match = Regex.Match(block, Regex.Escape(property) + ": (#[0-9a-fA-F]{6});");
        Assert.True(match.Success, property);
        return match.Groups[1].Value;
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
