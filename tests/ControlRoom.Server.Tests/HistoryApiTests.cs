using System.Net;
using System.Text.Json;

namespace ControlRoom.Server.Tests;

/// <summary><c>GET /api/history</c>: the finished-job list, its order, unknown tokens, the 7/30-day windows, the
/// totals and chart data, and the shared route rules. The clock is the fixed <see cref="ServerFactory.Time"/>.</summary>
public sealed class HistoryApiTests
{
    private const string Route = "/api/history";

    private static readonly DateTimeOffset Now = new(2026, 11, 19, 10, 0, 0, TimeSpan.Zero);

    private static readonly string[] JobFields =
        ["id", "project", "title", "mode", "path", "round", "outcome", "tokens", "finished"];

    private static readonly string[] BoardFields =
    [
        "id", "project", "title", "branch", "stage", "round", "mode", "path", "risk", "reviews", "pastRounds",
        "specReview", "specRevised", "decision", "note", "ciStatus", "tokens", "request", "started", "ended",
    ];

    [Fact]
    public async Task History_ListsOnlyFinishedStagesWithTheirOutcome()
    {
        using TempFactoryRoot root = new();
        root.AddJob("1-merged", State("Alpha", "done", "quality", "2026-11-19T01:00:00Z", 10));
        root.AddJob("2-findings", State("Alpha", "done", "quality", "2026-11-19T02:00:00Z", 10, "FINDINGS"));
        root.AddJob("3-approved", State("Alpha", "done", "quality", "2026-11-19T03:00:00Z", 10, "APPROVE"));
        root.AddJob("4-escalated", State("Alpha", "needs-human", "quality", "2026-11-19T04:00:00Z", 10, "ESCALATE"));
        root.AddJob("5-reverted", State("Alpha", "reverted", "quality", "2026-11-19T05:00:00Z", 10, "FINDINGS"));
        foreach (string stage in new[] { "spec", "build", "review", "spike", "DONE", "" })
        {
            root.AddJob("6-" + (stage == "" ? "empty" : stage.ToLowerInvariant() + "x"),
                State("Alpha", stage, "quality", "2026-11-19T06:00:00Z", 10));
        }

        root.AddJob("7-no-stage", "{\"project\":\"Alpha\",\"updated\":\"2026-11-19T07:00:00Z\"}");
        root.AddJob("8-array", "[{\"stage\":\"done\"}]");
        root.AddJob("8-broken", "{\"stage\":");
        root.AddJob("8-missing", null);
        root.AddJob("8-text", "\"done\"");

        using JsonDocument json = await Get(root);

        Assert.Equal(
            ["jobs", "days", "totals", "chart", "lifetime"], json.RootElement.EnumerateObject().Select(p => p.Name));
        JsonElement[] jobs = json.RootElement.GetProperty("jobs").EnumerateArray().ToArray();
        Assert.Equal(
            ["5-reverted", "4-escalated", "3-approved", "2-findings", "1-merged"],
            jobs.Select(job => job.GetProperty("id").GetString()));
        Assert.Equal(
            ["reverted", "escalated", "merged", "findings", "merged"],
            jobs.Select(job => job.GetProperty("outcome").GetString()));
        Assert.All(jobs, job => Assert.Equal(JobFields, job.EnumerateObject().Select(p => p.Name)));
    }

    [Fact]
    public async Task History_JobCarriesItsFieldsAndFinishedInTheExactForm()
    {
        using TempFactoryRoot root = new();
        root.AddJob("12-full", JobSamples.FullState.Replace("\"stage\": \"review\"", "\"stage\": \"done\"",
            StringComparison.Ordinal).Replace("\"round\": 2,", "\"round\": 2, \"updated\": \"2026-11-18T23:59:59Z\",",
            StringComparison.Ordinal));
        root.AddJob("13-bare", "{\"stage\":\"done\"}");

        using JsonDocument json = await Get(root);
        JsonElement[] jobs = json.RootElement.GetProperty("jobs").EnumerateArray().ToArray();

        JsonElement full = jobs[0];
        Assert.Equal("12-full", full.GetProperty("id").GetString());
        Assert.Equal("Alpha", full.GetProperty("project").GetString());
        Assert.Equal("#2 Factory dashboard", full.GetProperty("title").GetString());
        Assert.Equal("quality", full.GetProperty("mode").GetString());
        Assert.Equal("full", full.GetProperty("path").GetString());
        Assert.Equal(2, full.GetProperty("round").GetInt32());
        Assert.Equal(204060, full.GetProperty("tokens").GetInt64());
        Assert.Equal("2026-11-18T23:59:59Z", full.GetProperty("finished").GetString());

        JsonElement bare = jobs[1];
        Assert.Equal("13-bare", bare.GetProperty("id").GetString());
        Assert.Equal("merged", bare.GetProperty("outcome").GetString());
        foreach (string name in new[] { "project", "title", "mode", "path", "round", "tokens", "finished" })
        {
            Assert.Equal(JsonValueKind.Null, bare.GetProperty(name).ValueKind);
        }
    }

