using System.Globalization;
using System.Net;
using System.Text.Json;

namespace ControlRoom.Server.Tests;

/// <summary>The story category on the read endpoints: <c>category</c> on every <c>GET /api/backlogs</c> entry, on
/// <c>GET /api/story</c> and on every <c>GET /api/search</c> item, plus the search's <c>category</c> filter. Every
/// story is read through <see cref="StoryReader"/>, so a missing, symlinked, oversized or badly linked story has no
/// category.</summary>
public sealed class CategoryApiTests
{
    private const string AlphaBacklog =
        "# Alpha\n"                                                    // 0
        + "- [ ] [#1](backlog/001-a.md) Alpha widget one\n"            // 1
        + "- [?] [#2](backlog/002-b.md) Alpha widget two\n"            // 2
        + "- [h] [#3](backlog/003-c.md) Missing story\n"               // 3
        + "- [x] [#4](backlog/004-d.md) Linked story\n"                // 4
        + "- [ ] [#5](backlog/005-e.md) Big story\n"                   // 5
        + "- [ ] Plain open: old widget line\n"                        // 6
        + "- [ ] [#6](elsewhere/006-f.md) Bad link\n"                  // 7
        + "- [x] [#7](backlog/007-g.md) Done widget\n"                 // 8
        + "- [ ] [#8](backlog/008-h.md) Fixes the widget\n";           // 9

    private const string BetaBacklog =
        "# Beta\n"
        + "- [ ] [#1](backlog/001-x.md) Beta one\n";

    [Fact]
    public async Task Backlogs_EveryEntryHasTheCategoryOfItsReadableStoryElseNull()
    {
        using TempFactoryRoot root = Sample();
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();

        using JsonDocument json = JsonDocument.Parse(await client.GetStringAsync("/api/backlogs"));

        JsonElement alpha = Project(json, "Alpha");
        Dictionary<string, string?> categories = [];
        foreach (string group in new[] { "open", "approval", "user", "done" })
        {
            foreach (JsonElement entry in alpha.GetProperty(group).EnumerateArray())
            {
                JsonElement category = entry.GetProperty("category");
                categories[entry.GetProperty("title").GetString()!] =
                    category.ValueKind == JsonValueKind.Null ? null : category.GetString();
            }
        }

        Assert.Equal("bug", categories["Alpha widget one"]);
        Assert.Null(categories["Alpha widget two"]);
        Assert.Null(categories["Missing story"]);
        Assert.Null(categories["Linked story"]);
        Assert.Null(categories["Big story"]);
        Assert.Null(categories["Plain open"]);
        Assert.Null(categories["Bad link"]);
        Assert.Equal("docs", categories["Done widget"]);
        Assert.Equal("security", categories["Fixes the widget"]);
        Assert.Equal(9, categories.Count);

        JsonElement beta = Project(json, "Beta");
        Assert.Equal(JsonValueKind.Null, beta.GetProperty("open")[0].GetProperty("category").ValueKind);
    }

    [Fact]
    public async Task Backlogs_NothingElseInTheEntryChanges()
    {
        using TempFactoryRoot root = Sample();
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();

        using JsonDocument json = JsonDocument.Parse(await client.GetStringAsync("/api/backlogs"));

        JsonElement entry = Project(json, "Alpha").GetProperty("open")[0];
        Assert.Equal(
            ["title", "rest", "number", "mark", "story", "after", "waiting", "category", "line"],
            entry.EnumerateObject().Select(property => property.Name).ToArray());
        Assert.Equal("backlog/001-a.md", entry.GetProperty("story").GetString());
        Assert.Equal(1, entry.GetProperty("line").GetInt32());
    }

