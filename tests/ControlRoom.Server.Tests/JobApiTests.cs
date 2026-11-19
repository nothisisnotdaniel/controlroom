using System.Net;
using System.Text.Json;

namespace ControlRoom.Server.Tests;

public sealed class JobApiTests
{
    private const string NotFound = "{\"error\":\"not found\"}";

    private const string Id = "7-board";

    [Fact]
    public async Task Job_ReturnsTheJobAndItsRecordsInTheFixedOrder()
    {
        using TempFactoryRoot root = new();
        root.AddJob(Id, JobSamples.State("Alpha", "approve", 2));
        // Written in a scrambled order: the response order comes from the fixed list.
        root.AddRecord(Id, "decision.md", "APPROVE\n");
        root.AddRecord(Id, "feedback.md", "Feedback\n");
        root.AddRecord(Id, "round-2/review-code.md", "VERDICT: PASS\n");
        root.AddRecord(Id, "round-1/review-ux.md", "VERDICT: CHANGES\n");
        root.AddRecord(Id, "round-1/review-security.md", "VERDICT: PASS\n");
        root.AddRecord(Id, "round-1/review-other.md", "not listed\n");
        root.AddRecord(Id, "round-3/review-code.md", "beyond the round\n");
        root.AddRecord(Id, "build.md", "## Round 1\n");
        root.AddRecord(Id, "findings.md", "Found\n");
        root.AddRecord(Id, "task.md", "Task\n");
        root.AddRecord(Id, "spec-review.md", "VERDICT: PASS\n");
        root.AddRecord(Id, "spec.md", "# Spec\n\n| a | b |\n|---|---|\n| 1 | 2 |\n");
        root.AddRecord(Id, "notes.md", "not listed\n");
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();

        using HttpResponseMessage response = await client.GetAsync("/api/job?id=" + Id);
        string body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        using JsonDocument json = JsonDocument.Parse(body);
        Assert.Equal(["job", "records", "request"], json.RootElement.EnumerateObject().Select(property => property.Name));
        Assert.Equal(Id, json.RootElement.GetProperty("job").GetProperty("id").GetString());
        JsonElement[] records = json.RootElement.GetProperty("records").EnumerateArray().ToArray();
        Assert.Equal(
            [
                "spec.md", "spec-review.md", "task.md", "findings.md", "build.md", "round-1/review-security.md",
                "round-1/review-ux.md", "round-2/review-code.md", "feedback.md", "decision.md",
            ],
            records.Select(record => record.GetProperty("name").GetString()));
        Assert.Equal(
            [
                "spec", "spec check", "task", "findings", "build", "R1 security", "R1 ux", "R2 code", "feedback",
                "decision",
            ],
            records.Select(record => record.GetProperty("label").GetString()));
        Assert.All(records, record => Assert.Equal(
            ["name", "label", "readable", "blocks"], record.EnumerateObject().Select(property => property.Name)));
        Assert.All(records, record => Assert.True(record.GetProperty("readable").GetBoolean()));
        JsonElement[] specBlocks = records[0].GetProperty("blocks").EnumerateArray().ToArray();
        Assert.Equal(["heading", "table"], specBlocks.Select(block => block.GetProperty("kind").GetString()));
        Assert.DoesNotContain("not listed", body, StringComparison.Ordinal);
        Assert.DoesNotContain("beyond the round", body, StringComparison.Ordinal);
        Assert.DoesNotContain(root.Path, body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Job_ReturnsEveryStateField()
    {
        using TempFactoryRoot root = new();
        root.AddJob(Id, JobSamples.FullState.Replace("\"decision\": null", "\"decision\": \"ESCALATE\"",
            StringComparison.Ordinal));
        root.AddJob("8-wrong", JobSamples.WrongTypes);
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();

        using JsonDocument full = JsonDocument.Parse(await client.GetStringAsync("/api/job?id=" + Id));
        JsonElement job = full.RootElement.GetProperty("job");

        Assert.Equal("review", job.GetProperty("stage").GetString());
        Assert.Equal(2, job.GetProperty("round").GetInt32());
        Assert.Equal("quality", job.GetProperty("mode").GetString());
        Assert.Equal("full", job.GetProperty("path").GetString());
        Assert.Equal("high", job.GetProperty("risk").GetString());
        Assert.Equal(
            [("security", "PASS"), ("ux", "pending"), ("ui", "skipped"), ("code", "CHANGES")],
            Pairs(job.GetProperty("reviews")));
        JsonElement past = Assert.Single(job.GetProperty("pastRounds").EnumerateArray());
        Assert.Equal(
            [("security", "CHANGES"), ("ux", "PASS"), ("ui", "skipped"), ("code", "PASS")], Pairs(past));
        Assert.Equal("PASS", job.GetProperty("specReview").GetString());
        Assert.True(job.GetProperty("specRevised").GetBoolean());
        Assert.Equal("ESCALATE", job.GetProperty("decision").GetString());
        Assert.Equal("waiting on ci", job.GetProperty("note").GetString());
        Assert.Equal("passed", job.GetProperty("ciStatus").GetString());

        using JsonDocument wrong = JsonDocument.Parse(await client.GetStringAsync("/api/job?id=8-wrong"));
        JsonElement broken = wrong.RootElement.GetProperty("job");

        foreach (string name in new[]
                 {
                     "stage", "round", "mode", "path", "risk", "specReview", "specRevised", "decision", "note",
                     "ciStatus",
                 })
        {
            Assert.True(broken.GetProperty(name).ValueKind == JsonValueKind.Null, name);
        }

        Assert.Equal(JsonValueKind.Object, broken.GetProperty("reviews").ValueKind);
        Assert.Empty(broken.GetProperty("reviews").EnumerateObject());
        Assert.Equal(0, broken.GetProperty("pastRounds").GetArrayLength());
    }

    private static (string, string?)[] Pairs(JsonElement map)
    {
        return map.EnumerateObject().Select(property => (property.Name, property.Value.GetString())).ToArray();
    }

    [Fact]
    public async Task Job_ListsReviewsOfAtMostTwentyRounds()
    {
        using TempFactoryRoot root = new();
        root.AddJob(Id, JobSamples.State("Alpha", "review", 25));
        root.AddRecord(Id, "round-20/review-ui.md", "twenty\n");
        root.AddRecord(Id, "round-21/review-ui.md", "twenty-one\n");
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();

        using JsonDocument json = JsonDocument.Parse(await client.GetStringAsync("/api/job?id=" + Id));

        Assert.Equal(
            ["R20 ui"],
            json.RootElement.GetProperty("records").EnumerateArray()
                .Select(record => record.GetProperty("label").GetString()));
    }

    [Fact]
    public async Task Job_NoRoundListsNoReviews()
    {
        using TempFactoryRoot root = new();
        root.AddJob(Id, "{\"stage\":\"spec\"}");
        root.AddRecord(Id, "round-1/review-ui.md", "early\n");
        root.AddRecord(Id, "spec.md", "Spec\n");
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();

        using JsonDocument json = JsonDocument.Parse(await client.GetStringAsync("/api/job?id=" + Id));

        Assert.Equal(
            ["spec"],
            json.RootElement.GetProperty("records").EnumerateArray()
                .Select(record => record.GetProperty("label").GetString()));
    }

    [Fact]
    public async Task Job_RefusedRecordsAreListedAsNotReadable()
    {
        using TempFactoryRoot root = new();
        string job = root.AddJob(Id, JobSamples.State("Alpha", "review", 2));
        string outside = Path.Combine(root.Path, ".outside");
        Directory.CreateDirectory(outside);
        string secret = Path.Combine(outside, "secret.md");
        File.WriteAllText(secret, "secret text\n");
        File.WriteAllText(Path.Combine(outside, "review-code.md"), "secret review\n");
        File.CreateSymbolicLink(Path.Combine(job, "spec.md"), secret);
        root.AddRecord(Id, "build.md", "## " + new string('a', (int)JobEndpoint.MaxRecordBytes));
        Directory.CreateDirectory(Path.Combine(job, "task.md"));
        if (!OperatingSystem.IsWindows())
        {
            TempFactoryRoot.MakeFifo(Path.Combine(job, "findings.md"));
        }

        root.AddRecord(Id, "round-1/review-code.md", "VERDICT: PASS\n");
        Directory.CreateSymbolicLink(Path.Combine(job, "round-2"), outside);
        root.AddRecord(Id, "decision.md", "APPROVE\n");
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();

        using HttpResponseMessage response = await client.GetAsync("/api/job?id=" + Id)
            .WaitAsync(TimeSpan.FromSeconds(10));
        string body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument json = JsonDocument.Parse(body);
        Dictionary<string, JsonElement> records = json.RootElement.GetProperty("records").EnumerateArray()
            .ToDictionary(record => record.GetProperty("label").GetString()!);
        List<string> refused = ["spec", "build", "task", "R2 code"];
        if (!OperatingSystem.IsWindows())
        {
            refused.Add("findings");
        }

        foreach (string label in refused)
        {
            Assert.False(records[label].GetProperty("readable").GetBoolean(), label);
            Assert.Equal(0, records[label].GetProperty("blocks").GetArrayLength());
        }

        Assert.True(records["R1 code"].GetProperty("readable").GetBoolean());
        Assert.True(records["decision"].GetProperty("readable").GetBoolean());
        Assert.DoesNotContain("secret", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Job_RecordExactlyAtTheCapIsRead()
    {
        using TempFactoryRoot root = new();
        root.AddJob(Id, "{}");
        root.AddRecord(Id, "build.md", "x" + new string('a', (int)JobEndpoint.MaxRecordBytes - 1));
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();

        using JsonDocument json = JsonDocument.Parse(await client.GetStringAsync("/api/job?id=" + Id));

        Assert.True(json.RootElement.GetProperty("records")[0].GetProperty("readable").GetBoolean());
    }

    [Theory]
    [InlineData("/api/job")]
    [InlineData("/api/job?id=")]
    [InlineData("/api/job?id=7-board&id=7-board")]
    [InlineData("/api/job?id=..")]
    [InlineData("/api/job?id=a/b")]
    [InlineData("/api/job?id=7-board/..")]
    [InlineData("/api/job?id=..%2F7-board")]
    [InlineData("/api/job?id=7-board%2Fstate.json")]
    [InlineData("/api/job?id=7-BOARD")]
    [InlineData("/api/job?id=7-board%20")]
    [InlineData("/api/job?id=%207-board")]
    [InlineData("/api/job?id=8-none")]
    [InlineData("/api/job?id=9-linked")]
    [InlineData("/api/job?id=10-broken")]
    [InlineData("/api/job?id=11-missing")]
    [InlineData("/api/job?id=Bad_Name")]
    [InlineData("/api/job?id=jobs")]
    [InlineData("/api/job?id=.")]
    public async Task Job_BadIdsGiveTheFixedNotFound(string url)
    {
        using TempFactoryRoot root = new();
        root.AddJob(Id, JobSamples.State("Alpha", "build", 1));
        root.AddRecord(Id, "spec.md", "secret spec\n");
        root.AddJob("Bad_Name", JobSamples.State("Alpha", "build", 1));
        string outside = Path.Combine(root.Path, ".outside");
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "state.json"), "{}");
        File.WriteAllText(Path.Combine(outside, "spec.md"), "secret linked\n");
        Directory.CreateSymbolicLink(Path.Combine(root.JobsPath, "9-linked"), outside);
        root.AddJob("10-broken", "{");
        root.AddJob("11-missing", null);
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();

        using HttpResponseMessage response = await client.GetAsync(url);
        string body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(NotFound, body);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Job_UnreadableStateGivesTheFixedNotFound()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using TempFactoryRoot root = new();
        string job = root.AddJob(Id, JobSamples.State("Alpha", "build", 1));
        root.Lock(Path.Combine(job, "state.json"));
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();

        using HttpResponseMessage response = await client.GetAsync("/api/job?id=" + Id);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(NotFound, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Job_HeadIsServedAndNothingIsWritten()
    {
        using TempFactoryRoot root = new();
        root.AddJob(Id, JobSamples.State("Alpha", "review", 1));
        root.AddRecord(Id, "spec.md", "Spec\n");
        root.AddRecord(Id, "round-1/review-ux.md", "VERDICT: PASS\n");
        IReadOnlyList<string> before = root.Snapshot();
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();

        using HttpRequestMessage request = new(HttpMethod.Head, "/api/job?id=" + Id);
        using HttpResponseMessage response = await client.SendAsync(request);
        using HttpResponseMessage get = await client.GetAsync("/api/job?id=" + Id);
        using HttpResponseMessage missing = await client.GetAsync("/api/job?id=9-none");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal(before, root.Snapshot());
    }

    private const string ApproveRequest =
        "{\"action\":\"approve\",\"round\":2,\"requested\":\"2026-03-01T09:30:00Z\"}\n";

    [Fact]
    public async Task Job_RequestIsNullWithoutAFile()
    {
        using TempFactoryRoot root = new();
        root.AddJob(Id, JobSamples.State("Alpha", "needs-human", 2));
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();

        using JsonDocument json = JsonDocument.Parse(await client.GetStringAsync("/api/job?id=" + Id));

        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("request").ValueKind);
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("job").GetProperty("request").ValueKind);
    }

    [Fact]
    public async Task Job_RequestIsTheParsedFile()
    {
        using TempFactoryRoot root = new();
        root.AddJob(Id, JobSamples.State("Alpha", "needs-human", 2));
        root.AddRecord(Id, "human.json",
            "{\"action\":\"rework\",\"round\":2,\"note\":\"Smaller <b>please</b>\",\"requested\":\"2026-03-01T09:30:00Z\"}\n");
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();

        using JsonDocument json = JsonDocument.Parse(await client.GetStringAsync("/api/job?id=" + Id));
        JsonElement request = json.RootElement.GetProperty("request");

        Assert.Equal(["action", "round", "note", "requested"], request.EnumerateObject().Select(property => property.Name));
        Assert.Equal("rework", request.GetProperty("action").GetString());
        Assert.Equal(2, request.GetProperty("round").GetInt32());
        Assert.Equal("Smaller <b>please</b>", request.GetProperty("note").GetString());
        Assert.Equal("2026-03-01T09:30:00Z", request.GetProperty("requested").GetString());
        Assert.Equal("rework", json.RootElement.GetProperty("job").GetProperty("request").GetString());
    }

    [Fact]
    public async Task Job_ApproveRequestHasNoNote()
    {
        using TempFactoryRoot root = new();
        root.AddJob(Id, JobSamples.State("Alpha", "needs-human", 2));
        root.AddRecord(Id, "human.json", ApproveRequest);
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();

        using JsonDocument json = JsonDocument.Parse(await client.GetStringAsync("/api/job?id=" + Id));

        Assert.Equal(["action", "round", "requested"],
            json.RootElement.GetProperty("request").EnumerateObject().Select(property => property.Name));
    }

    [Theory]
    [InlineData("bad")]
    [InlineData("big")]
    [InlineData("link")]
    [InlineData("dangling")]
    [InlineData("folder")]
    public async Task Job_RequestIsUnreadableForABadSymlinkedOrOversizedFile(string kind)
    {
        using TempFactoryRoot root = new();
        string folder = root.AddJob(Id, JobSamples.State("Alpha", "needs-human", 2));
        string file = Path.Combine(folder, "human.json");
        string target = Path.Combine(root.Path, ".outside", "target.json");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        switch (kind)
        {
            case "bad":
                File.WriteAllText(file, "{\"action\":\"approve\"}\n");
                break;
            case "big":
                File.WriteAllText(file, ApproveRequest.TrimEnd('\n') + new string(' ', 5000));
                break;
            case "link":
                File.WriteAllText(target, ApproveRequest);
                File.CreateSymbolicLink(file, target);
                break;
            case "dangling":
                File.CreateSymbolicLink(file, target);
                break;
            default:
                Directory.CreateDirectory(file);
                break;
        }

        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();

        using JsonDocument json = JsonDocument.Parse(await client.GetStringAsync("/api/job?id=" + Id));

        Assert.Equal("{\"action\":\"unreadable\"}", json.RootElement.GetProperty("request").GetRawText());
        Assert.Equal("unreadable", json.RootElement.GetProperty("job").GetProperty("request").GetString());
    }
}