    [Fact]
    public async Task History_SortsNewestFirstThenIdDescendingWithUnfinishedLast()
    {
        using TempFactoryRoot root = new();
        root.AddJob("1-old", State("Alpha", "done", "quality", "2026-11-01T10:00:00Z", 1));
        root.AddJob("2-tie", State("Alpha", "done", "quality", "2026-11-10T10:00:00Z", 1));
        root.AddJob("10-tie", State("Alpha", "done", "quality", "2026-11-10T10:00:00Z", 1));
        root.AddJob("3-new", State("Alpha", "done", "quality", "2026-11-12T10:00:00Z", 1));
        root.AddJob("4-null", State("Alpha", "done", "quality", null, 1));
        root.AddJob("20-null", State("Alpha", "done", "quality", null, 1));
        root.AddJob("5-bad", State("Alpha", "done", "quality", "2026-11-12T10:00:00+00:00", 1));
        root.AddJob("6-future", State("Alpha", "done", "quality", "2027-01-01T00:00:00Z", 1));

        using JsonDocument json = await Get(root);

        Assert.Equal(
            ["6-future", "3-new", "2-tie", "10-tie", "1-old", "5-bad", "4-null", "20-null"],
            json.RootElement.GetProperty("jobs").EnumerateArray().Select(job => job.GetProperty("id").GetString()));
    }

    [Fact]
    public async Task History_TokensAreNullWhenUnknownAndTheSumOtherwise()
    {
        using TempFactoryRoot root = new();
        root.AddJob("1-missing", "{\"stage\":\"done\"}");
        root.AddJob("2-empty", "{\"stage\":\"done\",\"tokens\":{}}");
        root.AddJob("3-bad", "{\"stage\":\"done\",\"tokens\":{\"a\":{\"input\":-1,\"output\":\"5\"},\"b\":3}}");
        root.AddJob("4-array", "{\"stage\":\"done\",\"tokens\":[1,2]}");
        root.AddJob("5-known",
            "{\"stage\":\"done\",\"tokens\":{\"a\":{\"input\":1,\"cacheRead\":20,\"output\":300},\"b\":{\"output\":4000}}}");
        root.AddJob("6-zero", "{\"stage\":\"done\",\"tokens\":{\"a\":{\"input\":0}}}");

        using JsonDocument json = await Get(root);
        Dictionary<string, JsonElement> tokens = json.RootElement.GetProperty("jobs").EnumerateArray()
            .ToDictionary(job => job.GetProperty("id").GetString()!, job => job.GetProperty("tokens"));

        foreach (string id in new[] { "1-missing", "2-empty", "3-bad", "4-array" })
        {
            Assert.Equal(JsonValueKind.Null, tokens[id].ValueKind);
        }

        Assert.Equal(4321, tokens["5-known"].GetInt64());
        Assert.Equal(0, tokens["6-zero"].GetInt64());
    }

    [Fact]
    public async Task History_DaysAreTheThirtyUtcDatesEndingToday()
    {
        using TempFactoryRoot root = new();

        using JsonDocument json = await Get(root, new DateTimeOffset(2026, 11, 19, 23, 30, 0, TimeSpan.FromHours(-5)));

        string[] days = json.RootElement.GetProperty("days").EnumerateArray().Select(day => day.GetString()!).ToArray();
        Assert.Equal(30, days.Length);
        Assert.Equal("2026-10-22", days[0]);
        Assert.Equal("2026-11-20", days[^1]);
        Assert.Equal(days.Order(StringComparer.Ordinal), days);
        Assert.Empty(json.RootElement.GetProperty("jobs").EnumerateArray());
        Assert.Empty(json.RootElement.GetProperty("totals").GetProperty("projects").EnumerateArray());
        Assert.Empty(json.RootElement.GetProperty("totals").GetProperty("modes").EnumerateArray());
        Assert.Empty(json.RootElement.GetProperty("chart").GetProperty("modes").EnumerateArray());
    }

