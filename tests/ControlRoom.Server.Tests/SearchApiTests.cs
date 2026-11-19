using System.Net;
using System.Text;
using System.Text.Json;

namespace ControlRoom.Server.Tests;

/// <summary><c>GET /api/search</c> (<see cref="SearchEndpoint"/>): the gates, the response shape, number, word and
/// story matches, refused stories, the filters, the order and cap, the <c>400</c> cases and that nothing is
/// written.</summary>
public sealed class SearchApiTests
{
    private const string Route = "/api/search";

    private const string AlphaBacklog =
        "# Alpha\n"                                                                  // 0
        + "- [x] [#1](backlog/001-done.md) Done Widget\n"                            // 1
        + "- [ ] [#2](backlog/002-open.md) Open widget → 7-x\n"                      // 2
        + "- [?] [#3](backlog/003-proposal.md) Proposal gadget\n"                    // 3
        + "- [h] [#4](backlog/004-hand.md) Hand task (after: #2)\n"                  // 4
        + "- [ ] Old line: free widget text\n"                                       // 5
        + "- [X] [#6](backlog/006-big.md) Big done\n";                               // 6

    private const string BetaBacklog =
        "# Beta\n"                                                                   // 0
        + "- [ ] [#2](backlog/002-beta.md) Beta second\n"                            // 1
        + "- [h] [#7](backlog/007-zebra.md) Zebra in title\n";                       // 2

