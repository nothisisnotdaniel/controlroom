using System.Net;
using System.Text;
using System.Text.Json;

namespace ControlRoom.Server.Tests;

/// <summary>The backlog position in the story view (#126), checked in <c>app.js</c>, <c>edit.js</c>, <c>app.css</c> and
/// <c>CLAUDE.md</c> as text (there is no JS test runtime yet, see #59), and the <c>/api/backlogs</c> order it is
/// taken from.</summary>
public sealed class StoryPositionWebAppTests
{
    private const string Backlog =
        "# Alpha backlog\n"                          // 0
        + "- [ ] [#1](backlog/001-a.md) One\n"       // 1
        + "- [ ] Plain open line\n"                  // 2
        + "- [ ] [#2](backlog/002-b.md) Two\n"       // 3
        + "- [ ] [#3](backlog/003-c.md) Three\n";    // 4

    [Fact]
    public async Task EditJs_LookupGivesTheOneBasedIndexInTheGroupItLoopsOver()
    {
        string edit = await Fetch("/edit.js");
        string entry = Between(edit, "async function findStoryEntry(project, number) {", "\n}\n");

        int list = entry.IndexOf("const entries = asList(match[group]);", StringComparison.Ordinal);
        int loop = entry.IndexOf("for (let index = 0; index < entries.length; index += 1) {", StringComparison.Ordinal);
        int pick = entry.IndexOf("const entry = entries[index];", StringComparison.Ordinal);
        int position = entry.IndexOf("position: index + 1 };", StringComparison.Ordinal);
        Assert.True(list >= 0 && list < loop && loop < pick && pick < position);
        Assert.Contains("mark: asText(entry.mark),", entry, StringComparison.Ordinal);
        Assert.Contains("for (const group of [\"open\", \"approval\", \"user\", \"done\"]) {", entry, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EditJs_ShowStoryDecisionShowsTheFoundPositionOrNone()
    {
        string edit = await Fetch("/edit.js");
        string show = Between(edit, "async function showStoryDecision(target, data) {", "\n}\n");

        int stale = show.IndexOf("if (request !== storyDecisionRequest", StringComparison.Ordinal);
        int status = show.IndexOf("showStoryStatus(entry === null ? null : entry.mark);", StringComparison.Ordinal);
        int position = show.IndexOf("showStoryPosition(entry === null ? null : entry.position);", StringComparison.Ordinal);
        int bar = show.IndexOf("const bar = document.querySelector(\"#story .story-bar\");", StringComparison.Ordinal);
        Assert.True(stale >= 0 && stale < status && status < position && position < bar);
    }

    [Fact]
    public async Task AppJs_PositionIsTextOnly_AndNoneForAnythingButAWholeNumberFromOne()
    {
        string app = await Fetch("/app.js");
        string texts = Between(app, "const Texts = {", "};");
        string build = Between(app, "function storyPosition(position) {", "\n}\n");

        Assert.Contains("  storyPosition: \"Position \",\n", texts, StringComparison.Ordinal);
        Assert.Contains("if (!Number.isInteger(position) || position < 1) {\n    return null;\n  }", build,
            StringComparison.Ordinal);
        Assert.Contains("return element(\"span\", \"story-position\", Texts.storyPosition + String(position));", build,
            StringComparison.Ordinal);
        Assert.DoesNotContain("innerHTML", app, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AppJs_PositionSitsAfterTheStatusChip_AndGoesWithoutAPosition()
    {
        string app = await Fetch("/app.js");
        string place = Between(app, "function placeStoryPosition(article, node) {", "\n}\n");
        string show = Between(app, "function showStoryPosition(position) {", "\n}\n");

        int status = place.IndexOf("article.querySelector(\":scope > .status-chip\");", StringComparison.Ordinal);
        int afterStatus = place.IndexOf("status.after(node);", StringComparison.Ordinal);
        int title = place.IndexOf("article.querySelector(\":scope > .story-title\");", StringComparison.Ordinal);
        Assert.True(status >= 0 && status < afterStatus && afterStatus < title);
        Assert.Contains("next !== null && next.classList.contains(\"category-chip\") ? next : title;", place,
            StringComparison.Ordinal);
        Assert.Contains("anchor.after(node);", place, StringComparison.Ordinal);
        int remove = show.IndexOf("old.remove();", StringComparison.Ordinal);
        int build = show.IndexOf("const node = position === null ? null : storyPosition(position);", StringComparison.Ordinal);
        int add = show.IndexOf("placeStoryPosition(article, node);", StringComparison.Ordinal);
        Assert.True(remove >= 0 && remove < build && build < add);
        Assert.Contains("article.querySelector(\":scope > .story-position\");", show, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AppJs_LiveRefreshCarriesThePositionOver_ThenLooksItUpAgain()
    {
        string app = await Fetch("/app.js");
        string refresh = Between(app, "async function refreshStory() {", "\n}\n");

        int status = refresh.IndexOf("placeStatusChip(article, status);", StringComparison.Ordinal);
        int carry = refresh.IndexOf("placeStoryPosition(article, position);", StringComparison.Ordinal);
        int replace = refresh.IndexOf("old.replaceWith(article);", StringComparison.Ordinal);
        int decision = refresh.IndexOf("showStoryDecision(target, data);", StringComparison.Ordinal);
        Assert.True(status >= 0 && status < carry && carry < replace && replace < decision);
    }

    [Fact]
    public async Task AppCss_PositionIsMutedAndStaysOnOneLine()
    {
        string css = await Fetch("/app.css");
        string rule = Between(css, ".story-position {", "\n}\n");

        Assert.Contains("color: var(--muted);", rule, StringComparison.Ordinal);
        Assert.Contains("white-space: nowrap;", rule, StringComparison.Ordinal);
        Assert.Contains("display: inline-block;", rule, StringComparison.Ordinal);
    }

    [Fact]
    public void ClaudeMd_DescribesThePosition()
    {
        string text = File.ReadAllText(Path.Combine(RepoFolder(), "CLAUDE.md"));

        Assert.Contains("the backlog position (#126, `storyPosition`: `span.story-position` `Position N`", text,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Backlogs_GiveANumberedItemAtItsPlaceInTheGroup_CountingUnnumberedLines()
    {
        using TempFactoryRoot root = new();
        root.AddProject("Alpha", Backlog);
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();

        JsonElement open = await OpenGroup(client);

        // #2 is the third row of Open (after #1 and the plain line), as the list numbers it.
        Assert.Equal(3, PositionOf(open, 2));
        Assert.Equal(1, PositionOf(open, 1));
        Assert.Equal(4, PositionOf(open, 3));
    }

    [Fact]
    public async Task Backlogs_GiveTheNewPlaceAfterAReorder()
    {
        using TempFactoryRoot root = new();
        string folder = root.AddProject("Alpha", Backlog);
        TempFactoryRoot.AddStory(folder, "001-a.md", "# 001 One\n");
        TempFactoryRoot.AddStory(folder, "002-b.md", "# 002 Two\n");
        TempFactoryRoot.AddStory(folder, "003-c.md", "# 003 Three\n");
        TempFactoryRoot.InitGit(folder, "develop");
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();
        string version = (await Project(client)).GetProperty("version").GetString()!;

        object body = new { project = "Alpha", version, action = "place", line = 4, target = 1, where = "before" };
        using StringContent content = new(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        using HttpResponseMessage response = await client.PostAsync("/api/backlog/change", content);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        JsonElement open = await OpenGroup(client);
        Assert.Equal(1, PositionOf(open, 3));
        Assert.Equal(2, PositionOf(open, 1));
        Assert.Equal(4, PositionOf(open, 2));
    }

    [Fact]
    public async Task Backlogs_GiveNoPlaceForALineThatIsGone()
    {
        using TempFactoryRoot root = new();
        root.AddProject("Alpha", Backlog.Replace("- [ ] [#2](backlog/002-b.md) Two\n", "", StringComparison.Ordinal));
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();

        JsonElement project = await Project(client);

        foreach (string group in new[] { "open", "approval", "user", "done" })
        {
            Assert.Null(PositionOf(project.GetProperty(group), 2));
        }

        Assert.Equal(3, PositionOf(project.GetProperty("open"), 3));
    }

    /// <summary>The 1-based index of item <paramref name="number"/> in a group, as <c>findStoryEntry</c> finds it.</summary>
    private static int? PositionOf(JsonElement group, int number)
    {
        int index = 0;
        foreach (JsonElement entry in group.EnumerateArray())
        {
            index += 1;
            if (entry.TryGetProperty("number", out JsonElement value) && value.ValueKind == JsonValueKind.Number
                && value.GetInt32() == number && entry.TryGetProperty("line", out JsonElement line)
                && line.ValueKind == JsonValueKind.Number)
            {
                return index;
            }
        }

        return null;
    }

    private static async Task<JsonElement> Project(HttpClient client)
    {
        using JsonDocument json = JsonDocument.Parse(await client.GetStringAsync("/api/backlogs"));
        return json.RootElement.GetProperty("projects")[0].Clone();
    }

    private static async Task<JsonElement> OpenGroup(HttpClient client)
    {
        return (await Project(client)).GetProperty("open");
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
