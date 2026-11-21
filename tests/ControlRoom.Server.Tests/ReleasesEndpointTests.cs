using System.Globalization;
using System.Net;
using System.Text.Json;

namespace ControlRoom.Server.Tests;

/// <summary><c>GET /api/releases</c>: shape, order and route rules, the release tag pick, the commit count and log,
/// the version rule from merged jobs' stories, and the unavailable cases, all from real local repos (read-only) in
/// each test's own temp factory root.</summary>
public sealed class ReleasesEndpointTests
{
    private const string Route = "/api/releases";

    [Fact]
    public async Task Releases_ListsEveryProjectInFinderOrderWithNoStore()
    {
        using TempFactoryRoot root = new();
        root.AddProject("beta", "- [ ] B\n");
        root.AddProject("Alpha", "- [ ] A\n");
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();

        using HttpResponseMessage get = await client.GetAsync(Route);
        using HttpRequestMessage headRequest = new(HttpMethod.Head, Route);
        using HttpResponseMessage head = await client.SendAsync(headRequest);

        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        Assert.Equal("application/json", get.Content.Headers.ContentType?.MediaType);
        Assert.True(get.Headers.CacheControl?.NoStore);
        Assert.Equal(HttpStatusCode.OK, head.StatusCode);
        Assert.True(head.Headers.CacheControl?.NoStore);
        Assert.Equal(
            "{\"projects\":["
            + "{\"name\":\"Alpha\",\"available\":false,\"release\":null,\"commits\":null,\"log\":[],\"suggested\":null,\"bump\":null},"
            + "{\"name\":\"beta\",\"available\":false,\"release\":null,\"commits\":null,\"log\":[],\"suggested\":null,\"bump\":null}]}",
            await get.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("?x=1")]
    [InlineData("?project=Alpha")]
    [InlineData("?x")]
    public async Task Releases_WithAQueryParameter_Is400Invalid(string query)
    {
        using TempFactoryRoot root = new();
        root.AddProject("Alpha", "- [ ] A\n");
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();

        using HttpResponseMessage response = await client.GetAsync(Route + query);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("{\"error\":\"invalid\"}", await response.Content.ReadAsStringAsync());
        Assert.True(response.Headers.CacheControl?.NoStore);
    }

    [Fact]
    public async Task Releases_WithoutTheToken_Is401()
    {
        using TempFactoryRoot root = new();
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(Route);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("{\"error\":\"unauthorized\"}", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Releases_Post_IsRefused()
    {
        using TempFactoryRoot root = new();
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();

        using HttpResponseMessage response = await client.PostAsync(Route, new StringContent("{}"));

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    [Fact]
    public async Task Release_IsTheHighestStrictSemVerTagWithItsCreatorDate()
    {
        using TempFactoryRoot root = new();
        string repo = Repo(root, "Alpha");
        foreach (string tag in new[] { "v0.1.0", "v0.2.0", "v0.10.0" })
        {
            Commit(repo, "work " + tag);
            TempFactoryRoot.Git(repo, "tag", tag);
        }

        long tagDate = long.Parse(TempFactoryRoot.Git(repo, "log", "-1", "--format=%ct"), CultureInfo.InvariantCulture);
        foreach (string tag in new[] { "v0.11.0-develop.3", "v1.0", "x1.0.0", "v1.0.0-rc.1", "v01.0.0" })
        {
            Commit(repo, "work " + tag);
            TempFactoryRoot.Git(repo, "tag", tag);
        }

        JsonElement project = await First(root);

        Assert.True(project.GetProperty("available").GetBoolean());
        JsonElement release = project.GetProperty("release");
        Assert.Equal("v0.10.0", release.GetProperty("tag").GetString());
        Assert.Equal(
            DateTimeOffset.FromUnixTimeSeconds(tagDate).UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
            release.GetProperty("date").GetString());
        Assert.Equal(5, project.GetProperty("commits").GetInt32());
    }

    [Fact]
    public async Task Release_WithoutAReleaseTag_IsNullAndCountsAllOfDevelop()
    {
        using TempFactoryRoot root = new();
        string repo = Repo(root, "Alpha");
        TempFactoryRoot.Git(repo, "tag", "v1.0.0-rc.1");
        Commit(repo, "second");

        JsonElement project = await First(root);

        Assert.True(project.GetProperty("available").GetBoolean());
        Assert.Equal(JsonValueKind.Null, project.GetProperty("release").ValueKind);
        Assert.Equal(2, project.GetProperty("commits").GetInt32());
        Assert.Equal(2, project.GetProperty("log").GetArrayLength());
    }

    [Fact]
    public async Task Commits_CountMergedAndPlainCommitsWhileTheLogIsFirstParentNewestFirst()
    {
        using TempFactoryRoot root = new();
        string repo = Repo(root, "Alpha");
        TempFactoryRoot.Git(repo, "tag", "v0.1.0");
        Commit(repo, "plain one");
        Merge(repo, "12-x", 2);
        Merge(repo, "backlog", 1);
        Merge(repo, "Bad_Id", 1);

        JsonElement project = await First(root);

        // 1 plain + (2 + merge) + (1 + merge) + (1 + merge) = 8 commits; first-parent rows: 4.
        Assert.Equal(8, project.GetProperty("commits").GetInt32());
        JsonElement[] log = project.GetProperty("log").EnumerateArray().ToArray();
        string[] expected = ["factory: merge Bad_Id", "factory: merge backlog", "factory: merge 12-x", "plain one"];
        Assert.Equal(expected, log.Select(row => row.GetProperty("subject").GetString() ?? "").ToArray());
        Assert.Equal(JsonValueKind.Null, log[0].GetProperty("job").ValueKind);
        Assert.Equal(JsonValueKind.Null, log[1].GetProperty("job").ValueKind);
        Assert.Equal("12-x", log[2].GetProperty("job").GetString());
        Assert.Equal(JsonValueKind.Null, log[3].GetProperty("job").ValueKind);
        Assert.All(log, row => Assert.Matches("^[0-9a-f]{7}$", row.GetProperty("sha").GetString()));
        string head = TempFactoryRoot.Git(repo, "rev-parse", "refs/heads/develop");
        Assert.Equal(head[..7], log[0].GetProperty("sha").GetString());
    }

    [Fact]
    public async Task Log_KeepsTheNewest50Rows()
    {
        using TempFactoryRoot root = new();
        string repo = Repo(root, "Alpha");
        TempFactoryRoot.Git(repo, "tag", "v0.1.0");
        for (int i = 1; i <= 55; i++)
        {
            Commit(repo, "c" + i);
        }

        JsonElement project = await First(root);

        Assert.Equal(55, project.GetProperty("commits").GetInt32());
        JsonElement[] log = project.GetProperty("log").EnumerateArray().ToArray();
        Assert.Equal(50, log.Length);
        Assert.Equal("c55", log[0].GetProperty("subject").GetString());
        Assert.Equal("c6", log[49].GetProperty("subject").GetString());
    }

    [Fact]
    public async Task Log_CutsSubjectsTo200CharsAndDropsControlCharacters()
    {
        using TempFactoryRoot root = new();
        string repo = Repo(root, "Alpha");
        TempFactoryRoot.Git(repo, "tag", "v0.1.0");
        string longSubject = new string('a', 120) + new string('b', 130);
        Commit(repo, longSubject);
        Commit(repo, "tab\there");

        JsonElement project = await First(root);

        JsonElement[] log = project.GetProperty("log").EnumerateArray().ToArray();
        Assert.Equal("tabhere", log[0].GetProperty("subject").GetString());
        Assert.Equal(longSubject[..200], log[1].GetProperty("subject").GetString());
    }

    [Fact]
    public void CleanSubject_NeverSplitsASurrogatePair()
    {
        string subject = new string('a', 199) + "\U0001F600" + "b";

        Assert.Equal(new string('a', 199), ReleasesEndpoint.CleanSubject(subject));
    }

    [Theory]
    [InlineData("factory: merge 12-x", "12-x")]
    [InlineData("factory: merge backlog", null)]
    [InlineData("factory: merge Bad_Id", null)]
    [InlineData("factory: merge 12-x extra", null)]
    [InlineData("Factory: merge 12-x", null)]
    [InlineData(" factory: merge 12-x", null)]
    public void MergedJob_OnlyForAnExactSubjectWithAValidId(string subject, string? expected)
    {
        Assert.Equal(expected, ReleasesEndpoint.MergedJob(subject));
    }

    [Fact]
    public async Task Bump_FeatureJob_GivesMinor()
    {
        using TempFactoryRoot root = new();
        CategoryRepo(root, ("bug", "1-fix"), ("feature", "2-new"), ("tidy-up", "3-tidy"));

        JsonElement project = await First(root);

        Assert.Equal("minor", project.GetProperty("bump").GetString());
        Assert.Equal("1.3.0", project.GetProperty("suggested").GetString());
    }

    [Fact]
    public async Task Bump_OnlyBugAndTidyUpJobs_GivesPatch()
    {
        using TempFactoryRoot root = new();
        CategoryRepo(root, ("bug", "1-fix"), ("tidy-up", "3-tidy"));

        JsonElement project = await First(root);

        Assert.Equal("patch", project.GetProperty("bump").GetString());
        Assert.Equal("1.2.4", project.GetProperty("suggested").GetString());
    }

    [Fact]
    public async Task Bump_JobWithoutAStateFile_GivesMinor()
    {
        using TempFactoryRoot root = new();
        string repo = CategoryRepo(root, ("bug", "1-fix"));
        Merge(repo, "9-unknown", 1);

        JsonElement project = await First(root);

        Assert.Equal("minor", project.GetProperty("bump").GetString());
        Assert.Equal("1.3.0", project.GetProperty("suggested").GetString());
    }

    [Theory]
    [InlineData("{\"project\":\"Alpha\",\"title\":\"Fix CI build\",\"stage\":\"done\"}")]
    [InlineData("{\"project\":\"Alpha\",\"title\":\"#77 Not in the backlog\",\"stage\":\"done\"}")]
    [InlineData("{\"project\":\"Alpha\",\"title\":\"#4 No category\",\"stage\":\"done\"}")]
    public async Task Bump_JobWhoseCategoryCannotBeFound_GivesMinor(string state)
    {
        using TempFactoryRoot root = new();
        string repo = CategoryRepo(root, ("bug", "1-fix"));
        root.AddJob("8-other", state);
        Merge(repo, "8-other", 1);

        JsonElement project = await First(root);

        Assert.Equal("minor", project.GetProperty("bump").GetString());
    }

    [Fact]
    public async Task Bump_OnlyBacklogMerges_IsNull()
    {
        using TempFactoryRoot root = new();
        string repo = Repo(root, "Alpha");
        TempFactoryRoot.Git(repo, "tag", "v1.2.3");
        Merge(repo, "backlog", 1);
        Commit(repo, "backlog: move #3");

        JsonElement project = await First(root);

        Assert.True(project.GetProperty("available").GetBoolean());
        Assert.Equal(3, project.GetProperty("commits").GetInt32());
        Assert.Equal(JsonValueKind.Null, project.GetProperty("bump").ValueKind);
        Assert.Equal(JsonValueKind.Null, project.GetProperty("suggested").ValueKind);
    }

    [Fact]
    public async Task Bump_NothingSinceTheTag_IsNull()
    {
        using TempFactoryRoot root = new();
        string repo = Repo(root, "Alpha");
        TempFactoryRoot.Git(repo, "tag", "v1.2.3");

        JsonElement project = await First(root);

        Assert.Equal(0, project.GetProperty("commits").GetInt32());
        Assert.Equal(0, project.GetProperty("log").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, project.GetProperty("bump").ValueKind);
    }

    [Theory]
    [InlineData("feature", "0.1.0", "minor")]
    [InlineData("bug", "0.0.1", "patch")]
    public async Task Suggested_WithoutATag_StartsFromZero(string category, string suggested, string bump)
    {
        using TempFactoryRoot root = new();
        CategoryRepo(root, null, (category, "1-only"));

        JsonElement project = await First(root);

        Assert.Equal(JsonValueKind.Null, project.GetProperty("release").ValueKind);
        Assert.Equal(bump, project.GetProperty("bump").GetString());
        Assert.Equal(suggested, project.GetProperty("suggested").GetString());
    }

    [Fact]
    public async Task Unavailable_ForNoOwnRepoOrNoDevelop_WhileAHealthyProjectIsStillAnswered()
    {
        using TempFactoryRoot root = new();
        string healthy = Repo(root, "Healthy");
        TempFactoryRoot.Git(healthy, "tag", "v0.1.0");
        Commit(healthy, "one");
        root.AddProject("NoGit", "- [ ] A\n");
        string noDevelop = root.AddProject("NoDevelop", "- [ ] A\n");
        TempFactoryRoot.InitGit(noDevelop, "main");
        TempFactoryRoot.Git(noDevelop, "tag", "v1.0.0");
        string[] repos = [healthy, noDevelop];
        string[] before = repos.Select(RepoState).ToArray();
        IReadOnlyList<string> files = root.Snapshot();
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();

        string body = await client.GetStringAsync(Route);

        Assert.Equal(files, root.Snapshot());
        Assert.Equal(before, repos.Select(RepoState).ToArray());
        using JsonDocument json = JsonDocument.Parse(body);
        Dictionary<string, JsonElement> byName = json.RootElement.GetProperty("projects").EnumerateArray()
            .ToDictionary(p => p.GetProperty("name").GetString()!, p => p.Clone());
        const string Unavailable =
            ",\"available\":false,\"release\":null,\"commits\":null,\"log\":[],\"suggested\":null,\"bump\":null}";
        Assert.Equal("{\"name\":\"NoGit\"" + Unavailable, byName["NoGit"].GetRawText());
        Assert.Equal("{\"name\":\"NoDevelop\"" + Unavailable, byName["NoDevelop"].GetRawText());
        Assert.True(byName["Healthy"].GetProperty("available").GetBoolean());
        Assert.Equal(1, byName["Healthy"].GetProperty("commits").GetInt32());
    }

    [Fact]
    public async Task Unavailable_FolderInsideAnotherRepo_IsNotRead()
    {
        using TempFactoryRoot root = new();
        string inner = root.AddProject("Inner", "- [ ] A\n");
        TempFactoryRoot.InitGit(root.Path, "develop");

        Assert.Null(await ControlRoom.Server.ProjectGit.Tags(inner));
        Assert.Null(await ControlRoom.Server.ProjectGit.CommitsSince(inner, null));
        Assert.Null(await ControlRoom.Server.ProjectGit.LogSince(inner, null));
    }

    [Theory]
    [InlineData("-v1.0.0")]
    [InlineData("v1.0.0..HEAD")]
    [InlineData("v1.0")]
    [InlineData("--all")]
    public async Task GitCalls_RefuseATagThatIsNoReleaseTag(string tag)
    {
        using TempFactoryRoot root = new();
        string repo = Repo(root, "Alpha");

        Assert.False(ControlRoom.Server.ProjectGit.IsReleaseTag(tag));
        Assert.Null(await ControlRoom.Server.ProjectGit.CommitsSince(repo, tag));
        Assert.Null(await ControlRoom.Server.ProjectGit.LogSince(repo, tag));
    }

    [Fact]
    public async Task Releases_MakesNoOutboundCall()
    {
        using TempFactoryRoot root = new();
        Repo(root, "Alpha");
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();
        await client.GetStringAsync("/api/version");
        int updates = factory.Update.Requests.Count;
        int pushes = factory.Push.Requests.Count;

        using HttpResponseMessage response = await client.GetAsync(Route);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(updates, factory.Update.Requests.Count);
        Assert.Equal(pushes, factory.Push.Requests.Count);
    }

    [Fact]
    public void Source_StartsOnlyTheThreeFixedGitCallsAndNoHttpClient()
    {
        string folder = AccessTokenTests.SourceFolder();
        string endpoint = File.ReadAllText(Path.Combine(folder, "ReleasesEndpoint.cs"));
        Assert.DoesNotContain("\"gh\"", endpoint, StringComparison.Ordinal);
        Assert.DoesNotContain("HttpClient", endpoint, StringComparison.Ordinal);
        Assert.DoesNotContain("ProcessStartInfo", endpoint, StringComparison.Ordinal);
        Assert.DoesNotContain("Process.", endpoint, StringComparison.Ordinal);
        Assert.DoesNotContain("ProjectGit.Run", endpoint, StringComparison.Ordinal);
        string git = File.ReadAllText(Path.Combine(folder, "ProjectGit.cs"));
        Assert.Contains(
            "[\"for-each-ref\", \"--format=%(refname:strip=2)%09%(creatordate:unix)\", \"refs/tags/\"]", git, StringComparison.Ordinal);
        Assert.Contains("[\"rev-list\", \"--count\", range]", git, StringComparison.Ordinal);
        Assert.Contains(
            "[\"log\", \"--first-parent\", \"--format=%h%x1f%s\", \"--max-count=\" + MaxLog, range]", git, StringComparison.Ordinal);
        Assert.Contains("public const int MaxLog = 1000;", git, StringComparison.Ordinal);
        Assert.Contains("return IsReleaseTag(tag) ? \"refs/tags/\" + tag + \"..refs/heads/\" + Branch : null;", git, StringComparison.Ordinal);
    }

    /// <summary>A project with a repo on <c>develop</c> holding one commit.</summary>
    private static string Repo(TempFactoryRoot root, string name)
    {
        string folder = root.AddProject(name, "- [ ] A\n");
        TempFactoryRoot.InitGit(folder, "develop");
        return folder;
    }

    /// <summary>Project Alpha with backlog items 1 to 4 (item 4 without a category), tag <c>v1.2.3</c> unless
    /// <paramref name="tag"/> is <c>null</c>, and for each <c>(category, id)</c> a done job titled <c>#N …</c> (N the
    /// id's number) merged into develop after the tag.</summary>
    private static string CategoryRepo(TempFactoryRoot root, params (string Category, string Id)[] jobs)
    {
        return CategoryRepo(root, "v1.2.3", jobs);
    }

    private static string CategoryRepo(TempFactoryRoot root, string? tag, params (string Category, string Id)[] jobs)
    {
        string[] categories = ["bug", "feature", "tidy-up"];
        string backlog = "";
        for (int n = 1; n <= 4; n++)
        {
            backlog += "- [x] [#" + n + "](backlog/00" + n + "-item.md) Item " + n + "\n";
        }

        string folder = root.AddProject("Alpha", backlog);
        for (int n = 1; n <= 4; n++)
        {
            string category = n <= 3 ? "Category: " + categories[n - 1] + "\n\n" : "";
            TempFactoryRoot.AddStory(folder, "00" + n + "-item.md", "# Item " + n + "\n\n" + category + "## What\n\nX\n");
        }

        TempFactoryRoot.InitGit(folder, "develop");
        if (tag is not null)
        {
            TempFactoryRoot.Git(folder, "tag", tag);
        }

        foreach ((string category, string id) in jobs)
        {
            int number = Array.IndexOf(categories, category) + 1;
            root.AddJob(id, "{\"project\":\"Alpha\",\"title\":\"#" + number + " Item\",\"stage\":\"done\"}");
            Merge(folder, id, 1);
        }

        return folder;
    }

    private static void Commit(string repo, string subject)
    {
        TempFactoryRoot.Git(repo, "commit", "-q", "--allow-empty", "-m", subject);
    }

    /// <summary>A branch with <paramref name="commits"/> commits off develop, merged back with
    /// <c>--no-ff</c> as <c>factory: merge &lt;id&gt;</c>.</summary>
    private static void Merge(string repo, string id, int commits)
    {
        string branch = "work/" + id;
        TempFactoryRoot.Git(repo, "checkout", "-q", "-b", branch);
        for (int i = 0; i < commits; i++)
        {
            Commit(repo, id + " step " + i);
        }

        TempFactoryRoot.Git(repo, "checkout", "-q", "develop");
        TempFactoryRoot.Git(repo, "merge", "-q", "--no-ff", "-m", "factory: merge " + id, branch);
    }

    private static async Task<JsonElement> First(TempFactoryRoot root)
    {
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();
        using JsonDocument json = JsonDocument.Parse(await client.GetStringAsync(Route));
        return json.RootElement.GetProperty("projects")[0].Clone();
    }

    private static string RepoState(string repo)
    {
        return TempFactoryRoot.Git(repo, "status", "--porcelain", "--untracked-files=all") + "|"
            + TempFactoryRoot.Git(repo, "rev-parse", "HEAD") + "|"
            + TempFactoryRoot.Git(repo, "for-each-ref", "--format=%(refname) %(objectname)");
    }
}
