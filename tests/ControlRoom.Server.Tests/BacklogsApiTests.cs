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
        using HttpClient client = factory.CreateClient();

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
            ["name", "readable", "open", "approval", "user", "done"],
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
        using HttpClient client = factory.CreateClient();

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
        using HttpClient client = factory.CreateClient();

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
        using HttpClient client = factory.CreateClient();

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
        using HttpClient client = factory.CreateClient();

        using JsonDocument json = JsonDocument.Parse(await client.GetStringAsync("/api/backlogs"));

        Assert.Equal([("Edge", "line")], Entries(Project(json, "Edge"), "open"));
    }

    [Fact]
    public async Task Backlogs_EmptyRootGivesNoProjectsAndNoStore()
    {
        using TempFactoryRoot root = new();
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateClient();

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
        using HttpClient client = factory.CreateClient();

        using HttpRequestMessage request = new(HttpMethod.Head, "/api/backlogs");
        using HttpResponseMessage response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
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