    [Fact]
    public async Task Search_WithoutTheToken_Is401()
    {
        using TempFactoryRoot root = new();
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(Route + "?q=widget");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("{\"error\":\"unauthorized\"}", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Search_GetIs200JsonWithNoStore_AndHeadIsAllowed()
    {
        using TempFactoryRoot root = new();
        root.AddProject("Alpha", AlphaBacklog);
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();

        using HttpResponseMessage response = await client.GetAsync(Route + "?q=widget");
        using HttpResponseMessage head = await client.SendAsync(new HttpRequestMessage(HttpMethod.Head, Route + "?q=x"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal(HttpStatusCode.OK, head.StatusCode);
        Assert.True(head.Headers.CacheControl?.NoStore);
    }

    [Fact]
    public async Task Search_HasTheExactShape_ProjectsInFinderOrder_AndUnreadableNames()
    {
        using TempFactoryRoot root = new();
        root.AddProject("beta", BetaBacklog);
        string alpha = root.AddProject("Alpha", AlphaBacklog);
        TempFactoryRoot.AddStory(alpha, "002-open.md", "# Open\n\nA secret story sentence.\n");
        root.AddProject("Missing", null);
        string locked = root.AddProject("Locked", "- [ ] [#1](backlog/001-l.md) Locked widget\n");
        root.Lock(Path.Combine(locked, "backlog.md"));
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();

        using JsonDocument json = await Get(client, "?q=widget");

        JsonElement body = json.RootElement;
        Assert.Equal(["items", "total", "projects", "unreadable"], body.EnumerateObject().Select(p => p.Name).ToArray());
        Assert.Equal(["Alpha", "beta", "Locked", "Missing"], Names(body.GetProperty("projects")));
        Assert.Equal(["Locked", "Missing"], Names(body.GetProperty("unreadable")));
        foreach (JsonElement item in body.GetProperty("items").EnumerateArray())
        {
            Assert.Equal(
                ["project", "number", "mark", "title", "rest", "story", "match"],
                item.EnumerateObject().Select(p => p.Name).ToArray());
        }

        JsonElement open = Item(body, "Alpha", 2);
        Assert.Equal("open", open.GetProperty("mark").GetString());
        Assert.Equal("Open widget", open.GetProperty("title").GetString());
        Assert.Equal("→ 7-x", open.GetProperty("rest").GetString());
        Assert.True(open.GetProperty("story").GetBoolean());
        Assert.Equal("title", open.GetProperty("match").GetString());
        JsonElement old = body.GetProperty("items").EnumerateArray()
            .Single(item => item.GetProperty("number").ValueKind == JsonValueKind.Null);
        Assert.Equal("Old line", old.GetProperty("title").GetString());
        Assert.Equal("free widget text", old.GetProperty("rest").GetString());
        Assert.False(old.GetProperty("story").GetBoolean());
        string raw = body.GetRawText();
        Assert.DoesNotContain("secret story", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("backlog/", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("\"version\"", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("\"line\"", raw, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Search_MapsEveryMark()
    {
        using TempFactoryRoot root = new();
        root.AddProject("Alpha", AlphaBacklog);
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();

        using JsonDocument json = await Get(client, "?project=Alpha");

        Assert.Equal(
            ["Alpha#1:done", "Alpha#2:open", "Alpha#3:approval", "Alpha#4:task", "Alpha#-:open", "Alpha#6:done"],
            json.RootElement.GetProperty("items").EnumerateArray()
                .Select(item => Key(item) + ":" + item.GetProperty("mark").GetString())
                .ToArray());
    }

    [Theory]
    [InlineData("#2")]
    [InlineData(" # 2 ")]
    [InlineData("#2 ")]
    public async Task Search_HashNumber_FindsThatNumberInEveryProject(string query)
    {
        using TempFactoryRoot root = new();
        root.AddProject("Alpha", AlphaBacklog);
        root.AddProject("Beta", BetaBacklog);
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();

        using JsonDocument json = await Get(client, "?q=" + Uri.EscapeDataString(query));

        Assert.Equal(["Alpha#2", "Beta#2"], Keys(json.RootElement));
        Assert.All(json.RootElement.GetProperty("items").EnumerateArray(),
            item => Assert.Equal("number", item.GetProperty("match").GetString()));
        Assert.Equal(2, json.RootElement.GetProperty("total").GetInt32());
    }

    [Theory]
    [InlineData("Beta#2")]
    [InlineData("beta#2")]
    [InlineData("BETA # 2")]
    public async Task Search_NameHashNumber_FindsOnlyThatItem_IgnoringCase(string query)
    {
        using TempFactoryRoot root = new();
        root.AddProject("Alpha", AlphaBacklog);
        root.AddProject("Beta", BetaBacklog);
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();

        using JsonDocument json = await Get(client, "?q=" + Uri.EscapeDataString(query));

        Assert.Equal(["Beta#2"], Keys(json.RootElement));
        Assert.Equal("number", json.RootElement.GetProperty("items")[0].GetProperty("match").GetString());
    }

    [Fact]
    public async Task Search_UnknownNameHashNumber_GivesNoItemsNotAnError()
    {
        using TempFactoryRoot root = new();
        root.AddProject("Alpha", AlphaBacklog);
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();

        using HttpResponseMessage response = await client.GetAsync(Route + "?q=Nope%232");
        using JsonDocument json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(json.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(0, json.RootElement.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Search_EveryWordMustOccurInTitleOrRest_IgnoringCase()
    {
        using TempFactoryRoot root = new();
        root.AddProject("Alpha", AlphaBacklog);
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();

        using JsonDocument both = await Get(client, "?q=" + Uri.EscapeDataString("WIDGET  open"));
        using JsonDocument rest = await Get(client, "?q=" + Uri.EscapeDataString("7-x widget"));
        using JsonDocument old = await Get(client, "?q=free");
        using JsonDocument none = await Get(client, "?q=" + Uri.EscapeDataString("widget nothing"));
        using JsonDocument repeated = await Get(client, "?q=" + Uri.EscapeDataString("widget Widget WIDGET open"));

        Assert.Equal(["Alpha#2"], Keys(both.RootElement));
        Assert.Equal(["Alpha#2"], Keys(rest.RootElement));
        Assert.Equal(["Alpha#-"], Keys(old.RootElement));
        Assert.Equal("title", old.RootElement.GetProperty("items")[0].GetProperty("match").GetString());
        Assert.Empty(none.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(["Alpha#2"], Keys(repeated.RootElement));
    }

    [Fact]
    public async Task Search_AWordOnlyInTheStory_MatchesAsStory_AndAnOldLineNeverReadsAStory()
    {
        using TempFactoryRoot root = new();
        string alpha = root.AddProject("Alpha", AlphaBacklog);
        TempFactoryRoot.AddStory(alpha, "003-proposal.md", "# Proposal\n\nThe Quokka and the zebra.\n");
        TempFactoryRoot.AddStory(alpha, "004-hand.md", "# Hand\n\nOnly a quokka.\n");
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();

        using JsonDocument one = await Get(client, "?q=quokka");
        using JsonDocument two = await Get(client, "?q=" + Uri.EscapeDataString("zebra QUOKKA"));
        using JsonDocument mixed = await Get(client, "?q=" + Uri.EscapeDataString("gadget zebra"));

        Assert.Equal(["Alpha#3", "Alpha#4"], Keys(one.RootElement));
        Assert.All(one.RootElement.GetProperty("items").EnumerateArray(),
            item => Assert.Equal("story", item.GetProperty("match").GetString()));
        Assert.Equal(["Alpha#3"], Keys(two.RootElement));
        // Every word must be in the title/rest, or else every word in the story: "gadget" is only in the title.
        Assert.Empty(mixed.RootElement.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task Search_AnItemMatchingOnTitleIsNotAlsoListedAsStory()
    {
        using TempFactoryRoot root = new();
        string alpha = root.AddProject("Alpha", AlphaBacklog);
        TempFactoryRoot.AddStory(alpha, "002-open.md", "# Open widget\n\nwidget widget\n");
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();

        using JsonDocument json = await Get(client, "?q=widget&project=Alpha&mark=open");

        Assert.Equal(["Alpha#2", "Alpha#-"], Keys(json.RootElement));
        Assert.All(json.RootElement.GetProperty("items").EnumerateArray(),
            item => Assert.Equal("title", item.GetProperty("match").GetString()));
    }

    [Fact]
    public async Task Search_RefusedStoriesAreNeverRead_ButTheirItemsStillMatchOnTitle()
    {
        using TempFactoryRoot root = new();
        string outside = Path.Combine(root.Path, ".outside");
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "001-linked.md"), "secretword\n");
        File.WriteAllText(Path.Combine(outside, "002-folder.md"), "secretword\n");
        string alpha = root.AddProject(
            "Alpha",
            "- [ ] [#1](backlog/001-linked.md) Linked file\n"
            + "- [ ] [#8](backlog/009-other.md) Mismatched number\n"
            + "- [ ] [#10](backlog/010-big.md) Too big\n"
            + "- [ ] [#11](../secret.md) Outside link\n"
            + "- [ ] [#12](backlog/../secret.md) Dotted link\n");
        Directory.CreateDirectory(Path.Combine(alpha, "backlog"));
        File.CreateSymbolicLink(Path.Combine(alpha, "backlog", "001-linked.md"), Path.Combine(outside, "001-linked.md"));
        TempFactoryRoot.AddStory(alpha, "009-other.md", "secretword\n");
        TempFactoryRoot.AddStory(alpha, "010-big.md", "secretword\n" + new string('a', (1024 * 1024) + 1 - 11));
        Assert.Equal((1024 * 1024) + 1, new FileInfo(Path.Combine(alpha, "backlog", "010-big.md")).Length);
        File.WriteAllText(Path.Combine(alpha, "secret.md"), "secretword\n");
        string beta = root.AddProject("Beta", "- [ ] [#2](backlog/002-folder.md) Linked folder\n");
        File.CreateSymbolicLink(Path.Combine(beta, "backlog"), outside);
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();

        using JsonDocument secret = await Get(client, "?q=secretword");
        using JsonDocument linked = await Get(client, "?q=linked");

        Assert.Empty(secret.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(0, secret.RootElement.GetProperty("total").GetInt32());
        Assert.Equal(["Alpha#1", "Beta#2"], Keys(linked.RootElement));
        Assert.All(linked.RootElement.GetProperty("items").EnumerateArray(),
            item => Assert.Equal("title", item.GetProperty("match").GetString()));
    }

    [Fact]
    public async Task Search_ABigStoryUnderTheCapIsRead()
    {
        using TempFactoryRoot root = new();
        string alpha = root.AddProject("Alpha", "- [ ] [#10](backlog/010-big.md) Big\n");
        TempFactoryRoot.AddStory(alpha, "010-big.md", "secretword\n" + new string('a', (1024 * 1024) - 11));
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();

        using JsonDocument json = await Get(client, "?q=secretword");

        Assert.Equal(["Alpha#10"], Keys(json.RootElement));
    }

    [Theory]
    [InlineData("open", new[] { "Alpha#2", "Alpha#-", "Beta#2" })]
    [InlineData("approval", new[] { "Alpha#3" })]
    [InlineData("task", new[] { "Alpha#4", "Beta#7" })]
    [InlineData("done", new[] { "Alpha#1", "Alpha#6" })]
    public async Task Search_MarkAlone_ReturnsEveryItemWithThatMarkAsFilter(string mark, string[] expected)
    {
        using TempFactoryRoot root = new();
        root.AddProject("Alpha", AlphaBacklog);
        root.AddProject("Beta", BetaBacklog);
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();

        using JsonDocument json = await Get(client, "?q=&mark=" + mark);

        Assert.Equal(expected, Keys(json.RootElement));
        Assert.All(json.RootElement.GetProperty("items").EnumerateArray(),
            item => Assert.Equal("filter", item.GetProperty("match").GetString()));
        Assert.Equal(expected.Length, json.RootElement.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Search_ProjectAndMarkFiltersCombineWithEachOtherAndWithQ()
    {
        using TempFactoryRoot root = new();
        root.AddProject("Alpha", AlphaBacklog);
        root.AddProject("Beta", BetaBacklog);
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();

        using JsonDocument project = await Get(client, "?q=%20%20&project=Beta");
        using JsonDocument both = await Get(client, "?project=Beta&mark=task");
        using JsonDocument withQ = await Get(client, "?q=widget&mark=done");
        using JsonDocument number = await Get(client, "?q=%232&project=Beta");
        using JsonDocument numberMark = await Get(client, "?q=%232&mark=task");

        Assert.Equal(["Beta#2", "Beta#7"], Keys(project.RootElement));
        Assert.Equal(["Beta#7"], Keys(both.RootElement));
        Assert.Equal(["Alpha#1"], Keys(withQ.RootElement));
        Assert.Equal(["Beta#2"], Keys(number.RootElement));
        Assert.Empty(numberMark.RootElement.GetProperty("items").EnumerateArray());
    }

    [Theory]
    [InlineData("")]
    [InlineData("?q=")]
    [InlineData("?q=%20%09%20")]
    [InlineData("?q=&mark=&project=")]
    public async Task Search_EmptyQueryWithoutAFilter_GivesNoItems(string query)
    {
        using TempFactoryRoot root = new();
        root.AddProject("Alpha", AlphaBacklog);
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();

        using JsonDocument json = await Get(client, query);

        Assert.Empty(json.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(0, json.RootElement.GetProperty("total").GetInt32());
        Assert.Equal(["Alpha"], Names(json.RootElement.GetProperty("projects")));
    }

    [Fact]
    public async Task Search_OrdersTitleThenStory_EachByProjectThenFileLine()
    {
        using TempFactoryRoot root = new();
        string beta = root.AddProject(
            "Beta",
            "- [ ] [#1](backlog/001-b.md) Beta story match\n"
            + "- [x] [#2](backlog/002-b.md) Beta zebra done\n"
            + "- [ ] [#3](backlog/003-b.md) Beta zebra open\n");
        TempFactoryRoot.AddStory(beta, "001-b.md", "zebra\n");
        string alpha = root.AddProject(
            "Alpha",
            "- [ ] [#5](backlog/005-a.md) Alpha story match\n"
            + "- [X] [#6](backlog/006-a.md) Alpha zebra done\n"
            + "- [?] [#7](backlog/007-a.md) Alpha zebra proposed\n"
            + "- [ ] [#8](backlog/008-a.md) Alpha zebra open\n");
        TempFactoryRoot.AddStory(alpha, "005-a.md", "zebra\n");
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();

        using JsonDocument json = await Get(client, "?q=zebra");
        using JsonDocument filter = await Get(client, "?project=Alpha");

        Assert.Equal(
            ["Alpha#6", "Alpha#7", "Alpha#8", "Beta#2", "Beta#3", "Alpha#5", "Beta#1"],
            Keys(json.RootElement));
        Assert.Equal(["Alpha#5", "Alpha#6", "Alpha#7", "Alpha#8"], Keys(filter.RootElement));
    }

    [Fact]
    public async Task Search_ReturnsAtMost100Items_AndTheFullTotal()
    {
        using TempFactoryRoot root = new();
        StringBuilder backlog = new();
        for (int number = 1; number <= 120; number += 1)
        {
            backlog.Append("- [ ] [#").Append(number).Append("](backlog/").Append(number.ToString("D3"))
                .Append("-x.md) Item word ").Append(number).Append('\n');
        }

        root.AddProject("Alpha", backlog.ToString());
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();

        using JsonDocument json = await Get(client, "?q=word");
        using JsonDocument filter = await Get(client, "?mark=open");

        Assert.Equal(100, json.RootElement.GetProperty("items").GetArrayLength());
        Assert.Equal(120, json.RootElement.GetProperty("total").GetInt32());
        Assert.Equal(1, json.RootElement.GetProperty("items")[0].GetProperty("number").GetInt32());
        Assert.Equal(100, json.RootElement.GetProperty("items")[99].GetProperty("number").GetInt32());
        Assert.Equal(100, filter.RootElement.GetProperty("items").GetArrayLength());
        Assert.Equal(120, filter.RootElement.GetProperty("total").GetInt32());
    }

    [Theory]
    [InlineData("?q=a&q=b")]
    [InlineData("?mark=open&mark=done")]
    [InlineData("?project=Alpha&project=Alpha")]
    [InlineData("?mark=closed")]
    [InlineData("?mark=OPEN")]
    [InlineData("?project=alpha")]
    [InlineData("?project=Nope")]
    [InlineData("?project=..")]
    public async Task Search_InvalidRequests_Are400(string query)
    {
        using TempFactoryRoot root = new();
        root.AddProject("Alpha", AlphaBacklog);
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();

        using HttpResponseMessage response = await client.GetAsync(Route + query);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("{\"error\":\"invalid\"}", await response.Content.ReadAsStringAsync());
        Assert.True(response.Headers.CacheControl?.NoStore);
    }

    [Fact]
    public async Task Search_AQueryOver200Characters_Is400_And200IsFine()
    {
        using TempFactoryRoot root = new();
        root.AddProject("Alpha", AlphaBacklog);
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();

        using HttpResponseMessage longer = await client.GetAsync(Route + "?q=" + new string('a', 201));
        using HttpResponseMessage exact = await client.GetAsync(Route + "?q=" + new string('a', 200));

        Assert.Equal(HttpStatusCode.BadRequest, longer.StatusCode);
        Assert.Equal("{\"error\":\"invalid\"}", await longer.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, exact.StatusCode);
    }

    [Fact]
    public async Task Search_NeverWritesAFile()
    {
        using TempFactoryRoot root = new();
        string alpha = root.AddProject("Alpha", AlphaBacklog);
        TempFactoryRoot.AddStory(alpha, "002-open.md", "# Open\n\nquokka\n");
        root.AddProject("Beta", BetaBacklog);
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();
        IReadOnlyList<string> before = root.Snapshot();

        foreach (string query in new[] { "?q=quokka", "?q=%232", "?mark=done", "?q=widget&project=Alpha", "?mark=x" })
        {
            using HttpResponseMessage response = await client.GetAsync(Route + query);
        }

        Assert.Equal(before, root.Snapshot());
    }

    private static async Task<JsonDocument> Get(HttpClient client, string query)
    {
        using HttpResponseMessage response = await client.GetAsync(Route + query);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    private static string[] Names(JsonElement array)
    {
        return array.EnumerateArray().Select(name => name.GetString()!).ToArray();
    }

    private static string Key(JsonElement item)
    {
        JsonElement number = item.GetProperty("number");
        return item.GetProperty("project").GetString() + "#"
            + (number.ValueKind == JsonValueKind.Null ? "-" : number.GetInt32().ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    private static string[] Keys(JsonElement body)
    {
        return body.GetProperty("items").EnumerateArray().Select(Key).ToArray();
    }

    private static JsonElement Item(JsonElement body, string project, int number)
    {
        return body.GetProperty("items").EnumerateArray()
            .Single(item => Key(item) == project + "#" + number.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }
}