    [Fact]
    public async Task History_WindowEdgesCountByUtcDate()
    {
        using TempFactoryRoot root = new();
        root.AddJob("1-today-late", State("Alpha", "done", "quality", "2026-11-19T23:59:59Z", 1));
        root.AddJob("2-6-days", State("Alpha", "done", "quality", "2026-11-13T00:00:00Z", 10));
        root.AddJob("3-7-days", State("Alpha", "done", "quality", "2026-11-12T23:59:59Z", 100));
        root.AddJob("4-29-days", State("Alpha", "done", "quality", "2026-10-21T00:00:00Z", 1000));
        root.AddJob("5-30-days", State("Alpha", "done", "quality", "2026-10-20T23:59:59Z", 10000));
        root.AddJob("6-future", State("Alpha", "done", "quality", "2026-11-20T00:00:00Z", 100000));
        root.AddJob("7-null", State("Alpha", "done", "quality", null, 1000000));

        using JsonDocument json = await Get(root);

        Assert.Equal("2026-10-21", json.RootElement.GetProperty("days")[0].GetString());
        Assert.Equal("2026-11-19", json.RootElement.GetProperty("days")[29].GetString());
        JsonElement alpha = Assert.Single(json.RootElement.GetProperty("totals").GetProperty("projects").EnumerateArray());
        Assert.Equal("Alpha", alpha.GetProperty("name").GetString());
        AssertWindow(alpha.GetProperty("last7"), 2, 11, 0);
        AssertWindow(alpha.GetProperty("last30"), 4, 1111, 0);
        Assert.Equal(7, json.RootElement.GetProperty("jobs").GetArrayLength());
    }

    [Fact]
    public async Task History_TotalsCountUnknownApartAndSortNamesWithNullLast()
    {
        using TempFactoryRoot root = new();
        root.AddJob("1-a", State("beta", "done", "quality", "2026-11-19T01:00:00Z", 5));
        root.AddJob("2-a", State("Alpha", "done", "premium", "2026-11-18T01:00:00Z", 7));
        root.AddJob("3-a", State("Alpha", "needs-human", "quality", "2026-11-01T01:00:00Z", null));
        root.AddJob("4-a", State(null, "done", null, "2026-11-17T01:00:00Z", 11));
        root.AddJob("5-a", State("Alpha", "reverted", "premium", "2026-11-16T01:00:00Z", null));
        root.AddJob("6-a", State("Old", "done", "budget", "2026-09-01T01:00:00Z", 99));
        root.AddJob("7-a", State("Never", "done", "light", null, 99));
        root.AddJob("8-a", State("Busy", "build", "fast", "2026-11-19T01:00:00Z", 99));

        using JsonDocument json = await Get(root);
        JsonElement totals = json.RootElement.GetProperty("totals");
        Assert.Equal(["projects", "modes"], totals.EnumerateObject().Select(p => p.Name));

        JsonElement[] projects = totals.GetProperty("projects").EnumerateArray().ToArray();
        Assert.Equal(["Alpha", "beta", null], projects.Select(p => p.GetProperty("name").GetString()));
        Assert.Equal(["name", "last7", "last30"], projects[0].EnumerateObject().Select(p => p.Name));
        Assert.Equal(["jobs", "tokens", "unknown"], projects[0].GetProperty("last7").EnumerateObject().Select(p => p.Name));
        AssertWindow(projects[0].GetProperty("last7"), 2, 7, 1);
        AssertWindow(projects[0].GetProperty("last30"), 3, 7, 2);
        AssertWindow(projects[1].GetProperty("last7"), 1, 5, 0);
        AssertWindow(projects[1].GetProperty("last30"), 1, 5, 0);
        AssertWindow(projects[2].GetProperty("last7"), 1, 11, 0);
        AssertWindow(projects[2].GetProperty("last30"), 1, 11, 0);

        JsonElement[] modes = totals.GetProperty("modes").EnumerateArray().ToArray();
        Assert.Equal(["premium", "quality", null], modes.Select(p => p.GetProperty("name").GetString()));
        AssertWindow(modes[0].GetProperty("last7"), 2, 7, 1);
        AssertWindow(modes[1].GetProperty("last7"), 1, 5, 0);
        AssertWindow(modes[1].GetProperty("last30"), 2, 5, 1);
        AssertWindow(modes[2].GetProperty("last30"), 1, 11, 0);
    }

