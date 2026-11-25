using System.Net;
using System.Text.Json;

namespace ControlRoom.Server.Tests;

public sealed class BoardApiTests
{
    private static readonly string[] JobFields =
    [
        "id", "project", "title", "branch", "stage", "round", "mode", "path", "risk", "reviews", "pastRounds",
        "specReview", "specRevised", "decision", "note", "ciStatus", "tokens", "request", "started", "ended",
    ];

    [Fact]
    public async Task Board_ReturnsJobsAndTasksWithNoStore()
    {
        using TempFactoryRoot root = new();
        root.AddJob("007-board", JobSamples.FullState);
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();

        using HttpResponseMessage response = await client.GetAsync("/api/board");
        string body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.True(response.Headers.CacheControl?.NoStore);
        using JsonDocument json = JsonDocument.Parse(body);
        Assert.Equal(["jobs", "tasks"], json.RootElement.EnumerateObject().Select(property => property.Name));
        JsonElement job = Assert.Single(json.RootElement.GetProperty("jobs").EnumerateArray());
        Assert.Equal(JobFields, job.EnumerateObject().Select(property => property.Name));
        Assert.Equal("007-board", job.GetProperty("id").GetString());
        Assert.Equal("Alpha", job.GetProperty("project").GetString());
        Assert.Equal(2, job.GetProperty("round").GetInt32());
        Assert.Equal(
            ["security", "ux", "ui", "code"],
            job.GetProperty("reviews").EnumerateObject().Select(property => property.Name));
        Assert.Equal("CHANGES", job.GetProperty("reviews").GetProperty("code").GetString());
        Assert.Equal(1, job.GetProperty("pastRounds").GetArrayLength());
        Assert.True(job.GetProperty("specRevised").GetBoolean());
        Assert.Equal(JsonValueKind.Null, job.GetProperty("decision").ValueKind);
        Assert.Equal("passed", job.GetProperty("ciStatus").GetString());
        Assert.Equal(204060, job.GetProperty("tokens").GetInt64());
        foreach (string secret in new[] { "secret-sha", "secret-reason", "baseSha", "stories", "opus", root.Path })
        {
            Assert.DoesNotContain(secret, body, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Board_WrongTypedFieldsComeBackAsNullOrEmpty()
    {
        using TempFactoryRoot root = new();
        root.AddJob("1-wrong", JobSamples.WrongTypes);
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();

        using JsonDocument json = JsonDocument.Parse(await client.GetStringAsync("/api/board"));
        JsonElement job = Assert.Single(json.RootElement.GetProperty("jobs").EnumerateArray());

        Assert.Equal(JobFields, job.EnumerateObject().Select(property => property.Name));
        foreach (JsonProperty field in job.EnumerateObject())
        {
            switch (field.Name)
            {
                case "id":
                    Assert.Equal("1-wrong", field.Value.GetString());
                    break;
                case "reviews":
                    Assert.Equal(JsonValueKind.Object, field.Value.ValueKind);
                    Assert.Empty(field.Value.EnumerateObject());
                    break;
                case "pastRounds":
                    Assert.Equal(0, field.Value.GetArrayLength());
                    break;
                default:
                    Assert.True(field.Value.ValueKind == JsonValueKind.Null, field.Name);
                    break;
            }
        }
    }

    [Fact]
    public async Task Board_LeavesOutRefusedJobsWithoutFailing()
    {
        using TempFactoryRoot root = new();
        root.AddJob("1-good", JobSamples.State("Alpha", "build", 1));
        root.AddJob("Bad-Name", JobSamples.State("Alpha", "build", 1));
        root.AddJob("2-missing", null);
        root.AddJob("3-big", "{\"note\":\"" + new string('a', (int)JobReader.MaxStateBytes) + "\"}");
        root.AddJob("4-broken", "{");
        root.AddJob("5-array", "[{}]");
        string outside = Path.Combine(root.Path, ".outside");
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "state.json"), JobSamples.State("Secret", "build", 1));
        Directory.CreateSymbolicLink(Path.Combine(root.JobsPath, "6-linked"), outside);
        string linkedState = root.AddJob("7-linked-state", null);
        File.CreateSymbolicLink(Path.Combine(linkedState, "state.json"), Path.Combine(outside, "state.json"));
        if (!OperatingSystem.IsWindows())
        {
            TempFactoryRoot.MakeFifo(Path.Combine(root.AddJob("8-fifo", null), "state.json"));
        }

        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();

        using HttpResponseMessage response = await client.GetAsync("/api/board").WaitAsync(TimeSpan.FromSeconds(10));
        string body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument json = JsonDocument.Parse(body);
        Assert.Equal(
            ["1-good"],
            json.RootElement.GetProperty("jobs").EnumerateArray().Select(job => job.GetProperty("id").GetString()));
        Assert.DoesNotContain("Secret", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Board_JobsAreOrderedByFolderName()
    {
        using TempFactoryRoot root = new();
        foreach (string id in new[] { "20-b", "100-c", "3-a" })
        {
            root.AddJob(id, JobSamples.State("Alpha", "done", 1));
        }

        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();

        using JsonDocument json = JsonDocument.Parse(await client.GetStringAsync("/api/board"));

        Assert.Equal(
            ["100-c", "20-b", "3-a"],
            json.RootElement.GetProperty("jobs").EnumerateArray().Select(job => job.GetProperty("id").GetString()));
    }

    [Fact]
    public async Task Board_NoJobsFolderGivesEmptyJobs()
    {
        using TempFactoryRoot root = new();
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();

        Assert.Equal("{\"jobs\":[],\"tasks\":[]}", await client.GetStringAsync("/api/board"));
    }

    [Fact]
    public async Task Board_SymlinkedJobsFolderGivesEmptyJobs()
    {
        using TempFactoryRoot root = new();
        string real = Path.Combine(root.Path, ".real");
        Directory.CreateDirectory(Path.Combine(real, "1-a"));
        File.WriteAllText(Path.Combine(real, "1-a", "state.json"), "{}");
        Directory.CreateDirectory(Path.Combine(root.Path, "factory"));
        Directory.CreateSymbolicLink(root.JobsPath, real);
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();

        Assert.Equal("{\"jobs\":[],\"tasks\":[]}", await client.GetStringAsync("/api/board"));
    }

    [Fact]
    public async Task Board_TasksAreTheHandLinesOfEveryProjectInOrder()
    {
        using TempFactoryRoot root = new();
        root.AddProject(
            "Beta",
            "- [ ] [#1](backlog/001-a.md) Open one\n"
            + "- [h] [#2](backlog/002-b.md) Sign the app → me\n"
            + "- [h] Renew cert: before May\n"
            + "- [x] [#3](backlog/003-c.md) Done\n");
        root.AddProject("alpha", "- [h] First: in alpha\n");
        root.AddProject("Missing", null);
        root.AddProject("Oversized", new string('a', (int)BacklogReader.MaxBytes) + "\n- [h] Big: no\n");
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();

        using JsonDocument json = JsonDocument.Parse(await client.GetStringAsync("/api/board"));
        JsonElement[] tasks = json.RootElement.GetProperty("tasks").EnumerateArray().ToArray();

        Assert.Equal(3, tasks.Length);
        Assert.All(tasks, task => Assert.Equal(
            ["project", "number", "title", "rest"], task.EnumerateObject().Select(property => property.Name)));
        Assert.Equal("alpha", tasks[0].GetProperty("project").GetString());
        Assert.Equal(JsonValueKind.Null, tasks[0].GetProperty("number").ValueKind);
        Assert.Equal("First", tasks[0].GetProperty("title").GetString());
        Assert.Equal("in alpha", tasks[0].GetProperty("rest").GetString());
        Assert.Equal("Beta", tasks[1].GetProperty("project").GetString());
        Assert.Equal(2, tasks[1].GetProperty("number").GetInt32());
        Assert.Equal("Sign the app", tasks[1].GetProperty("title").GetString());
        Assert.Equal("→ me", tasks[1].GetProperty("rest").GetString());
        Assert.Equal("Renew cert", tasks[2].GetProperty("title").GetString());
    }

    [Fact]
    public async Task Board_HeadIsServedAndNothingIsWritten()
    {
        using TempFactoryRoot root = TempFactoryRoot.WithSamples();
        root.AddJob("1-a", JobSamples.FullState);
        IReadOnlyList<string> before = root.Snapshot();
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();

        using HttpRequestMessage request = new(HttpMethod.Head, "/api/board");
        using HttpResponseMessage response = await client.SendAsync(request);
        using HttpResponseMessage get = await client.GetAsync("/api/board");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        Assert.Equal(before, root.Snapshot());
    }

    [Fact]
    public async Task Board_GivesNeedsHumanJobsTheirRequestActionAndNullForEveryOtherStage()
    {
        using TempFactoryRoot root = new();
        const string Approve = "{\"action\":\"approve\",\"round\":1,\"requested\":\"2026-03-01T09:30:00Z\"}\n";
        root.AddJob("1-approve", JobSamples.State("Alpha", "needs-human", 1));
        root.AddRecord("1-approve", "human.json", Approve);
        root.AddJob("2-rework", JobSamples.State("Alpha", "needs-human", 1));
        root.AddRecord("2-rework", "human.json",
            "{\"action\":\"rework\",\"round\":1,\"note\":\"Again\",\"requested\":\"2026-03-01T09:30:00Z\"}\n");
        root.AddJob("3-none", JobSamples.State("Alpha", "needs-human", 1));
        root.AddJob("4-bad", JobSamples.State("Alpha", "needs-human", 1));
        root.AddRecord("4-bad", "human.json", "nope");
        root.AddJob("5-review", JobSamples.State("Alpha", "review", 1));
        root.AddRecord("5-review", "human.json", Approve);
        root.AddJob("6-done", JobSamples.State("Alpha", "done", 1));
        root.AddRecord("6-done", "human.json", Approve);
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();

        using JsonDocument json = JsonDocument.Parse(await client.GetStringAsync("/api/board"));
        Dictionary<string, JsonElement> jobs = json.RootElement.GetProperty("jobs").EnumerateArray()
            .ToDictionary(job => job.GetProperty("id").GetString()!, job => job.GetProperty("request"));

        Assert.Equal("approve", jobs["1-approve"].GetString());
        Assert.Equal("rework", jobs["2-rework"].GetString());
        Assert.Equal(JsonValueKind.Null, jobs["3-none"].ValueKind);
        Assert.Equal("unreadable", jobs["4-bad"].GetString());
        Assert.Equal(JsonValueKind.Null, jobs["5-review"].ValueKind);
        Assert.Equal(JsonValueKind.Null, jobs["6-done"].ValueKind);
        Assert.DoesNotContain("Again", json.RootElement.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Board_ListsUuidSuffixedJobsNextToLegacyOnesAndSkipsLookAlikes()
    {
        using TempFactoryRoot root = new();
        string suffixed = "405-auto-pause." + JobReaderTests.Uuid;
        root.AddJob("398-legacy", JobSamples.State("Alpha", "done", 1));
        root.AddJob(suffixed, JobSamples.State("Alpha", "build", 1));
        root.AddJob("406-upper." + JobReaderTests.Uuid.ToUpperInvariant(), JobSamples.State("Alpha", "build", 1));
        root.AddJob("407-short." + JobReaderTests.Uuid[..31], JobSamples.State("Alpha", "build", 1));
        root.AddJob("408-a.b." + JobReaderTests.Uuid, JobSamples.State("Alpha", "build", 1));
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();

        using JsonDocument json = JsonDocument.Parse(await client.GetStringAsync("/api/board"));

        Assert.Equal(
            ["398-legacy", suffixed],
            json.RootElement.GetProperty("jobs").EnumerateArray().Select(job => job.GetProperty("id").GetString()));
    }
}
