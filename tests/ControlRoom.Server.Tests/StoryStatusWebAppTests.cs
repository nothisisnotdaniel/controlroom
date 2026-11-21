using System.Text.Json;

namespace ControlRoom.Server.Tests;

/// <summary>The status chip in the story view (#125), checked in <c>app.js</c>, <c>edit.js</c>, <c>app.css</c> and
/// <c>CLAUDE.md</c> as text (there is no JS test runtime yet, see #59), and the <c>/api/backlogs</c> marks it is
/// built from.</summary>
public sealed class StoryStatusWebAppTests
{
    [Theory]
    [InlineData("x: { key: \"done\", label: \"Done\" },")]
    [InlineData("\"?\": { key: \"unapproved\", label: \"Unapproved\" },")]
    [InlineData("\" \": { key: \"open\", label: \"Open\" },")]
    [InlineData("h: { key: \"user\", label: \"Your task\" },")]
    public async Task AppJs_EachMarkHasItsStatus(string entry)
    {
        string app = await Fetch("/app.js");
        string map = Between(app, "const StoryStatuses = {", "\n};\n");

        Assert.Contains(entry, map, StringComparison.Ordinal);
        Assert.Equal(4, map.Split('\n').Count(line => line.StartsWith("  ", StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData("- [x] [#1](backlog/001-a.md) One\n", "done", "x")]
    [InlineData("- [?] [#1](backlog/001-a.md) One\n", "approval", "?")]
    [InlineData("- [ ] [#1](backlog/001-a.md) One\n", "open", " ")]
    [InlineData("- [h] [#1](backlog/001-a.md) One\n", "user", "h")]
    public async Task Backlogs_GiveEachLineItsMarkAndLineInItsGroup(string backlog, string group, string mark)
    {
        using TempFactoryRoot root = new();
        root.AddProject("Alpha", backlog);
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();

        using JsonDocument json = JsonDocument.Parse(await client.GetStringAsync("/api/backlogs"));

        JsonElement entry = json.RootElement.GetProperty("projects")[0].GetProperty(group)[0];
        Assert.Equal(1, entry.GetProperty("number").GetInt32());
        Assert.Equal(0, entry.GetProperty("line").GetInt32());
        Assert.Equal(mark, entry.GetProperty("mark").GetString());
    }

    [Fact]
    public async Task AppJs_ChipIsTextOnlyWithItsClassOnlyFromTheMap()
    {
        string app = await Fetch("/app.js");
        string chip = Between(app, "function statusChip(mark) {", "\n}\n");

        Assert.Contains("if (typeof mark !== \"string\" || !Object.hasOwn(StoryStatuses, mark)) {\n    return null;\n  }",
            chip, StringComparison.Ordinal);
        Assert.Contains("element(\"span\", \"status-chip status-\" + status.key);", chip, StringComparison.Ordinal);
        Assert.Contains("chip.appendChild(element(\"span\", \"visually-hidden\", \"Status: \"));", chip,
            StringComparison.Ordinal);
        Assert.Contains("chip.append(status.label);", chip, StringComparison.Ordinal);
        Assert.DoesNotContain("innerHTML", app, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AppJs_ChipSitsInTheHeaderAfterTheTitleOrCategory_AndGoesWithoutAMark()
    {
        string app = await Fetch("/app.js");
        string place = Between(app, "function placeStatusChip(article, chip) {", "\n}\n");
        string show = Between(app, "function showStoryStatus(mark) {", "\n}\n");

        Assert.Contains("article.querySelector(\":scope > .story-title\");", place, StringComparison.Ordinal);
        Assert.Contains("next !== null && next.classList.contains(\"category-chip\") ? next : title;", place,
            StringComparison.Ordinal);
        Assert.Contains("anchor.after(chip);", place, StringComparison.Ordinal);
        int remove = show.IndexOf("old.remove();", StringComparison.Ordinal);
        int build = show.IndexOf("const chip = mark === null ? null : statusChip(mark);", StringComparison.Ordinal);
        int add = show.IndexOf("placeStatusChip(article, chip);", StringComparison.Ordinal);
        Assert.True(remove >= 0 && remove < build && build < add);
    }

    [Fact]
    public async Task EditJs_LookupReadsAllFourGroups_AndFindStoryLineStillDropsDone()
    {
        string edit = await Fetch("/edit.js");
        string entry = Between(edit, "async function findStoryEntry(project, number) {", "\n}\n");
        string notDone = Between(edit, "function notDone(entry) {", "\n}\n");
        string line = Between(edit, "async function findStoryLine(project, number) {", "\n}\n");

        Assert.Contains("for (const group of [\"open\", \"approval\", \"user\", \"done\"]) {", entry, StringComparison.Ordinal);
        Assert.Contains("mark: asText(entry.mark),", entry, StringComparison.Ordinal);
        Assert.Contains("if (entry === null || entry.group === \"done\") {\n    return null;\n  }", notDone,
            StringComparison.Ordinal);
        Assert.Contains("return notDone(await findStoryEntry(project, number));", line, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EditJs_ShowStoryDecisionShowsTheFoundMarkOrNoChip()
    {
        string edit = await Fetch("/edit.js");
        string show = Between(edit, "async function showStoryDecision(target, data) {", "\n}\n");

        int stale = show.IndexOf("if (request !== storyDecisionRequest", StringComparison.Ordinal);
        int status = show.IndexOf("showStoryStatus(entry === null ? null : entry.mark);", StringComparison.Ordinal);
        int bar = show.IndexOf("const bar = document.querySelector(\"#story .story-bar\");", StringComparison.Ordinal);
        Assert.True(stale >= 0 && stale < status && status < bar);
        Assert.Contains("} catch {\n      entry = null;\n    }", show, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApproveLeavesTheStory_AndLoadStoryBuildsTheChipOnTheNextVisit()
    {
        string edit = await Fetch("/edit.js");
        string app = await Fetch("/app.js");
        string decide = Between(edit, "async function decideStory(target, number, action) {", "\n}\n");
        string load = Between(app, "async function loadStory(target) {", "\n}\n");

        // Since #128 Approve closes the story view; the chip is built again when the story is opened next.
        Assert.Contains("loadInbox();\n  leaveDecidedStory();", decide, StringComparison.Ordinal);
        Assert.DoesNotContain("location.reload", edit, StringComparison.Ordinal);
        Assert.Contains("showStoryDecision(target, data);", load, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AppJs_LiveRefreshCarriesTheChipOver()
    {
        string app = await Fetch("/app.js");
        string refresh = Between(app, "async function refreshStory() {", "\n}\n");

        int carry = refresh.IndexOf("placeStatusChip(article, status);", StringComparison.Ordinal);
        int replace = refresh.IndexOf("old.replaceWith(article);", StringComparison.Ordinal);
        int decision = refresh.IndexOf("showStoryDecision(target, data);", StringComparison.Ordinal);
        Assert.True(carry >= 0 && carry < replace && replace < decision);
    }

    [Fact]
    public async Task AppCss_ChipSharesTheCategoryStyle_WithAColourPairPerStatusInLightAndDark()
    {
        string css = await Fetch("/app.css");
        int dark = css.IndexOf("@media (prefers-color-scheme: dark) {", StringComparison.Ordinal);
        Assert.True(dark > 0);
        string light = css[..dark];
        string darkPart = css[dark..];

        string chip = Between(css, ".category-chip,\n.status-chip {", "\n}\n");
        Assert.Contains("display: inline-block;", chip, StringComparison.Ordinal);
        Assert.Contains("white-space: nowrap;", chip, StringComparison.Ordinal);
        Assert.Contains(".story-title + .category-chip {\n  margin: -4px 6px 12px 0;\n}", css, StringComparison.Ordinal);
        Assert.Contains(".story-title + .status-chip,\n.story-title + .category-chip + .status-chip {\n  margin: -4px 0 12px;\n}",
            css, StringComparison.Ordinal);
        foreach (string key in new[] { "done", "unapproved", "open", "user" })
        {
            foreach (string part in new[] { "bg", "fg" })
            {
                string variable = "--status-" + key + "-" + part + ":";
                Assert.Contains(variable, light, StringComparison.Ordinal);
                Assert.Contains(variable, darkPart, StringComparison.Ordinal);
            }

            Assert.Contains(
                ".status-" + key + " {\n  background: var(--status-" + key + "-bg);\n  color: var(--status-" + key + "-fg);\n}",
                css, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ClaudeMd_DescribesTheStatusChip()
    {
        string text = File.ReadAllText(Path.Combine(RepoFolder(), "CLAUDE.md"));

        Assert.Contains("a status chip (#125, `statusChip`: `span.status-chip.status-<key>`", text, StringComparison.Ordinal);
    }

    private static string Between(string text, string start, string end)
    {
        int from = text.IndexOf(start, StringComparison.Ordinal);
        Assert.True(from >= 0, start);
        int to = text.IndexOf(end, from + start.Length, StringComparison.Ordinal);
        Assert.True(to > from, end);
        return text[from..to];
    }

    private static string RepoFolder()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "ControlRoom.slnx")))
        {
            current = current.Parent;
        }

        Assert.NotNull(current);
        return current.FullName;
    }

    private static async Task<string> Fetch(string path)
    {
        using TempFactoryRoot root = new();
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateClient();
        return await client.GetStringAsync(path);
    }
}
