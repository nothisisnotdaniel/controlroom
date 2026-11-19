using System.Net;
using System.Security.Cryptography;
using System.Text.Json;

namespace ControlRoom.Server.Tests;

/// <summary><c>GET /api/tasks</c> (<see cref="TasksEndpoint"/>): the gates, the task shape, the Ready/Waiting split
/// (<see cref="BacklogWaiting"/>) with same- and cross-project dependencies, the order and unreadable backlogs.</summary>
public sealed class TasksApiTests
{
    private const string Route = "/api/tasks";

    private const string AlphaBacklog =
        "# Alpha\n"                                                                        // 0
        + "- [x] [#1](backlog/001-done.md) Done one\n"                                     // 1
        + "- [h] [#2](backlog/002-hand.md) Ready plain → 7-x\n"                            // 2
        + "- [h] [#3](backlog/003-hand.md) Ready after (after: #1; Beta#5)\n"              // 3
        + "- [h] [#4](backlog/004-hand.md) Waits (after: #1; #9; Beta#6; Nope#1)\n"        // 4
        + "- [?] [#5](backlog/005-proposal.md) Proposal five\n"                            // 5
        + "- [ ] [#6](backlog/006-open.md) Open six (after: #1)\n"                         // 6
        + "- [h] Old hand: no number\n"                                                    // 7
        + "- [h] [#8](backlog/008-hand.md) Waits for open (after: #6)\n"                   // 8
        + "Prose with - [h] inside.\n";                                                    // 9

    private const string BetaBacklog =
        "# Beta\n"                                                                         // 0
        + "- [X] [#5](backlog/005-done.md) Done five\n"                                    // 1
        + "- [ ] [#6](backlog/006-open.md) Open six\n"                                     // 2
        + "- [h] [#7](backlog/007-hand.md) Beta hand (after: Alpha#1)\n";                  // 3