    [Fact]
    public async Task History_LifetimeCountsEveryFinishedJobWhateverItsAgeAndSortsNamesWithNullLast()
    {
        using TempFactoryRoot root = new();
        root.AddJob("1-a", State("beta", "done", "quality", "2026-11-19T01:00:00Z", 5));
        root.AddJob("2-a", State("Alpha", "needs-human", "premium", "2026-11-18T01:00:00Z", 7));
        root.AddJob("3-a", State("Alpha", "reverted", "quality", "2026-11-01T01:00:00Z", 11));
        root.AddJob("4-a", State("Old", "done", "budget", "2025-03-01T01:00:00Z", 13));
        root.AddJob("5-a", State("Never", "done", "light", null, 17));
        root.AddJob("6-a", State(null, "done", null, "2026-10-01T01:00:00Z", 19));
        root.AddJob("7-a", State("Busy", "build", "fast", "2026-11-19T01:00:00Z", 99));
        root.AddJob("8-a", State("Alpha", "review", "quality", "2026-11-19T01:00:00Z", 99));

        using JsonDocument json = await Get(root);

        Assert.Equal(
            ["jobs", "days", "totals", "chart", "lifetime"], json.RootElement.EnumerateObject().Select(p => p.Name));
        JsonElement totals = json.RootElement.GetProperty("totals");
        Assert.Equal(["projects", "modes"], totals.EnumerateObject().Select(p => p.Name));
        Assert.Equal(["name", "last7", "last30"],
            totals.GetProperty("projects")[0].EnumerateObject().Select(p => p.Name));

        JsonElement lifetime = json.RootElement.GetProperty("lifetime");
        Assert.Equal(["projects", "modes", "projectModes"], lifetime.EnumerateObject().Select(p => p.Name));

        JsonElement[] projects = lifetime.GetProperty("projects").EnumerateArray().ToArray();
        Assert.Equal(["Alpha", "Never", "Old", "beta", null], projects.Select(p => p.GetProperty("name").GetString()));
        Assert.All(projects, p => Assert.Equal(["name", "all"], p.EnumerateObject().Select(f => f.Name)));
        Assert.Equal(["jobs", "tokens", "unknown"], projects[0].GetProperty("all").EnumerateObject().Select(p => p.Name));
        AssertWindow(projects[0].GetProperty("all"), 2, 18, 0);
        AssertWindow(projects[1].GetProperty("all"), 1, 17, 0);
        AssertWindow(projects[2].GetProperty("all"), 1, 13, 0);
        AssertWindow(projects[3].GetProperty("all"), 1, 5, 0);
        AssertWindow(projects[4].GetProperty("all"), 1, 19, 0);

        JsonElement[] modes = lifetime.GetProperty("modes").EnumerateArray().ToArray();
        Assert.Equal(["budget", "light", "premium", "quality", null],
            modes.Select(p => p.GetProperty("name").GetString()));
        AssertWindow(modes[0].GetProperty("all"), 1, 13, 0);
        AssertWindow(modes[1].GetProperty("all"), 1, 17, 0);
        AssertWindow(modes[2].GetProperty("all"), 1, 7, 0);
        AssertWindow(modes[3].GetProperty("all"), 2, 16, 0);
        AssertWindow(modes[4].GetProperty("all"), 1, 19, 0);
        Assert.Equal(6, projects.Sum(p => p.GetProperty("all").GetProperty("jobs").GetInt32()));
    }

