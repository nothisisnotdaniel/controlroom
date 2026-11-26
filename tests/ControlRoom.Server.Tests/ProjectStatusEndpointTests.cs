using System.Net;
using System.Text.Json;

namespace ControlRoom.Server.Tests;

/// <summary><c>GET /api/status</c>: shape, order and route rules, the CI pick and field filters from job records,
/// the develop/main count from real local repos (read-only), and that no outbound call or other program is
/// used.</summary>
public sealed class ProjectStatusEndpointTests
{
    private const string Route = "/api/status";

    [Fact]
    public async Task Status_ListsEveryProjectInFinderOrderWithNoStore()
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
            "{\"projects\":[{\"name\":\"Alpha\",\"ci\":null,\"ahead\":null},{\"name\":\"beta\",\"ci\":null,\"ahead\":null}]}",
            await get.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("?x=1")]
    [InlineData("?project=Alpha")]
    [InlineData("?x")]
    public async Task Status_WithAQueryParameter_Is400Invalid(string query)
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
    public async Task Status_WithoutTheToken_Is401()
    {
        using TempFactoryRoot root = new();
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(Route);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("{\"error\":\"unauthorized\"}", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Status_Post_IsRefused()
    {
        using TempFactoryRoot root = new();
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();

        using HttpResponseMessage response = await client.PostAsync(Route, new StringContent("{}"));

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    [Fact]
    public async Task Ci_IsFromTheHighestNumberedJobWithAValidCi()
    {
        using TempFactoryRoot root = new();
        root.AddProject("Alpha", "- [ ] A\n");
        root.AddProject("alpha2", "- [ ] A\n");
        root.AddJob("9-a", State("Alpha", "failure", "https://github.com/o/r/actions/runs/9", Sha('9'), "2026-11-10T08:00:00Z"));
        root.AddJob("10-b", State("Alpha", "timeout", "https://github.com/o/r/actions/runs/10", Sha('b'), "2026-11-11T08:00:00Z"));
        root.AddJob("10-c", State("Alpha", "success", "https://github.com/o/r/actions/runs/11", Sha('c'), "2026-11-12T09:30:05Z"));
        root.AddJob("11-no-ci", "{\"project\":\"Alpha\",\"updated\":\"2026-11-13T08:00:00Z\"}");
        root.AddJob("12-unknown", State("Alpha", "running", "https://github.com/o/r", Sha('d'), "2026-11-14T08:00:00Z"));
        root.AddJob("13-other", State("Beta", "failure", "https://github.com/o/r", Sha('e'), "2026-11-15T08:00:00Z"));
        root.AddJob("14-case", State("alpha", "failure", "https://github.com/o/r", Sha('f'), "2026-11-16T08:00:00Z"));
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();

        using JsonDocument json = JsonDocument.Parse(await client.GetStringAsync(Route));

        JsonElement alpha = json.RootElement.GetProperty("projects")[0];
        Assert.Equal("Alpha", alpha.GetProperty("name").GetString());
        Assert.Equal(
            "{\"status\":\"success\",\"url\":\"https://github.com/o/r/actions/runs/11\",\"sha\":\"ccccccc\",\"time\":\"2026-11-12T09:30:05Z\"}",
            alpha.GetProperty("ci").GetRawText());
        JsonElement other = json.RootElement.GetProperty("projects")[1];
        Assert.Equal("alpha2", other.GetProperty("name").GetString());
        Assert.Equal(JsonValueKind.Null, other.GetProperty("ci").ValueKind);
    }

    [Theory]
    [InlineData("http://github.com/o/r/actions/runs/1")]
    [InlineData("javascript:alert(1)")]
    [InlineData("https://user:pw@github.com/o/r")]
    [InlineData("https://user@github.com/o/r")]
    [InlineData("https://github.com:8443/o/r")]
    [InlineData("https://github.com:443/o/r")]
    [InlineData("https://github.com.evil.example/o/r")]
    [InlineData("https://gist.github.com/o/r")]
    [InlineData("https://github.com/o/r\nx")]
    [InlineData("/o/r")]
    [InlineData("")]
    public async Task Ci_UrlThatIsNotAPlainGithubHttpsUrl_IsNull(string url)
    {
        using TempFactoryRoot root = new();
        root.AddProject("Alpha", "- [ ] A\n");
        root.AddJob("1-a", State("Alpha", "success", url, Sha('a'), "2026-11-12T09:30:05Z"));
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();

        JsonElement ci = await FirstCi(client);

        Assert.Equal("success", ci.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, ci.GetProperty("url").ValueKind);
        Assert.Equal("aaaaaaa", ci.GetProperty("sha").GetString());
    }

    [Fact]
    public async Task Ci_UrlOver500Chars_IsNull()
    {
        using TempFactoryRoot root = new();
        root.AddProject("Alpha", "- [ ] A\n");
        string url = "https://github.com/" + new string('a', 482);
        Assert.Equal(501, url.Length);
        root.AddJob("1-a", State("Alpha", "skipped", url, Sha('a'), "2026-11-12T09:30:05Z"));
        root.AddJob("0-a", State("Alpha", "skipped", url[..500], Sha('a'), "2026-11-12T09:30:05Z"));
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();

        JsonElement ci = await FirstCi(client);

        Assert.Equal("skipped", ci.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, ci.GetProperty("url").ValueKind);
        Assert.Equal(url[..500], ProjectStatusEndpoint.SafeUrl(url[..500]));
    }

    [Theory]
    [InlineData("ABCDEF1")]
    [InlineData("abcdef")]
    [InlineData("abcdefg")]
    [InlineData("0123456789abcdef0123456789abcdef012345678")]
    [InlineData(" abcdef1")]
    public async Task Ci_BadSha_IsNull(string sha)
    {
        using TempFactoryRoot root = new();
        root.AddProject("Alpha", "- [ ] A\n");
        root.AddJob("1-a", State("Alpha", "success", "https://github.com/o/r", sha, "2026-11-12T09:30:05Z"));
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();

        JsonElement ci = await FirstCi(client);

        Assert.Equal(JsonValueKind.Null, ci.GetProperty("sha").ValueKind);
        Assert.Equal("https://github.com/o/r", ci.GetProperty("url").GetString());
    }

    [Theory]
    [InlineData("abcdef1", "abcdef1")]
    [InlineData("0123456789abcdef0123456789abcdef01234567", "0123456")]
    public void ShortSha_KeepsTheFirstSevenOfValidHex(string sha, string expected)
    {
        Assert.Equal(expected, ProjectStatusEndpoint.ShortSha(sha));
    }

    [Theory]
    [InlineData("2026-11-12 09:30:05Z")]
    [InlineData("2026-13-12T09:30:05Z")]
    [InlineData("2026-11-12T09:30:05+01:00")]
    public async Task Ci_BadUpdated_GivesNullTime(string updated)
    {
        using TempFactoryRoot root = new();
        root.AddProject("Alpha", "- [ ] A\n");
        root.AddJob("1-a", State("Alpha", "failure", "https://github.com/o/r", Sha('a'), updated));
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();

        JsonElement ci = await FirstCi(client);

        Assert.Equal("failure", ci.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, ci.GetProperty("time").ValueKind);
    }

    [Fact]
    public async Task Ci_OnlyUnknownStatuses_IsNull()
    {
        using TempFactoryRoot root = new();
        root.AddProject("Alpha", "- [ ] A\n");
        root.AddJob("1-a", State("Alpha", "Success", "https://github.com/o/r", Sha('a'), "2026-11-12T09:30:05Z"));
        root.AddJob("2-a", "{\"project\":\"Alpha\",\"ci\":\"success\"}");
        root.AddJob("3-a", "{\"project\":\"Alpha\",\"ci\":{\"status\":1}}");
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();

        using JsonDocument json = JsonDocument.Parse(await client.GetStringAsync(Route));

        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("projects")[0].GetProperty("ci").ValueKind);
    }

    [Fact]
    public async Task Board_And_Job_JsonDoNotGainCiUrlOrSha()
    {
        using TempFactoryRoot root = new();
        root.AddProject("Alpha", "- [ ] A\n");
        root.AddJob("1-a", State("Alpha", "success", "https://github.com/o/r", Sha('a'), "2026-11-12T09:30:05Z"));
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();

        string board = await client.GetStringAsync("/api/board");
        string job = await client.GetStringAsync("/api/job?id=1-a");

        Assert.DoesNotContain("ciUrl", board, StringComparison.Ordinal);
        Assert.DoesNotContain("ciSha", board, StringComparison.Ordinal);
        Assert.DoesNotContain("ciUrl", job, StringComparison.Ordinal);
        Assert.DoesNotContain("ciSha", job, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Ahead_CountsDevelopCommitsNotOnMainAndWritesNothing()
    {
        using TempFactoryRoot root = new();
        string level = root.AddProject("Level", "- [ ] A\n");
        TempFactoryRoot.InitGit(level, "main");
        TempFactoryRoot.Git(level, "branch", "develop");
        string ahead = root.AddProject("Three", "- [ ] A\n");
        TempFactoryRoot.InitGit(ahead, "main");
        TempFactoryRoot.Git(ahead, "checkout", "-q", "-b", "develop");
        for (int i = 0; i < 3; i++)
        {
            File.WriteAllText(Path.Combine(ahead, "f" + i + ".txt"), "x" + i);
            TempFactoryRoot.Git(ahead, "add", "f" + i + ".txt");
            TempFactoryRoot.Git(ahead, "commit", "-q", "-m", "c" + i);
        }

        File.WriteAllText(Path.Combine(ahead, "dirty.txt"), "untracked");
        File.AppendAllText(Path.Combine(ahead, "f0.txt"), "changed");
        string noMain = root.AddProject("NoMain", "- [ ] A\n");
        TempFactoryRoot.InitGit(noMain, "develop");
        string noDevelop = root.AddProject("NoDevelop", "- [ ] A\n");
        TempFactoryRoot.InitGit(noDevelop, "main");
        root.AddProject("NoGit", "- [ ] A\n");
        string[] repos = [level, ahead, noMain, noDevelop];
        string[] before = repos.Select(RepoState).ToArray();
        IReadOnlyList<string> files = root.Snapshot();
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();

        string body = await client.GetStringAsync(Route);

        Assert.Equal(files, root.Snapshot());
        Assert.Equal(before, repos.Select(RepoState).ToArray());
        Assert.All(repos, repo => Assert.False(File.Exists(Path.Combine(repo, ".git", "index.lock"))));
        using JsonDocument json = JsonDocument.Parse(body);
        Dictionary<string, JsonElement> byName = json.RootElement.GetProperty("projects").EnumerateArray()
            .ToDictionary(p => p.GetProperty("name").GetString()!, p => p.GetProperty("ahead"));
        Assert.Equal(0, byName["Level"].GetInt32());
        Assert.Equal(3, byName["Three"].GetInt32());
        Assert.Equal(JsonValueKind.Null, byName["NoMain"].ValueKind);
        Assert.Equal(JsonValueKind.Null, byName["NoDevelop"].ValueKind);
        Assert.Equal(JsonValueKind.Null, byName["NoGit"].ValueKind);
    }

    [Fact]
    public async Task AheadOfMain_FolderWithoutOwnGit_IsNullEvenInsideARepo()
    {
        using TempFactoryRoot root = new();
        string inner = root.AddProject("Inner", "- [ ] A\n");
        TempFactoryRoot.InitGit(root.Path, "main");
        TempFactoryRoot.Git(root.Path, "branch", "develop");

        Assert.Null(await RoomKit.ProjectGit.AheadOfMain(inner));
        Assert.Equal(0, await RoomKit.ProjectGit.AheadOfMain(root.Path));
    }

    [Fact]
    public async Task Status_MakesNoOutboundCall()
    {
        using TempFactoryRoot root = new();
        string alpha = root.AddProject("Alpha", "- [ ] A\n");
        TempFactoryRoot.InitGit(alpha, "main");
        TempFactoryRoot.Git(alpha, "branch", "develop");
        root.AddJob("1-a", State("Alpha", "failure", "https://github.com/o/r", Sha('a'), "2026-11-12T09:30:05Z"));
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
    public void Source_StartsOnlyGitAndNoHttpClient()
    {
        // ProjectGit.cs is in the RoomKit package now; RoomKit's own tests scan it.
        string endpoint = File.ReadAllText(Path.Combine(AccessTokenTests.SourceFolder(), "ProjectStatusEndpoint.cs"));
        Assert.DoesNotContain("\"gh\"", endpoint, StringComparison.Ordinal);
        Assert.DoesNotContain("\"curl\"", endpoint, StringComparison.Ordinal);
        Assert.DoesNotContain("HttpClient", endpoint, StringComparison.Ordinal);
        Assert.DoesNotContain("UseShellExecute = true", endpoint, StringComparison.Ordinal);
        Assert.DoesNotContain("ProcessStartInfo", endpoint, StringComparison.Ordinal);
        Assert.DoesNotContain("Process.", endpoint, StringComparison.Ordinal);
    }

    private static string RepoState(string repo)
    {
        return TempFactoryRoot.Git(repo, "status", "--porcelain", "--untracked-files=all") + "|"
            + TempFactoryRoot.Git(repo, "rev-parse", "HEAD") + "|"
            + TempFactoryRoot.Git(repo, "for-each-ref", "--format=%(refname) %(objectname)");
    }

    private static async Task<JsonElement> FirstCi(HttpClient client)
    {
        using JsonDocument json = JsonDocument.Parse(await client.GetStringAsync(Route));
        return json.RootElement.GetProperty("projects")[0].GetProperty("ci").Clone();
    }

    private static string Sha(char c)
    {
        return new string(c, 40);
    }

    private static string State(string project, string status, string url, string sha, string updated)
    {
        return JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["project"] = project,
            ["stage"] = "done",
            ["updated"] = updated,
            ["ci"] = new Dictionary<string, string> { ["status"] = status, ["sha"] = sha, ["url"] = url },
        });
    }
}
