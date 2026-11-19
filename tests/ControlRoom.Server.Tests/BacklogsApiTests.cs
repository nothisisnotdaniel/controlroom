using System.Net;
using System.Text.Json;

namespace ControlRoom.Server.Tests;

public sealed class BacklogsApiTests
{
    [Fact]
    public async Task Backlogs_ReturnsEveryProjectWithGroupedLines()
    {
        using TempFactoryRoot root = TempFactoryRoot.WithSamples();
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();

        using HttpResponseMessage response = await client.GetAsync("/api/backlogs");
        string body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        using JsonDocument json = JsonDocument.Parse(body);
        JsonElement[] projects = json.RootElement.GetProperty("projects").EnumerateArray().ToArray();
        Assert.Equal(
            ["emptyOne", "LinkedBacklog", "Missing", "Normal", "Oversized"],
            projects.Select(project => project.GetProperty("name").GetString()));

        JsonElement normal = projects[3];
        Assert.Equal(
            ["name", "readable", "open", "approval", "user", "done", "version", "next", "stopped", "blocked"],
            normal.EnumerateObject().Select(property => property.Name));
        Assert.True(normal.GetProperty("readable").GetBoolean());
        Assert.Equal(
            [("First open", "do `this` → 125-x"), ("Second open without rest", "")],
            Entries(normal, "open"));
        Assert.Equal([("Proposal", "wait for me")], Entries(normal, "approval"));
        Assert.Equal([("Hand task", "for you")], Entries(normal, "user"));
        Assert.Equal([("Done one", "finished"), ("Done two", "a: b")], Entries(normal, "done"));

        JsonElement empty = projects[0];
        Assert.True(empty.GetProperty("readable").GetBoolean());
        Assert.All(["open", "approval", "user", "done"], group => Assert.Empty(Entries(empty, group)));
        Assert.DoesNotContain("Secret", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Hidden", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Factory", body, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Missing")]
    [InlineData("Oversized")]
    [InlineData("LinkedBacklog")]
    public async Task Backlogs_UnreadableBacklogIsFlaggedWithEmptyGroups(string name)
    {
        using TempFactoryRoot root = TempFactoryRoot.WithSamples();
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();

        string body = await client.GetStringAsync("/api/backlogs");

        using JsonDocument json = JsonDocument.Parse(body);
        JsonElement project = Project(json, name);
        Assert.False(project.GetProperty("readable").GetBoolean());
        Assert.All(["open", "approval", "user", "done"], group => Assert.Empty(Entries(project, group)));
        Assert.True(Project(json, "Normal").GetProperty("readable").GetBoolean());
        Assert.DoesNotContain("xception", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Backlogs_PermissionErrorIsFlaggedWithoutExceptionText()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using TempFactoryRoot root = new();
        string folder = root.AddProject("Locked", "- [ ] Locked: line\n");
        root.AddProject("Open", "- [ ] Fine: line\n");
        root.Lock(Path.Combine(folder, "backlog.md"));
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();

        string body = await client.GetStringAsync("/api/backlogs");

        using JsonDocument json = JsonDocument.Parse(body);
        Assert.False(Project(json, "Locked").GetProperty("readable").GetBoolean());
        Assert.Equal([("Fine", "line")], Entries(Project(json, "Open"), "open"));
        Assert.DoesNotContain("denied", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Backlogs_FifoBacklogIsRefusedWithoutHanging()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using TempFactoryRoot root = new();
        string folder = root.AddProject("Fifo", null);
        root.AddProject("Open", "- [ ] Fine: line\n");
        TempFactoryRoot.MakeFifo(Path.Combine(folder, "backlog.md"));
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();

        string body = await client.GetStringAsync("/api/backlogs").WaitAsync(TimeSpan.FromSeconds(10));

        using JsonDocument json = JsonDocument.Parse(body);
        JsonElement fifo = Project(json, "Fifo");
        Assert.False(fifo.GetProperty("readable").GetBoolean());
        Assert.All(["open", "approval", "user", "done"], group => Assert.Empty(Entries(fifo, group)));
        Assert.Equal([("Fine", "line")], Entries(Project(json, "Open"), "open"));
    }

    [Fact]
    public async Task Backlogs_BacklogExactlyAtTheLimitIsRead()
    {
        using TempFactoryRoot root = new();
        string line = "- [ ] Edge: line\n";
        root.AddProject("Edge", line + new string('#', (int)BacklogReader.MaxBytes - line.Length));
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();

        using JsonDocument json = JsonDocument.Parse(await client.GetStringAsync("/api/backlogs"));

        Assert.Equal([("Edge", "line")], Entries(Project(json, "Edge"), "open"));
    }

    [Fact]
    public async Task Backlogs_EmptyRootGivesNoProjectsAndNoStore()
    {
        using TempFactoryRoot root = new();
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();

        using HttpResponseMessage response = await client.GetAsync("/api/backlogs");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("{\"projects\":[]}", await response.Content.ReadAsStringAsync());
        Assert.True(response.Headers.CacheControl?.NoStore);
    }

    [Fact]
    public async Task Backlogs_HeadIsServed()
    {
        using TempFactoryRoot root = new();
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();

        using HttpRequestMessage request = new(HttpMethod.Head, "/api/backlogs");
        using HttpResponseMessage response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("- [ ] [#5](backlog/005-t.md) Times five\n", true)]
    [InlineData("- [x] [#5](backlog/005-t.md) Times five → 1-x\n", false)]
    [InlineData("- [X] [#5](backlog/005-t.md) Times five\n", false)]
    [InlineData("- [x] Times five: old format, no number\n", true)]
    public async Task Backlogs_EntryIsWaitingWhileADependencyIsOpen(string timesBacklog, bool waiting)
    {
        using TempFactoryRoot root = new();
        root.AddProject(
            "Alpha",
            "- [ ] [#4](backlog/004-a.md) Four (after: #3; Times#5)\n"
            + "- [x] [#3](backlog/003-a.md) Three → 1-y\n");
        root.AddProject("Times", timesBacklog);
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();

        using JsonDocument json = JsonDocument.Parse(await client.GetStringAsync("/api/backlogs"));

        JsonElement four = Project(json, "Alpha").GetProperty("open")[0];
        Assert.Equal(4, four.GetProperty("number").GetInt32());
        Assert.Equal(" ", four.GetProperty("mark").GetString());
        Assert.Equal("backlog/004-a.md", four.GetProperty("story").GetString());
        Assert.Equal(
            [(null, 3), ("Times", 5)],
            four.GetProperty("after").EnumerateArray()
                .Select(item => (item.GetProperty("project").GetString(), item.GetProperty("number").GetInt32())));
        Assert.Equal(waiting, four.GetProperty("waiting").GetBoolean());
        JsonElement three = Project(json, "Alpha").GetProperty("done")[0];
        Assert.False(three.GetProperty("waiting").GetBoolean());
        Assert.Equal("x", three.GetProperty("mark").GetString());
    }

    [Theory]
    [InlineData("(after: Nope#1)", true)]
    [InlineData("(after: #99)", true)]
    [InlineData("(after: alpha#3)", false)]
    [InlineData("(after: alpha#2)", true)]
    [InlineData("(after: Alpha#3)", false)]
    [InlineData("(after: #3)", false)]
    [InlineData("", false)]
    public async Task Backlogs_UnknownProjectOrNumberCountsAsNotDone(string after, bool waiting)
    {
        using TempFactoryRoot root = new();
        root.AddProject(
            "Alpha",
            "- [?] [#4](backlog/004-a.md) Four " + after + "\n"
            + "- [x] [#3](backlog/003-a.md) Three\n"
            + "- [ ] Old line: no number\n");
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();

        using JsonDocument json = JsonDocument.Parse(await client.GetStringAsync("/api/backlogs"));

        JsonElement alpha = Project(json, "Alpha");
        Assert.Equal(waiting, alpha.GetProperty("approval")[0].GetProperty("waiting").GetBoolean());
        JsonElement old = alpha.GetProperty("open")[0];
        Assert.Equal(JsonValueKind.Null, old.GetProperty("number").ValueKind);
        Assert.Empty(old.GetProperty("after").EnumerateArray());
        Assert.False(old.GetProperty("waiting").GetBoolean());
    }

    [Fact]
    public async Task Backlogs_DependencyOnAnUnreadableBacklogIsWaiting()
    {
        using TempFactoryRoot root = new();
        root.AddProject("Alpha", "- [ ] [#4](backlog/004-a.md) Four (after: Missing#1)\n");
        root.AddProject("Missing", null);
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();

        using JsonDocument json = JsonDocument.Parse(await client.GetStringAsync("/api/backlogs"));

        Assert.True(Project(json, "Alpha").GetProperty("open")[0].GetProperty("waiting").GetBoolean());
    }

    [Fact]
    public async Task Backlogs_EmptyTitleIsStillReturnedAsEmpty()
    {
        using TempFactoryRoot root = new();
        root.AddProject("Alpha", "- [ ] [#1](backlog/001-a.md) \n- [ ] [#2](backlog/002-b.md) Named\n");
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();

        string body = await client.GetStringAsync("/api/backlogs");

        using JsonDocument json = JsonDocument.Parse(body);
        Assert.Equal(["", "Named"], Entries(Project(json, "Alpha"), "open").Select(entry => entry.Item1));
    }

    [Fact]
    public async Task Backlogs_PlanHasNextAndBlockedAcrossProjects()
    {
        using TempFactoryRoot root = new();
        root.AddProject(
            "Alpha",
            "- [ ] Old line: no number\n"
            + "- [?] [#1](backlog/001-a.md) Proposal\n"
            + "- [h] [#2](backlog/002-a.md) Hand\n"
            + "- [ ] [#3](backlog/003-a.md) Blocked first (after: #2; Times#6)\n"
            + "- [ ] [#4](backlog/004-a.md) Spike: try it (after: times#5)\n"
            + "- [ ] [#5](backlog/005-a.md) Later\n");
        root.AddProject("Times", "- [x] [#5](backlog/005-t.md) Five\n- [ ] [#6](backlog/006-t.md) Six\n");
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();

        using JsonDocument json = JsonDocument.Parse(await client.GetStringAsync("/api/backlogs"));

        JsonElement alpha = Project(json, "Alpha");
        JsonElement next = alpha.GetProperty("next");
        Assert.Equal(["number", "title", "story", "spike"], next.EnumerateObject().Select(p => p.Name));
        Assert.Equal(4, next.GetProperty("number").GetInt32());
        Assert.Equal("Spike: try it", next.GetProperty("title").GetString());
        Assert.Equal("backlog/004-a.md", next.GetProperty("story").GetString());
        Assert.True(next.GetProperty("spike").GetBoolean());
        Assert.False(alpha.GetProperty("stopped").GetBoolean());
        JsonElement blocked = Assert.Single(alpha.GetProperty("blocked").EnumerateArray());
        Assert.Equal(["number", "title", "story", "after"], blocked.EnumerateObject().Select(p => p.Name));
        Assert.Equal((3, "Blocked first"), (blocked.GetProperty("number").GetInt32(), blocked.GetProperty("title").GetString()));
        Assert.Equal(["Alpha#2-", "Times#6-"], Dependencies(blocked));

        JsonElement times = Project(json, "Times");
        Assert.Equal(6, times.GetProperty("next").GetProperty("number").GetInt32());
        Assert.False(times.GetProperty("next").GetProperty("spike").GetBoolean());
        Assert.Empty(times.GetProperty("blocked").EnumerateArray());
    }

    [Fact]
    public async Task Backlogs_MissingDependenciesStopTheProject()
    {
        using TempFactoryRoot root = new();
        root.AddProject(
            "Alpha",
            "- [ ] [#1](backlog/001-a.md) One (after: Nope#1; Times#99; Locked#1; factory#3)\n"
            + "- [ ] [#2](backlog/002-a.md) Two\n");
        root.AddProject("Beta", "- [ ] [#1](backlog/001-b.md) One (after: Locked#1; factory#3)\n");
        root.AddProject("Locked", null);
        root.AddProject("Times", "- [x] [#5](backlog/005-t.md) Five\n");
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();

        using JsonDocument json = JsonDocument.Parse(await client.GetStringAsync("/api/backlogs"));

        JsonElement alpha = Project(json, "Alpha");
        Assert.True(alpha.GetProperty("stopped").GetBoolean());
        Assert.Equal(2, alpha.GetProperty("next").GetProperty("number").GetInt32());
        Assert.Equal(
            ["Nope#1!", "Times#99!", "Locked#1-", "factory#3-"],
            Dependencies(alpha.GetProperty("blocked")[0]));
        JsonElement beta = Project(json, "Beta");
        Assert.False(beta.GetProperty("stopped").GetBoolean());
        Assert.Equal(JsonValueKind.Null, beta.GetProperty("next").ValueKind);
        Assert.Equal(["Locked#1-", "factory#3-"], Dependencies(beta.GetProperty("blocked")[0]));
        JsonElement locked = Project(json, "Locked");
        Assert.Equal(JsonValueKind.Null, locked.GetProperty("next").ValueKind);
        Assert.False(locked.GetProperty("stopped").GetBoolean());
        Assert.Empty(locked.GetProperty("blocked").EnumerateArray());
    }

    [Fact]
    public async Task Backlogs_ADependencyIsMetOnceTheNamedLineIsDone()
    {
        using TempFactoryRoot root = new();
        root.AddProject("Alpha", "- [ ] [#1](backlog/001-a.md) One (after: Times#6)\n");
        string times = root.AddProject("Times", "- [ ] [#6](backlog/006-t.md) Six\n");
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();

        using JsonDocument before = JsonDocument.Parse(await client.GetStringAsync("/api/backlogs"));
        File.WriteAllText(Path.Combine(times, "backlog.md"), "- [x] [#6](backlog/006-t.md) Six\n");
        using JsonDocument after = JsonDocument.Parse(await client.GetStringAsync("/api/backlogs"));

        Assert.Equal(JsonValueKind.Null, Project(before, "Alpha").GetProperty("next").ValueKind);
        Assert.Single(Project(before, "Alpha").GetProperty("blocked").EnumerateArray());
        Assert.Equal(1, Project(after, "Alpha").GetProperty("next").GetProperty("number").GetInt32());
        Assert.Empty(Project(after, "Alpha").GetProperty("blocked").EnumerateArray());
    }

    private static string[] Dependencies(JsonElement blocked)
    {
        return blocked.GetProperty("after").EnumerateArray()
            .Select(dependency => dependency.GetProperty("project").GetString() + "#"
                + dependency.GetProperty("number").GetInt32()
                + (dependency.GetProperty("missing").GetBoolean() ? "!" : "-"))
            .ToArray();
    }

    private static JsonElement Project(JsonDocument json, string name)
    {
        return json.RootElement.GetProperty("projects").EnumerateArray()
            .Single(project => project.GetProperty("name").GetString() == name);
    }

    private static List<(string?, string?)> Entries(JsonElement project, string group)
    {
        return project.GetProperty(group).EnumerateArray()
            .Select(entry => (entry.GetProperty("title").GetString(), entry.GetProperty("rest").GetString()))
            .ToList();
    }
}