    [Fact]
    public async Task History_LifetimeCountsUnknownTokensApartNeverAsZero()
    {
        using TempFactoryRoot root = new();
        root.AddJob("1-a", State("Alpha", "done", "quality", "2026-11-19T01:00:00Z", 5));
        root.AddJob("2-a", State("Alpha", "done", "quality", "2024-01-01T01:00:00Z", null));
        root.AddJob("3-a", State("Alpha", "needs-human", "premium", null, null));
        root.AddJob("4-a", State("Beta", "done", "premium", "2026-01-01T01:00:00Z", 0));
        root.AddJob("5-a", State("Beta", "done", "premium", "2026-01-02T01:00:00Z", null));

        using JsonDocument json = await Get(root);
        JsonElement lifetime = json.RootElement.GetProperty("lifetime");

        JsonElement[] projects = lifetime.GetProperty("projects").EnumerateArray().ToArray();
        AssertWindow(projects[0].GetProperty("all"), 3, 5, 2);
        AssertWindow(projects[1].GetProperty("all"), 2, 0, 1);
        JsonElement[] modes = lifetime.GetProperty("modes").EnumerateArray().ToArray();
        Assert.Equal(["premium", "quality"], modes.Select(p => p.GetProperty("name").GetString()));
        AssertWindow(modes[0].GetProperty("all"), 3, 0, 2);
        AssertWindow(modes[1].GetProperty("all"), 2, 5, 1);
    }

    [Fact]
    public async Task History_LifetimeTokensSaturateAtLongMaxValue()
    {
        using TempFactoryRoot root = new();
        root.AddJob("1-a", State("Alpha", "done", "quality", "2026-11-19T01:00:00Z", long.MaxValue - 10));
        root.AddJob("2-a", State("Alpha", "done", "quality", "2020-11-19T01:00:00Z", long.MaxValue - 20));

        using JsonDocument json = await Get(root);
        JsonElement lifetime = json.RootElement.GetProperty("lifetime");

        AssertWindow(lifetime.GetProperty("projects")[0].GetProperty("all"), 2, long.MaxValue, 0);
        AssertWindow(lifetime.GetProperty("modes")[0].GetProperty("all"), 2, long.MaxValue, 0);
        AssertWindow(lifetime.GetProperty("projectModes")[0].GetProperty("modes")[0].GetProperty("all"),
            2, long.MaxValue, 0);
    }

    [Fact]
    public async Task History_LifetimeProjectModesFollowTheProjectsAndAddUpToTheirJobs()
    {
        using TempFactoryRoot root = new();
        root.AddJob("1-a", State("Beta", "done", "quality", "2026-11-19T01:00:00Z", 1));
        root.AddJob("2-a", State("Alpha", "done", "quality", "2026-11-18T01:00:00Z", 2));
        root.AddJob("3-a", State("Alpha", "done", "premium", "2025-11-18T01:00:00Z", 4));
        root.AddJob("4-a", State("Alpha", "done", "quality", null, null));
        root.AddJob("5-a", State("Beta", "reverted", "budget", "2026-01-18T01:00:00Z", 8));
        root.AddJob("6-a", State(null, "done", null, "2026-01-18T01:00:00Z", 16));
        root.AddJob("7-a", State("Alpha", "build", "light", "2026-11-19T01:00:00Z", 32));

        using JsonDocument json = await Get(root);
        JsonElement lifetime = json.RootElement.GetProperty("lifetime");
        JsonElement[] projects = lifetime.GetProperty("projects").EnumerateArray().ToArray();
        JsonElement[] projectModes = lifetime.GetProperty("projectModes").EnumerateArray().ToArray();

        Assert.Equal(
            projects.Select(p => p.GetProperty("name").GetString()),
            projectModes.Select(p => p.GetProperty("name").GetString()));
        Assert.All(projectModes, p => Assert.Equal(["name", "modes"], p.EnumerateObject().Select(f => f.Name)));

        JsonElement[] alpha = projectModes[0].GetProperty("modes").EnumerateArray().ToArray();
        Assert.Equal(["premium", "quality"], alpha.Select(p => p.GetProperty("name").GetString()));
        Assert.Equal(["name", "all"], alpha[0].EnumerateObject().Select(f => f.Name));
        AssertWindow(alpha[0].GetProperty("all"), 1, 4, 0);
        AssertWindow(alpha[1].GetProperty("all"), 2, 2, 1);

        JsonElement[] beta = projectModes[1].GetProperty("modes").EnumerateArray().ToArray();
        Assert.Equal(["budget", "quality"], beta.Select(p => p.GetProperty("name").GetString()));
        AssertWindow(beta[0].GetProperty("all"), 1, 8, 0);
        AssertWindow(beta[1].GetProperty("all"), 1, 1, 0);

        Assert.Equal(JsonValueKind.Null, projectModes[2].GetProperty("name").ValueKind);
        JsonElement none = Assert.Single(projectModes[2].GetProperty("modes").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, none.GetProperty("name").ValueKind);
        AssertWindow(none.GetProperty("all"), 1, 16, 0);

        for (int index = 0; index < projects.Length; index++)
        {
            Assert.Equal(
                projects[index].GetProperty("all").GetProperty("jobs").GetInt32(),
                projectModes[index].GetProperty("modes").EnumerateArray()
                    .Sum(mode => mode.GetProperty("all").GetProperty("jobs").GetInt32()));
        }
    }

