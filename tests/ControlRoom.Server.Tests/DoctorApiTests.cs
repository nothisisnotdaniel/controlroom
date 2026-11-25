using System.Net;
using System.Text.Json;

namespace ControlRoom.Server.Tests;

/// <summary><c>GET /api/doctor</c>: the response shape and route rules, stuck jobs against the fixed
/// <see cref="ServerFactory.Time"/>, uncommitted job records and merged leftover branches from real local repos,
/// story problems, unreadable projects and that a request writes nothing.</summary>
public sealed class DoctorApiTests
{
    private const string Route = "/api/doctor";

    private static readonly DateTimeOffset Now = new(2026, 11, 19, 10, 0, 0, TimeSpan.Zero);

    private static readonly string[] Keys = ["stuck", "uncommitted", "mergedBranches", "stories", "unreadable"];

    [Fact]
    public async Task Doctor_Is200WithNoStoreAndExactlyTheFiveLists()
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
        using JsonDocument json = JsonDocument.Parse(await get.Content.ReadAsStringAsync());
        Assert.Equal(Keys, json.RootElement.EnumerateObject().Select(p => p.Name));
        Assert.All(json.RootElement.EnumerateObject(), p => Assert.Equal(JsonValueKind.Array, p.Value.ValueKind));
        Assert.Equal(["factory"], Strings(json.RootElement.GetProperty("unreadable")));
    }

    [Fact]
    public async Task Doctor_WithoutTheToken_Is401()
    {
        using TempFactoryRoot root = new();
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(Route);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("{\"error\":\"unauthorized\"}", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Doctor_Post_IsRefusedLikeOtherReadRoutes()
    {
        using TempFactoryRoot root = new();
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();

        using HttpResponseMessage doctor = await client.PostAsync(Route, new StringContent("{}"));
        using HttpResponseMessage history = await client.PostAsync("/api/history", new StringContent("{}"));

        Assert.Equal(HttpStatusCode.MethodNotAllowed, doctor.StatusCode);
        Assert.Equal(history.StatusCode, doctor.StatusCode);
    }

    [Fact]
    public async Task Doctor_IgnoresAnyQueryString()
    {
        using TempFactoryRoot root = new();
        string alpha = root.AddProject("Alpha", "- [ ] No story here\n");
        root.AddJob("1-old", State("Alpha", "build", "2026-11-17T10:00:00Z"));
        File.WriteAllText(Path.Combine(root.Path, "backlog.md"), "- [ ] Root line\n");
        Assert.True(Directory.Exists(alpha));
        using ServerFactory factory = new(root.Path);
        factory.Time.Now = Now;
        using HttpClient client = factory.CreateTokenClient();

        string plain = await client.GetStringAsync(Route);
        string withQuery = await client.GetStringAsync(Route + "?project=..&path=%2Fetc&id=1-old&project=Alpha");

        Assert.Equal(plain, withQuery);
        Assert.Contains("No story here", plain, StringComparison.Ordinal);
        Assert.DoesNotContain("Root line", plain, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Stuck_ListsActiveJobsOverADayOldOrWithoutTime_NullFirstThenOldest()
    {
        using TempFactoryRoot root = new();
        root.AddJob("1-old", State("Alpha", "build", "2026-11-18T09:00:00Z"));
        root.AddJob("2-recent", State("Alpha", "review", "2026-11-18T11:00:00Z"));
        root.AddJob("3-none", State("Beta", "spec", null));
        root.AddJob("4-done", State("Alpha", "done", "2026-11-10T10:00:00Z"));
        root.AddJob("5-older", State("Alpha", "spike", "2026-11-17T10:00:00Z"));
        root.AddJob("6-tie", State("Alpha", "approve", "2026-11-18T04:00:00Z"));
        root.AddJob("7-tie", State("Alpha", "review", "2026-11-18T04:00:00Z"));
        root.AddJob("8-human", State("Alpha", "needs-human", "2026-11-10T10:00:00Z"));
        root.AddJob("9-bad-time", State("Alpha", "build", "2026-11-10 10:00:00"));
        root.AddJob("10-edge", State("Alpha", "build", "2026-11-18T10:00:00Z"));

        using JsonDocument json = await Get(root);

        JsonElement[] stuck = json.RootElement.GetProperty("stuck").EnumerateArray().ToArray();
        Assert.Equal(
            ["3-none", "9-bad-time", "5-older", "6-tie", "7-tie", "1-old"],
            stuck.Select(job => job.GetProperty("id").GetString()));
        Assert.All(stuck, job => Assert.Equal(["id", "project", "stage", "hours"],
            job.EnumerateObject().Select(p => p.Name)));
        Assert.Equal(JsonValueKind.Null, stuck[0].GetProperty("hours").ValueKind);
        Assert.Equal(JsonValueKind.Null, stuck[1].GetProperty("hours").ValueKind);
        Assert.Equal([48, 30, 30, 25], stuck[2..].Select(job => job.GetProperty("hours").GetInt64()));
        Assert.Equal("Beta", stuck[0].GetProperty("project").GetString());
        Assert.Equal("spec", stuck[0].GetProperty("stage").GetString());
        Assert.Equal("approve", stuck[3].GetProperty("stage").GetString());
    }

    [Fact]
    public async Task Uncommitted_ListsUntrackedAndChangedJobIdsOnce_AndSkipsDotAndBadNames()
    {
        using TempFactoryRoot root = new();
        root.AddJob("1-changed", State("Alpha", "done", "2026-11-18T09:00:00Z"));
        root.AddJob("3-clean", State("Alpha", "done", "2026-11-18T09:00:00Z"));
        TempFactoryRoot.InitGit(root.Path, "main");
        File.WriteAllText(Path.Combine(root.JobsPath, "1-changed", "state.json"), State("Alpha", "done", null));
        root.AddJob("2-untracked", State("Alpha", "build", "2026-11-19T09:00:00Z"));
        root.AddRecord("2-untracked", "build.md", "x");
        root.AddRecord("2-untracked", "rounds/1/review-code.md", "x");
        File.WriteAllText(Path.Combine(root.JobsPath, ".ids"), "1\n");
        root.AddRecord("Bad_Name", "state.json", "{}");
        File.WriteAllText(Path.Combine(root.Path, "other.txt"), "not a job\n");

        using JsonDocument json = await Get(root);

        Assert.Equal(["1-changed", "2-untracked"], Strings(json.RootElement.GetProperty("uncommitted")));
        Assert.Empty(json.RootElement.GetProperty("unreadable").EnumerateArray());
    }

    [Fact]
    public async Task Uncommitted_WhenGitFailsInTheRoot_IsEmptyAndFactoryIsUnreadable()
    {
        using TempFactoryRoot root = new();
        root.AddJob("1-a", State("Alpha", "done", null));
        File.WriteAllText(Path.Combine(root.Path, ".git"), "gitdir: ./no-such-folder\n");

        using JsonDocument json = await Get(root);

        Assert.Empty(json.RootElement.GetProperty("uncommitted").EnumerateArray());
        Assert.Equal(["factory"], Strings(json.RootElement.GetProperty("unreadable")));
    }

    [Fact]
    public async Task MergedBranches_ListsOnlyMergedBranchesOfDoneJobs_DevelopElseMain()
    {
        using TempFactoryRoot root = new();
        string alpha = root.AddProject("Alpha", "");
        TempFactoryRoot.InitGit(alpha, "develop");
        foreach (string branch in new[]
                 {
                     "factory/1-done-merged", "factory/3-active-merged", "factory/4-missing-merged",
                     "factory/6-human-merged", "feature/1-done-merged",
                 })
        {
            TempFactoryRoot.Git(alpha, "branch", branch);
        }

        TempFactoryRoot.Git(alpha, "checkout", "-q", "-b", "factory/2-done-unmerged");
        File.WriteAllText(Path.Combine(alpha, "new.txt"), "x\n");
        TempFactoryRoot.Git(alpha, "add", "new.txt");
        TempFactoryRoot.Git(alpha, "commit", "-q", "-m", "unmerged");
        TempFactoryRoot.Git(alpha, "checkout", "-q", "develop");
        TempFactoryRoot.Git(alpha, "branch", "main");

        string beta = root.AddProject("Beta", "");
        TempFactoryRoot.InitGit(beta, "main");
        TempFactoryRoot.Git(beta, "branch", "factory/5-done-beta");
        root.AddProject("Gamma", "");

        root.AddJob("1-done-merged", State("Alpha", "done", null));
        root.AddJob("2-done-unmerged", State("Alpha", "done", null));
        root.AddJob("3-active-merged", State("Alpha", "build", "2026-11-19T09:00:00Z"));
        root.AddJob("5-done-beta", State("Beta", "done", null));
        root.AddJob("6-human-merged", State("Alpha", "needs-human", null));

        using JsonDocument json = await Get(root);

        JsonElement[] branches = json.RootElement.GetProperty("mergedBranches").EnumerateArray().ToArray();
        Assert.Equal(
            ["Alpha factory/1-done-merged", "Beta factory/5-done-beta"],
            branches.Select(b => b.GetProperty("project").GetString() + " " + b.GetProperty("branch").GetString()));
        Assert.All(branches, b => Assert.Equal(["project", "branch"], b.EnumerateObject().Select(p => p.Name)));
        Assert.Equal(["factory"], Strings(json.RootElement.GetProperty("unreadable")));
    }

    [Fact]
    public async Task UuidSuffixedIds_AreListedAsUncommittedAndAsMergedBranchesLikeLegacyOnes()
    {
        using TempFactoryRoot root = new();
        string suffixed = "405-auto-pause." + JobSamples.Uuid;
        string upper = "406-upper." + JobSamples.Uuid.ToUpperInvariant();
        string alpha = root.AddProject("Alpha", "");
        TempFactoryRoot.InitGit(alpha, "develop");
        TempFactoryRoot.Git(alpha, "branch", "factory/398-legacy");
        TempFactoryRoot.Git(alpha, "branch", "factory/" + suffixed);
        TempFactoryRoot.Git(alpha, "branch", "factory/" + upper);
        TempFactoryRoot.InitGit(root.Path, "main");
        root.AddJob("398-legacy", State("Alpha", "done", null));
        root.AddJob(suffixed, State("Alpha", "done", null));
        root.AddRecord(suffixed, "build.md", "x");
        root.AddRecord(upper, "state.json", State("Alpha", "done", null));

        using JsonDocument json = await Get(root);

        Assert.Equal(["398-legacy", suffixed], Strings(json.RootElement.GetProperty("uncommitted")));
        Assert.Equal(
            ["Alpha factory/398-legacy", "Alpha factory/" + suffixed],
            json.RootElement.GetProperty("mergedBranches").EnumerateArray()
                .Select(b => b.GetProperty("project").GetString() + " " + b.GetProperty("branch").GetString()));
        Assert.Empty(json.RootElement.GetProperty("unreadable").EnumerateArray());
    }

    [Fact]
    public async Task Stories_ReportsEachProblemInFileOrderThenOrphans_WithoutLeavingTheBacklogFolder()
    {
        using TempFactoryRoot root = new();
        string longTitle = new('a', 100);
        string alpha = root.AddProject("Alpha",
            "# Backlog\n"
            + "- [ ] [#1](backlog/001-good.md) Good one\n"
            + "- [ ] Old line: without story\n"
            + "- [x] Done old line: ignored\n"
            + "- [?] [#2](backlog/002-missing.md) Missing story\n"
            + "- [h] [#1](backlog/001-good.md) Same number\n"
            + "- [ ] [#3](backlog/003-linked.md) Linked story\n"
            + "- [ ] [#4](../x.md) Outside\n"
            + "- [ ] [#5](backlog/005-Upper.md) Upper case\n"
            + "- [x] [#6](backlog/006-gone.md) Done but missing\n"
            + "- [ ] [#7](backlog/007-folder.md) A folder\n"
            + "- [ ] " + longTitle + "\n");
        TempFactoryRoot.AddStory(alpha, "001-good.md", "# Good\n");
        TempFactoryRoot.AddStory(alpha, "008-orphan.md", "# Orphan\n");
        TempFactoryRoot.AddStory(alpha, "notes.txt", "not a story\n");
        Directory.CreateDirectory(Path.Combine(alpha, "backlog", "007-folder.md"));
        string outside = Path.Combine(root.Path, ".outside");
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "003-linked.md"), "# Linked\n");
        File.CreateSymbolicLink(Path.Combine(alpha, "backlog", "003-linked.md"), Path.Combine(outside, "003-linked.md"));
        // The ../x.md target resolves to this file; it must still be reported as a bad link.
        File.WriteAllText(Path.Combine(root.Path, "x.md"), "# Outside\n");

        string beta = root.AddProject("Beta", "- [ ] [#1](backlog/001-good.md) Linked folder\n");
        File.WriteAllText(Path.Combine(outside, "001-good.md"), "# Good\n");
        File.WriteAllText(Path.Combine(outside, "009-stray.md"), "# Stray\n");
        Directory.CreateSymbolicLink(Path.Combine(beta, "backlog"), outside);

        using JsonDocument json = await Get(root);

        JsonElement[] stories = json.RootElement.GetProperty("stories").EnumerateArray().ToArray();
        Assert.All(stories, s => Assert.Equal(["project", "number", "problem", "line"],
            s.EnumerateObject().Select(p => p.Name)));
        Assert.Equal(
            [
                "Alpha - no_story Old line",
                "Alpha 2 missing Missing story",
                "Alpha 1 duplicate Same number",
                "Alpha 3 missing Linked story",
                "Alpha 4 bad_link Outside",
                "Alpha 5 bad_link Upper case",
                "Alpha 6 missing Done but missing",
                "Alpha 7 missing A folder",
                "Alpha - no_story " + new string('a', 80),
                "Alpha - orphan 008-orphan.md",
                "Beta 1 missing Linked folder",
            ],
            stories.Select(Describe));
    }

    [Fact]
    public async Task Unreadable_ProjectsContributeNothingElse_SortedOnce()
    {
        using TempFactoryRoot root = new();
        string outside = Path.Combine(root.Path, ".outside");
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "backlog.md"), "- [ ] Secret line\n");

        string linked = root.AddProject("Linked", null);
        File.CreateSymbolicLink(Path.Combine(linked, "backlog.md"), Path.Combine(outside, "backlog.md"));
        TempFactoryRoot.InitGit(linked, "develop");
        TempFactoryRoot.Git(linked, "branch", "factory/1-done");
        root.AddProject("Absent", null);
        string broken = root.AddProject("Broken", "- [ ] Broken line\n");
        File.WriteAllText(Path.Combine(broken, ".git"), "gitdir: ./no-such-folder\n");
        root.AddProject("Fine", "- [ ] Fine line\n");
        root.AddJob("1-done", State("Linked", "done", null));

        using JsonDocument json = await Get(root);

        Assert.Equal(["Absent", "Broken", "Linked", "factory"], Strings(json.RootElement.GetProperty("unreadable")));
        Assert.Empty(json.RootElement.GetProperty("mergedBranches").EnumerateArray());
        Assert.Equal(["Fine - no_story Fine line"],
            json.RootElement.GetProperty("stories").EnumerateArray().Select(Describe));
    }

    [Fact]
    public async Task Doctor_WritesNothingAndMovesNoHead()
    {
        using TempFactoryRoot root = new();
        string alpha = root.AddProject("Alpha", "- [ ] [#1](backlog/001-a.md) A\n");
        TempFactoryRoot.AddStory(alpha, "001-a.md", "# A\n");
        TempFactoryRoot.InitGit(alpha, "develop");
        TempFactoryRoot.Git(alpha, "branch", "factory/1-done");
        root.AddJob("1-done", State("Alpha", "done", null));
        TempFactoryRoot.InitGit(root.Path, "main");
        root.AddJob("2-new", State("Alpha", "build", null));
        File.WriteAllText(Path.Combine(alpha, "backlog.md"), "- [ ] [#1](backlog/001-a.md) A changed\n");
        string rootStatus = TempFactoryRoot.Git(root.Path, "status", "--porcelain", "--untracked-files=all");
        string alphaStatus = TempFactoryRoot.Git(alpha, "status", "--porcelain", "--untracked-files=all");
        string rootHead = TempFactoryRoot.Git(root.Path, "rev-parse", "HEAD");
        string alphaHead = TempFactoryRoot.Git(alpha, "rev-parse", "HEAD");
        IReadOnlyList<string> before = root.Snapshot();

        using JsonDocument json = await Get(root);

        Assert.Equal(before, root.Snapshot());
        Assert.Equal(rootStatus, TempFactoryRoot.Git(root.Path, "status", "--porcelain", "--untracked-files=all"));
        Assert.Equal(alphaStatus, TempFactoryRoot.Git(alpha, "status", "--porcelain", "--untracked-files=all"));
        Assert.Equal(rootHead, TempFactoryRoot.Git(root.Path, "rev-parse", "HEAD"));
        Assert.Equal(alphaHead, TempFactoryRoot.Git(alpha, "rev-parse", "HEAD"));
        Assert.Equal(["2-new"], Strings(json.RootElement.GetProperty("uncommitted")));
        Assert.Single(json.RootElement.GetProperty("mergedBranches").EnumerateArray());
    }

    private static string Describe(JsonElement story)
    {
        JsonElement number = story.GetProperty("number");
        return story.GetProperty("project").GetString() + " "
            + (number.ValueKind == JsonValueKind.Null ? "-" : number.GetInt32().ToString(System.Globalization.CultureInfo.InvariantCulture))
            + " " + story.GetProperty("problem").GetString() + " " + story.GetProperty("line").GetString();
    }

    private static string[] Strings(JsonElement list)
    {
        return list.EnumerateArray().Select(item => item.GetString() ?? "").ToArray();
    }

    private static async Task<JsonDocument> Get(TempFactoryRoot root)
    {
        using ServerFactory factory = new(root.Path);
        factory.Time.Now = Now;
        using HttpClient client = factory.CreateTokenClient();
        using HttpResponseMessage response = await client.GetAsync(Route);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    /// <summary>A job state; <paramref name="updated"/> <c>null</c> leaves <c>updated</c> out.</summary>
    private static string State(string project, string stage, string? updated)
    {
        Dictionary<string, object> state = new() { ["project"] = project, ["stage"] = stage, ["title"] = "T" };
        if (updated is not null)
        {
            state["updated"] = updated;
        }

        return JsonSerializer.Serialize(state);
    }
}