    [Fact]
    public async Task Tasks_WithoutTheToken_Is401()
    {
        using TempFactoryRoot root = new();
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(Route);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("{\"error\":\"unauthorized\"}", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Tasks_WithAWrongToken_Is401()
    {
        using TempFactoryRoot root = new();
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateClient();
        using HttpRequestMessage request = new(HttpMethod.Get, Route);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", new string('A', 43));

        using HttpResponseMessage response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("{\"error\":\"unauthorized\"}", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Tasks_Post_IsRefusedLikeOtherReadRoutes()
    {
        using TempFactoryRoot root = new();
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();

        using HttpResponseMessage tasks = await client.PostAsync(Route, new StringContent("{}"));
        using HttpResponseMessage inbox = await client.PostAsync("/api/inbox", new StringContent("{}"));

        Assert.Equal(HttpStatusCode.MethodNotAllowed, tasks.StatusCode);
        Assert.Equal(inbox.StatusCode, tasks.StatusCode);
        Assert.Equal(await inbox.Content.ReadAsStringAsync(), await tasks.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Tasks_GetIs200WithNoStore_AndHeadIsAllowed()
    {
        using TempFactoryRoot root = new();
        root.AddProject("Alpha", AlphaBacklog);
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();

        using HttpResponseMessage response = await client.GetAsync(Route);
        using HttpResponseMessage head = await client.SendAsync(new HttpRequestMessage(HttpMethod.Head, Route));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal(HttpStatusCode.OK, head.StatusCode);
        Assert.True(head.Headers.CacheControl?.NoStore);
    }

    [Fact]
    public async Task Tasks_HaveTheExactShapeVersionLineAndStory()
    {
        using TempFactoryRoot root = new();
        string alpha = root.AddProject("Alpha", AlphaBacklog);
        root.AddProject("Beta", BetaBacklog);
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();

        using JsonDocument json = JsonDocument.Parse(await client.GetStringAsync(Route));

        Assert.Equal(["ready", "waiting", "unreadable"], json.RootElement.EnumerateObject().Select(p => p.Name).ToArray());
        JsonElement[] all = [.. json.RootElement.GetProperty("ready").EnumerateArray(),
            .. json.RootElement.GetProperty("waiting").EnumerateArray()];
        foreach (JsonElement item in all)
        {
            Assert.Equal(
                ["project", "version", "line", "number", "title", "rest", "story", "after"],
                item.EnumerateObject().Select(p => p.Name).ToArray());
        }

        JsonElement first = json.RootElement.GetProperty("ready")[0];
        Assert.Equal("Alpha", first.GetProperty("project").GetString());
        Assert.Equal(Sha(File.ReadAllBytes(Path.Combine(alpha, "backlog.md"))), first.GetProperty("version").GetString());
        Assert.Equal(2, first.GetProperty("line").GetInt32());
        Assert.Equal(2, first.GetProperty("number").GetInt32());
        Assert.Equal("Ready plain", first.GetProperty("title").GetString());
        Assert.Equal("→ 7-x", first.GetProperty("rest").GetString());
        Assert.True(first.GetProperty("story").GetBoolean());
        Assert.Empty(first.GetProperty("after").EnumerateArray());

        JsonElement old = all.Single(item => item.GetProperty("line").GetInt32() == 7);
        Assert.Equal(JsonValueKind.Null, old.GetProperty("number").ValueKind);
        Assert.Equal("Old hand", old.GetProperty("title").GetString());
        Assert.Equal("no number", old.GetProperty("rest").GetString());
        Assert.False(old.GetProperty("story").GetBoolean());
        Assert.Empty(json.RootElement.GetProperty("unreadable").EnumerateArray());
    }

    [Fact]
    public async Task Tasks_AreSplitIntoReadyAndWaitingInProjectThenFileOrder()
    {
        using TempFactoryRoot root = new();
        root.AddProject("Beta", BetaBacklog);
        root.AddProject("Alpha", AlphaBacklog);
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();

        using JsonDocument json = JsonDocument.Parse(await client.GetStringAsync(Route));

        Assert.Equal(
            ["Alpha:2", "Alpha:3", "Alpha:7", "Beta:3"],
            Keys(json.RootElement.GetProperty("ready")));
        Assert.Equal(["Alpha:4", "Alpha:8"], Keys(json.RootElement.GetProperty("waiting")));
    }

    [Fact]
    public async Task Tasks_WaitingListsExactlyTheUnmetDependenciesWithTheirProjects()
    {
        using TempFactoryRoot root = new();
        root.AddProject("Alpha", AlphaBacklog);
        root.AddProject("Beta", BetaBacklog);
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();

        using JsonDocument json = JsonDocument.Parse(await client.GetStringAsync(Route));

        JsonElement[] waiting = json.RootElement.GetProperty("waiting").EnumerateArray().ToArray();
        // #1 is done and left out; #9 (missing number), Beta#6 (open) and Nope#1 (missing project) are unmet.
        Assert.Equal(["Alpha#9", "Beta#6", "Nope#1"], After(waiting[0]));
        Assert.Equal(["Alpha#6"], After(waiting[1]));
        foreach (JsonElement dependency in waiting[0].GetProperty("after").EnumerateArray())
        {
            Assert.Equal(["project", "number"], dependency.EnumerateObject().Select(p => p.Name).ToArray());
        }
    }

    [Fact]
    public async Task Tasks_ADependencyOnAnUnreadableProjectIsUnmet_AndTheProjectIsOnlyNamed()
    {
        using TempFactoryRoot root = new();
        root.AddProject("Alpha", "- [h] [#1](backlog/001-a.md) Needs linked (after: Linked#1)\n- [?] Not a task: no\n");
        string outside = Path.Combine(root.Path, ".outside");
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "backlog.md"), "- [x] [#1](backlog/001-s.md) Secret done\n- [h] Secret: task\n");
        string linked = root.AddProject("Linked", null);
        File.CreateSymbolicLink(Path.Combine(linked, "backlog.md"), Path.Combine(outside, "backlog.md"));
        root.AddProject("Missing", null);
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();

        using JsonDocument json = JsonDocument.Parse(await client.GetStringAsync(Route));

        string[] unreadable = json.RootElement.GetProperty("unreadable").EnumerateArray()
            .Select(name => name.GetString()!)
            .ToArray();
        Assert.Equal(["Linked", "Missing"], unreadable);
        Assert.Empty(json.RootElement.GetProperty("ready").EnumerateArray());
        JsonElement item = Assert.Single(json.RootElement.GetProperty("waiting").EnumerateArray());
        Assert.Equal(["Linked#1"], After(item));
        Assert.DoesNotContain("Secret", json.RootElement.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Tasks_FollowADoneChange_TheWaitingItemBecomesReady()
    {
        using TempFactoryRoot root = new();
        string alpha = root.AddProject("Alpha", AlphaBacklog);
        root.AddProject("Beta", BetaBacklog);
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();
        File.WriteAllText(
            Path.Combine(alpha, "backlog.md"),
            AlphaBacklog.Replace("- [ ] [#6]", "- [x] [#6]", StringComparison.Ordinal));

        using JsonDocument json = JsonDocument.Parse(await client.GetStringAsync(Route));

        Assert.Contains("Alpha:8", Keys(json.RootElement.GetProperty("ready")));
        Assert.Equal(["Alpha:4"], Keys(json.RootElement.GetProperty("waiting")));
    }

    [Fact]
    public async Task Tasks_ProjectNamesInDependenciesMatchIgnoringCase()
    {
        using TempFactoryRoot root = new();
        root.AddProject(
            "Alpha",
            "- [h] [#1](backlog/001-a.md) Met (after: times#5)\n"
            + "- [h] [#2](backlog/002-a.md) Unmet (after: TIMES#6)\n");
        root.AddProject("Times", "- [x] [#5](backlog/005-t.md) Five\n- [ ] [#6](backlog/006-t.md) Six\n");
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();

        using JsonDocument json = JsonDocument.Parse(await client.GetStringAsync(Route));

        Assert.Equal(["Alpha:0"], Keys(json.RootElement.GetProperty("ready")));
        JsonElement waiting = json.RootElement.GetProperty("waiting")[0];
        Assert.Equal(["Times#6"], After(waiting));
    }

    private static string[] Keys(JsonElement items)
    {
        return items.EnumerateArray()
            .Select(item => item.GetProperty("project").GetString() + ":" + item.GetProperty("line").GetInt32())
            .ToArray();
    }

    private static string[] After(JsonElement item)
    {
        return item.GetProperty("after").EnumerateArray()
            .Select(dependency => dependency.GetProperty("project").GetString() + "#" + dependency.GetProperty("number").GetInt32())
            .ToArray();
    }

    private static string Sha(byte[] bytes)
    {
        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }
}