    [Fact]
    public async Task History_LifetimeIsEmptyWithoutFinishedJobs()
    {
        using TempFactoryRoot root = new();
        root.AddJob("1-a", State("Alpha", "build", "quality", "2026-11-19T01:00:00Z", 5));

        using JsonDocument json = await Get(root);
        JsonElement lifetime = json.RootElement.GetProperty("lifetime");

        Assert.Empty(lifetime.GetProperty("projects").EnumerateArray());
        Assert.Empty(lifetime.GetProperty("modes").EnumerateArray());
        Assert.Empty(lifetime.GetProperty("projectModes").EnumerateArray());
    }

    [Fact]
    public async Task History_ChartHasThirtyDailyValuesPerModeInTotalsOrder()
    {
        using TempFactoryRoot root = new();
        root.AddJob("1-a", State("Alpha", "done", "quality", "2026-11-18T01:00:00Z", 100));
        root.AddJob("2-a", State("Beta", "done", "quality", "2026-11-18T23:00:00Z", 20));
        root.AddJob("3-a", State("Alpha", "done", "premium", "2026-11-18T12:00:00Z", 3000));
        root.AddJob("4-a", State("Alpha", "done", "premium", "2026-11-18T13:00:00Z", null));
        root.AddJob("5-a", State("Alpha", "done", "premium", "2026-10-21T00:00:00Z", 4));
        root.AddJob("6-a", State("Alpha", "done", "quality", "2026-11-19T09:00:00Z", 5));
        root.AddJob("7-a", State("Alpha", "done", "budget", "2026-11-19T09:00:00Z", null));

        using JsonDocument json = await Get(root);

        JsonElement[] chart = json.RootElement.GetProperty("chart").GetProperty("modes").EnumerateArray().ToArray();
        Assert.Equal(
            json.RootElement.GetProperty("totals").GetProperty("modes").EnumerateArray()
                .Select(mode => mode.GetProperty("name").GetString()),
            chart.Select(mode => mode.GetProperty("name").GetString()));
        Assert.Equal(["budget", "premium", "quality"], chart.Select(mode => mode.GetProperty("name").GetString()));
        Assert.Equal(["name", "tokens"], chart[0].EnumerateObject().Select(p => p.Name));

        long[] budget = Values(chart[0]);
        long[] premium = Values(chart[1]);
        long[] quality = Values(chart[2]);
        Assert.All([budget, premium, quality], values => Assert.Equal(30, values.Length));
        Assert.All(budget, value => Assert.Equal(0, value));
        Assert.Equal(4, premium[0]);
        Assert.Equal(3000, premium[28]);
        Assert.Equal(3004, premium.Sum());
        Assert.Equal(120, quality[28]);
        Assert.Equal(5, quality[29]);
        Assert.Equal(125, quality.Sum());
    }