    [Theory]
    [InlineData(1, "bug")]
    [InlineData(2, null)]
    [InlineData(7, "docs")]
    [InlineData(8, "security")]
    public async Task Story_HasTheCategoryOfTheTextItReturns(int number, string? expected)
    {
        using TempFactoryRoot root = Sample();
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();

        using JsonDocument json = JsonDocument.Parse(await client.GetStringAsync(
            "/api/story?project=Alpha&number=" + number.ToString(CultureInfo.InvariantCulture)));

        JsonElement category = json.RootElement.GetProperty("category");
        Assert.Equal(expected, category.ValueKind == JsonValueKind.Null ? null : category.GetString());
        Assert.Equal(expected, StoryCategory.Parse(json.RootElement.GetProperty("markdown").GetString()));
    }

    [Fact]
    public async Task Search_EveryItemHasItsCategory()
    {
        using TempFactoryRoot root = Sample();
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();

        using JsonDocument json = await Get(client, "?q=widget");

        Assert.Equal(
            ["Alpha#1:bug", "Alpha#2:-", "Alpha#-:-", "Alpha#7:docs", "Alpha#8:security"],
            Keys(json.RootElement));
    }

    [Fact]
    public async Task Search_CategoryAlone_ListsEveryMatchingItemAsAFilterMatch()
    {
        using TempFactoryRoot root = Sample();
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();

        using JsonDocument json = await Get(client, "?q=&category=bug");

        Assert.Equal(["Alpha#1:bug", "Beta#2:bug"], Keys(json.RootElement));
        Assert.All(
            json.RootElement.GetProperty("items").EnumerateArray(),
            item => Assert.Equal("filter", item.GetProperty("match").GetString()));
        Assert.Equal(2, json.RootElement.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Search_CategoryWithAQuery_KeepsOnlyThatCategory_InTitleAndStoryMatches()
    {
        using TempFactoryRoot root = Sample();
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();

        using JsonDocument title = await Get(client, "?q=widget&category=docs");
        using JsonDocument story = await Get(client, "?q=sprocket&category=bug");
        using JsonDocument number = await Get(client, "?q=%231&category=bug");

        Assert.Equal(["Alpha#7:docs"], Keys(title.RootElement));
        Assert.Equal("title", title.RootElement.GetProperty("items")[0].GetProperty("match").GetString());
        Assert.Equal(["Beta#2:bug"], Keys(story.RootElement));
        Assert.Equal("story", story.RootElement.GetProperty("items")[0].GetProperty("match").GetString());
        Assert.Equal(["Alpha#1:bug"], Keys(number.RootElement));
    }

    [Fact]
    public async Task Search_CategoryWithMarkAndProject()
    {
        using TempFactoryRoot root = Sample();
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();

        using JsonDocument done = await Get(client, "?category=docs&mark=done");
        using JsonDocument open = await Get(client, "?category=docs&mark=open");
        using JsonDocument beta = await Get(client, "?category=bug&project=Beta");

        Assert.Equal(["Alpha#7:docs"], Keys(done.RootElement));
        Assert.Empty(Keys(open.RootElement));
        Assert.Equal(["Beta#2:bug"], Keys(beta.RootElement));
    }

    [Fact]
    public async Task Search_AnEmptyCategoryIsNoFilter()
    {
        using TempFactoryRoot root = Sample();
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();

        using JsonDocument none = await Get(client, "?q=&category=");
        using JsonDocument words = await Get(client, "?q=widget&category=");

        Assert.Empty(Keys(none.RootElement));
        Assert.Equal(5, Keys(words.RootElement).Length);
    }

    [Theory]
    [InlineData("?category=nope")]
    [InlineData("?category=FEATURE")]
    [InlineData("?category=Bug")]
    [InlineData("?category=bug%20")]
    [InlineData("?category=bug&category=docs")]
    [InlineData("?q=widget&category=bug&category=bug")]
    public async Task Search_UnknownOrRepeatedCategory_Is400Invalid(string query)
    {
        using TempFactoryRoot root = Sample();
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();

        using HttpResponseMessage response = await client.GetAsync("/api/search" + query);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("{\"error\":\"invalid\"}", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Backlogs_ASymlinkedStoryFolderGivesNoCategory()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using TempFactoryRoot root = new();
        string gamma = root.AddProject("Gamma", "- [ ] [#1](backlog/001-x.md) Gamma one\n");
        string real = Path.Combine(root.Path, "real-stories");
        Directory.CreateDirectory(real);
        File.WriteAllText(Path.Combine(real, "001-x.md"), "# 001 Gamma one\n\nCategory: bug\n");
        Directory.CreateSymbolicLink(Path.Combine(gamma, "backlog"), real);
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();

        using JsonDocument json = JsonDocument.Parse(await client.GetStringAsync("/api/backlogs"));
        using JsonDocument search = await Get(client, "?category=bug");

        Assert.Equal(
            JsonValueKind.Null, Project(json, "Gamma").GetProperty("open")[0].GetProperty("category").ValueKind);
        Assert.Empty(Keys(search.RootElement));
    }

    /// <summary>Alpha: a valid category, an invalid one, a missing story, a symlinked one, one over 1 MiB, an
    /// unnumbered line, a bad link, a done item with one and a category found only through the story text. Beta:
    /// no category line and a story that names a word.</summary>
    private static TempFactoryRoot Sample()
    {
        TempFactoryRoot root = new();
        string alpha = root.AddProject("Alpha", AlphaBacklog);
        TempFactoryRoot.AddStory(alpha, "001-a.md", "# 001 Alpha widget one\n\nCategory: bug\n\n## What\n\nx\n");
        TempFactoryRoot.AddStory(alpha, "002-b.md", "# 002 Alpha widget two\n\nCategory: Bug\n\n## What\n\nx\n");
        string outside = Path.Combine(root.Path, "outside.md");
        File.WriteAllText(outside, "# 004\n\nCategory: ui\n");
        File.CreateSymbolicLink(Path.Combine(alpha, "backlog", "004-d.md"), outside);
        TempFactoryRoot.AddStory(
            alpha, "005-e.md", "# 005\n\nCategory: docs\n\n" + new string('x', (int)StoryReader.MaxBytes) + "\n");
        string elsewhere = Path.Combine(alpha, "elsewhere");
        Directory.CreateDirectory(elsewhere);
        File.WriteAllText(Path.Combine(elsewhere, "006-f.md"), "# 006\n\nCategory: bug\n");
        TempFactoryRoot.AddStory(alpha, "006-f.md", "# 006\n\nCategory: bug\n");
        TempFactoryRoot.AddStory(alpha, "007-g.md", "﻿# 007 Done widget\r\n\r\nCategory: docs\r\n");
        TempFactoryRoot.AddStory(alpha, "008-h.md", "# 008\n\nCategory: security\n\n## What\n\nCategory: bug\n");

        string beta = root.AddProject("Beta", BetaBacklog + "- [ ] [#2](backlog/002-y.md) Beta two\n");
        TempFactoryRoot.AddStory(beta, "001-x.md", "# 001 Beta one\n\n## What\n\nNo category.\n");
        TempFactoryRoot.AddStory(beta, "002-y.md", "# 002 Beta two\n\nCategory: bug\n\n## What\n\nA sprocket.\n");
        return root;
    }

    private static JsonElement Project(JsonDocument json, string name)
    {
        return json.RootElement.GetProperty("projects").EnumerateArray()
            .Single(project => project.GetProperty("name").GetString() == name);
    }

    private static async Task<JsonDocument> Get(HttpClient client, string query)
    {
        using HttpResponseMessage response = await client.GetAsync("/api/search" + query);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    /// <summary>Each item as <c>Project#N:category</c> (<c>-</c> for no number or no category), in response
    /// order.</summary>
    private static string[] Keys(JsonElement body)
    {
        return body.GetProperty("items").EnumerateArray()
            .Select(item =>
            {
                JsonElement number = item.GetProperty("number");
                JsonElement category = item.GetProperty("category");
                return item.GetProperty("project").GetString() + "#"
                    + (number.ValueKind == JsonValueKind.Null
                        ? "-"
                        : number.GetInt32().ToString(CultureInfo.InvariantCulture))
                    + ":" + (category.ValueKind == JsonValueKind.Null ? "-" : category.GetString());
            })
            .ToArray();
    }
}
