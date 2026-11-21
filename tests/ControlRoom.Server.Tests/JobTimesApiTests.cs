using System.Net;
using System.Text.Json;

namespace ControlRoom.Server.Tests;

/// <summary><c>started</c>, <c>ended</c> and per-agent <c>seconds</c> from <c>state.json</c> on the board and the job
/// page (#136). Each test has its own <see cref="TempFactoryRoot"/>; no clock is read.</summary>
public sealed class JobTimesApiTests
{
    private const string Running =
        """
        {"project":"Alpha","title":"T","stage":"build","round":1,"started":"2026-11-21T15:32:19Z",
         "tokens":{"builder":{"runs":1,"input":1000,"cacheRead":2000,"output":300,"seconds":754}}}
        """;

    private const string Finished =
        """
        {"project":"Alpha","title":"T","stage":"done","round":1,"started":"2026-11-21T15:32:19Z",
         "ended":"2026-11-21T16:44:19Z",
         "tokens":{"spec-writer":{"input":10,"cacheRead":20,"output":30,"seconds":240},
                   "builder":{"input":1000,"cacheRead":2000,"output":300},
                   "broken":"x",
                   "approver":{"input":5,"seconds":4320}}}
        """;

    private const string Old =
        """
        {"project":"Alpha","title":"T","stage":"done","round":1,
         "tokens":{"builder":{"runs":1,"input":1000,"cacheRead":2000,"output":300}}}
        """;

    [Fact]
    public async Task Job_RunningJob_HasStartedNoEndedAndAgentSeconds()
    {
        using JsonDocument json = await Job(Running);

        JsonElement job = json.RootElement.GetProperty("job");
        Assert.Equal("2026-11-21T15:32:19Z", job.GetProperty("started").GetString());
        Assert.Equal(JsonValueKind.Null, job.GetProperty("ended").ValueKind);
        JsonElement agent = Assert.Single(json.RootElement.GetProperty("agents").EnumerateArray());
        Assert.Equal(["name", "tokens", "seconds"], agent.EnumerateObject().Select(property => property.Name));
        Assert.Equal("builder", agent.GetProperty("name").GetString());
        Assert.Equal(3300, agent.GetProperty("tokens").GetInt64());
        Assert.Equal(754, agent.GetProperty("seconds").GetInt64());
    }

    [Fact]
    public async Task Job_FinishedJob_HasBothTimesAndAgentsInFileOrder_AgentWithoutSecondsIsNull()
    {
        using JsonDocument json = await Job(Finished);

        JsonElement job = json.RootElement.GetProperty("job");
        Assert.Equal("2026-11-21T15:32:19Z", job.GetProperty("started").GetString());
        Assert.Equal("2026-11-21T16:44:19Z", job.GetProperty("ended").GetString());
        JsonElement[] agents = json.RootElement.GetProperty("agents").EnumerateArray().ToArray();
        Assert.Equal(["spec-writer", "builder", "approver"], agents.Select(agent => agent.GetProperty("name").GetString()));
        Assert.Equal(240, agents[0].GetProperty("seconds").GetInt64());
        Assert.Equal(JsonValueKind.Null, agents[1].GetProperty("seconds").ValueKind);
        Assert.Equal(3300, agents[1].GetProperty("tokens").GetInt64());
        Assert.Equal(4320, agents[2].GetProperty("seconds").GetInt64());
        Assert.Equal(5, agents[2].GetProperty("tokens").GetInt64());
    }

    [Fact]
    public async Task Job_OldRecord_HasNullTimesAndNullSeconds_RestUnchanged()
    {
        using JsonDocument json = await Job(Old);

        JsonElement job = json.RootElement.GetProperty("job");
        Assert.Equal(JsonValueKind.Null, job.GetProperty("started").ValueKind);
        Assert.Equal(JsonValueKind.Null, job.GetProperty("ended").ValueKind);
        Assert.Equal("done", job.GetProperty("stage").GetString());
        Assert.Equal(3300, job.GetProperty("tokens").GetInt64());
        JsonElement agent = Assert.Single(json.RootElement.GetProperty("agents").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, agent.GetProperty("seconds").ValueKind);
    }

    [Fact]
    public async Task Job_WithoutTokens_HasNoAgents()
    {
        using JsonDocument json = await Job(JobSamples.State("Alpha", "build", 1));

        Assert.Equal(0, json.RootElement.GetProperty("agents").GetArrayLength());
    }

    [Fact]
    public async Task Board_HasStartedAndEndedButNoAgents()
    {
        using TempFactoryRoot root = new();
        root.AddJob("1-run", Running);
        root.AddJob("2-done", Finished);
        root.AddJob("3-old", Old);
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();

        string body = await client.GetStringAsync("/api/board");

        using JsonDocument json = JsonDocument.Parse(body);
        Dictionary<string, JsonElement> jobs = json.RootElement.GetProperty("jobs").EnumerateArray()
            .ToDictionary(job => job.GetProperty("id").GetString()!);
        Assert.Equal("2026-11-21T15:32:19Z", jobs["1-run"].GetProperty("started").GetString());
        Assert.Equal(JsonValueKind.Null, jobs["1-run"].GetProperty("ended").ValueKind);
        Assert.Equal("2026-11-21T16:44:19Z", jobs["2-done"].GetProperty("ended").GetString());
        Assert.Equal(JsonValueKind.Null, jobs["3-old"].GetProperty("started").ValueKind);
        Assert.All(jobs.Values, job => Assert.False(job.TryGetProperty("agents", out _)));
        Assert.DoesNotContain("seconds", body, StringComparison.Ordinal);
    }

    private static async Task<JsonDocument> Job(string state)
    {
        using TempFactoryRoot root = new();
        root.AddJob("7-times", state);
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();

        using HttpResponseMessage response = await client.GetAsync("/api/job?id=7-times");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }
}