    [Fact]
    public async Task History_WithoutTheToken_Is401()
    {
        using TempFactoryRoot root = new();
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(Route);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("{\"error\":\"unauthorized\"}", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task History_GetIs200WithNoStore_AndHeadIsAllowed()
    {
        using TempFactoryRoot root = new();
        using ServerFactory factory = new(root.Path);
        factory.Time.Now = Now;
        using HttpClient client = factory.CreateTokenClient();

        using HttpResponseMessage get = await client.GetAsync(Route);
        using HttpRequestMessage headRequest = new(HttpMethod.Head, Route);
        using HttpResponseMessage head = await client.SendAsync(headRequest);

        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        Assert.Equal("application/json", get.Content.Headers.ContentType?.MediaType);
        Assert.True(get.Headers.CacheControl?.NoStore);
        Assert.Equal(HttpStatusCode.OK, head.StatusCode);
        Assert.True(head.Headers.CacheControl?.NoStore);
    }

    [Fact]
    public async Task History_Post_IsRefusedLikeOtherReadRoutes()
    {
        using TempFactoryRoot root = new();
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();

        using HttpResponseMessage history = await client.PostAsync(Route, new StringContent("{}"));
        using HttpResponseMessage tasks = await client.PostAsync("/api/tasks", new StringContent("{}"));

        Assert.Equal(HttpStatusCode.MethodNotAllowed, history.StatusCode);
        Assert.Equal(tasks.StatusCode, history.StatusCode);
        Assert.Equal(await tasks.Content.ReadAsStringAsync(), await history.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task History_WritesNothingAndSkipsSymlinkedJobsAndStates()
    {
        using TempFactoryRoot root = new();
        root.AddJob("1-real", State("Alpha", "done", "quality", "2026-11-19T01:00:00Z", 5));
        string outside = Path.Combine(root.Path, ".outside");
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "state.json"),
            State("Secret", "done", "quality", "2026-11-19T02:00:00Z", 7));
        Directory.CreateSymbolicLink(Path.Combine(root.JobsPath, "2-linked-folder"), outside);
        string linkedState = root.AddJob("3-linked-state", null);
        File.CreateSymbolicLink(Path.Combine(linkedState, "state.json"), Path.Combine(outside, "state.json"));
        IReadOnlyList<string> before = root.Snapshot();

        using JsonDocument json = await Get(root);

        Assert.Equal(before, root.Snapshot());
        JsonElement job = Assert.Single(json.RootElement.GetProperty("jobs").EnumerateArray());
        Assert.Equal("1-real", job.GetProperty("id").GetString());
        Assert.DoesNotContain("Secret", json.RootElement.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Board_KeyListStaysTheSameWithUpdated()
    {
        using TempFactoryRoot root = new();
        root.AddJob("1-a", State("Alpha", "done", "quality", "2026-11-19T01:00:00Z", 5));
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();

        string body = await client.GetStringAsync("/api/board");
        using JsonDocument json = JsonDocument.Parse(body);

        JsonElement job = Assert.Single(json.RootElement.GetProperty("jobs").EnumerateArray());
        Assert.Equal(BoardFields, job.EnumerateObject().Select(p => p.Name));
        Assert.DoesNotContain("2026-11-19", body, StringComparison.Ordinal);
    }

    private static async Task<JsonDocument> Get(TempFactoryRoot root)
    {
        return await Get(root, Now);
    }

    private static async Task<JsonDocument> Get(TempFactoryRoot root, DateTimeOffset now)
    {
        using ServerFactory factory = new(root.Path);
        factory.Time.Now = now;
        using HttpClient client = factory.CreateTokenClient();
        using HttpResponseMessage response = await client.GetAsync(Route);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    private static void AssertWindow(JsonElement window, int jobs, long tokens, int unknown)
    {
        Assert.Equal(jobs, window.GetProperty("jobs").GetInt32());
        Assert.Equal(tokens, window.GetProperty("tokens").GetInt64());
        Assert.Equal(unknown, window.GetProperty("unknown").GetInt32());
    }

    private static long[] Values(JsonElement mode)
    {
        return mode.GetProperty("tokens").EnumerateArray().Select(value => value.GetInt64()).ToArray();
    }

    /// <summary>A finished-job state; <paramref name="tokens"/> <c>null</c> leaves <c>tokens</c> out, a name
    /// <c>null</c> leaves that field out.</summary>
    private static string State(
        string? project, string stage, string? mode, string? updated, long? tokens, string? decision = null)
    {
        Dictionary<string, object> state = new() { ["stage"] = stage, ["title"] = "T", ["path"] = "full", ["round"] = 1 };
        if (project is not null)
        {
            state["project"] = project;
        }

        if (mode is not null)
        {
            state["mode"] = mode;
        }

        if (updated is not null)
        {
            state["updated"] = updated;
        }

        if (tokens is not null)
        {
            state["tokens"] = new Dictionary<string, object>
            {
                ["builder"] = new Dictionary<string, object> { ["input"] = tokens.Value, ["output"] = 0 },
            };
        }

        if (decision is not null)
        {
            state["decision"] = decision;
        }

        return JsonSerializer.Serialize(state);
    }
}
