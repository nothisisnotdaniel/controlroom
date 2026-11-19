using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ControlRoom.Server.Tests;

/// <summary><c>GET /api/changes</c> (<see cref="ChangesEndpoint"/>, <see cref="ChangeFingerprint"/>): the response
/// shape, the gates, and which value changes for which file. Timestamps are set explicitly, never waited for.</summary>
public sealed partial class ChangesApiTests
{
    private static readonly DateTime Later = new(2030, 1, 2, 3, 4, 5, DateTimeKind.Utc);

    private const string State = "{\"project\":\"Alpha\",\"stage\":\"build\"}";

    [Fact]
    public async Task Changes_ReturnsTheExactShapeWithNoStore()
    {
        using TempFactoryRoot root = new();
        root.AddProject("Alpha", "- [ ] One\n");
        root.AddProject("Beta", null);
        root.AddJob("1-first", State);
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();

        using HttpResponseMessage response = await client.GetAsync("/api/changes");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.True(response.Headers.CacheControl?.NoStore);
        string body = await response.Content.ReadAsStringAsync();
        Assert.Matches(Shape(), body);
        Assert.Contains("\"Alpha\":{", body, StringComparison.Ordinal);
        Assert.Contains("\"Beta\":{", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Changes_HasOneEntryPerDiscoveredProject()
    {
        using TempFactoryRoot root = TempFactoryRoot.WithSamples();
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();

        using JsonDocument document = await Get(client);

        string[] names = document.RootElement.GetProperty("projects").EnumerateObject().Select(p => p.Name).ToArray();
        string[] expected = ProjectFinder.Find(root.Path).Select(p => p.Name).ToArray();
        Assert.Equal(expected, names);
        Assert.DoesNotContain("factory", names);
        Assert.DoesNotContain("LinkedFolder", names);
    }

    [Fact]
    public async Task Changes_WithoutTheToken_Is401()
    {
        using TempFactoryRoot root = new();
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync("/api/changes");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("{\"error\":\"unauthorized\"}", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Changes_WithABadHost_Is421()
    {
        using TempFactoryRoot root = new();
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();
        using HttpRequestMessage request = new(HttpMethod.Get, "/api/changes");
        request.Headers.Host = "evil.example:8100";

        using HttpResponseMessage response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.MisdirectedRequest, response.StatusCode);
    }

    [Fact]
    public async Task Changes_Post_Is405()
    {
        using TempFactoryRoot root = new();
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();

        using HttpResponseMessage response = await client.PostAsync("/api/changes", new StringContent("{}"));

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    [Fact]
    public async Task Changes_Head_IsAllowed()
    {
        using TempFactoryRoot root = new();
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();

        using HttpResponseMessage response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Head, "/api/changes"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public void Jobs_StaysEqualWhenNothingChanged()
    {
        using TempFactoryRoot root = new();
        root.AddJob("1-first", State);
        root.AddJob("2-second", State);

        Assert.Equal(ChangeFingerprint.Jobs(root.Path), ChangeFingerprint.Jobs(root.Path));
    }

    [Fact]
    public void Jobs_ChangesWhenAStateIsRewrittenAddedOrRemoved()
    {
        using TempFactoryRoot root = new();
        string folder = root.AddJob("1-first", State);
        string state = Path.Combine(folder, "state.json");
        string before = ChangeFingerprint.Jobs(root.Path);

        File.WriteAllText(state, State.Replace("build", "review", StringComparison.Ordinal));
        string rewritten = ChangeFingerprint.Jobs(root.Path);
        Assert.NotEqual(before, rewritten);

        File.SetLastWriteTimeUtc(state, Later);
        string touched = ChangeFingerprint.Jobs(root.Path);
        Assert.NotEqual(rewritten, touched);

        string second = Path.Combine(root.AddJob("2-second", State), "state.json");
        string added = ChangeFingerprint.Jobs(root.Path);
        Assert.NotEqual(touched, added);

        File.Delete(second);
        Assert.NotEqual(added, ChangeFingerprint.Jobs(root.Path));
    }

    [Fact]
    public void Jobs_IgnoresFoldersThatListFoldersRefusesAndOtherRecords()
    {
        using TempFactoryRoot root = new();
        root.AddJob("1-first", State);
        string before = ChangeFingerprint.Jobs(root.Path);

        root.AddJob("Not-A-Job", State);
        root.AddRecord("1-first", "build.md", "# Build\n");

        Assert.Equal(before, ChangeFingerprint.Jobs(root.Path));
    }

    [Fact]
    public void Jobs_SymlinkedStateAddsOnlyTheLinkMarker()
    {
        using TempFactoryRoot root = new();
        string outside = Path.Combine(root.Path, ".outside");
        Directory.CreateDirectory(outside);
        string target = Path.Combine(outside, "state.json");
        File.WriteAllText(target, State);
        string folder = root.AddJob("1-first", null);
        File.CreateSymbolicLink(Path.Combine(folder, "state.json"), target);
        string before = ChangeFingerprint.Jobs(root.Path);

        File.WriteAllText(target, State + "  ");
        File.SetLastWriteTimeUtc(target, Later);

        Assert.Equal(before, ChangeFingerprint.Jobs(root.Path));
    }

    [Fact]
    public void Backlog_ChangesOnlyForItsOwnProject()
    {
        using TempFactoryRoot root = new();
        string alpha = root.AddProject("Alpha", "- [ ] One\n");
        root.AddProject("Beta", "- [ ] Two\n");
        ChangesResponse before = ChangeFingerprint.Build(root.Path);
        Assert.Equal(before, ChangeFingerprint.Build(root.Path), new ResponseComparer());

        File.WriteAllText(Path.Combine(alpha, "backlog.md"), "- [ ] One\n- [ ] Three\n");
        ChangesResponse after = ChangeFingerprint.Build(root.Path);

        Assert.NotEqual(before.Projects["Alpha"].Backlog, after.Projects["Alpha"].Backlog);
        Assert.Equal(before.Projects["Alpha"].Stories, after.Projects["Alpha"].Stories);
        Assert.Equal(before.Projects["Beta"], after.Projects["Beta"]);
        Assert.Equal(before.Jobs, after.Jobs);

        File.SetLastWriteTimeUtc(Path.Combine(alpha, "backlog.md"), Later);
        Assert.NotEqual(after.Projects["Alpha"].Backlog, ChangeFingerprint.Build(root.Path).Projects["Alpha"].Backlog);
    }

    [Fact]
    public void Backlog_MissingFileAddsTheMissingMarker()
    {
        using TempFactoryRoot root = new();
        string alpha = root.AddProject("Alpha", null);
        ProjectFolder project = new("Alpha", alpha);
        string missing = ChangeFingerprint.Backlog(project);

        File.WriteAllText(Path.Combine(alpha, "backlog.md"), "- [ ] One\n");
        string present = ChangeFingerprint.Backlog(project);
        Assert.NotEqual(missing, present);

        File.Delete(Path.Combine(alpha, "backlog.md"));
        Assert.Equal(missing, ChangeFingerprint.Backlog(project));
    }

    [Fact]
    public void Backlog_SymlinkIsNeverFollowed()
    {
        using TempFactoryRoot root = new();
        string target = OutsideFile(root, "backlog.md");
        string alpha = root.AddProject("Alpha", null);
        File.CreateSymbolicLink(Path.Combine(alpha, "backlog.md"), target);
        ProjectFolder project = new("Alpha", alpha);
        string before = ChangeFingerprint.Backlog(project);

        File.WriteAllText(target, "- [ ] Changed and longer\n");
        File.SetLastWriteTimeUtc(target, Later);

        Assert.Equal(before, ChangeFingerprint.Backlog(project));
    }

    [Fact]
    public void Stories_ChangeOnlyForItsOwnProjectAndOnlyForMarkdownFiles()
    {
        using TempFactoryRoot root = new();
        string alpha = root.AddProject("Alpha", "- [ ] One\n");
        string beta = root.AddProject("Beta", "- [ ] Two\n");
        string story = TempFactoryRoot.AddStory(alpha, "001-one.md", "# One\n");
        TempFactoryRoot.AddStory(beta, "001-two.md", "# Two\n");
        ChangesResponse before = ChangeFingerprint.Build(root.Path);
        Assert.Equal(before.Projects["Alpha"].Stories, ChangeFingerprint.Build(root.Path).Projects["Alpha"].Stories);

        File.WriteAllText(story, "# One\n\nMore text.\n");
        ChangesResponse rewritten = ChangeFingerprint.Build(root.Path);
        Assert.NotEqual(before.Projects["Alpha"].Stories, rewritten.Projects["Alpha"].Stories);
        Assert.Equal(before.Projects["Alpha"].Backlog, rewritten.Projects["Alpha"].Backlog);
        Assert.Equal(before.Projects["Beta"], rewritten.Projects["Beta"]);

        File.SetLastWriteTimeUtc(story, Later);
        ChangesResponse touched = ChangeFingerprint.Build(root.Path);
        Assert.NotEqual(rewritten.Projects["Alpha"].Stories, touched.Projects["Alpha"].Stories);

        TempFactoryRoot.AddStory(alpha, "notes.txt", "not a story\n");
        Assert.Equal(touched.Projects["Alpha"].Stories, ChangeFingerprint.Build(root.Path).Projects["Alpha"].Stories);

        string added = TempFactoryRoot.AddStory(alpha, "002-two.md", "# Two\n");
        ChangesResponse withNew = ChangeFingerprint.Build(root.Path);
        Assert.NotEqual(touched.Projects["Alpha"].Stories, withNew.Projects["Alpha"].Stories);

        File.Delete(added);
        Assert.Equal(touched.Projects["Alpha"].Stories, ChangeFingerprint.Build(root.Path).Projects["Alpha"].Stories);
    }

    [Fact]
    public void Stories_MissingFolderAddsTheMissingMarker()
    {
        using TempFactoryRoot root = new();
        string alpha = root.AddProject("Alpha", null);
        string beta = root.AddProject("Beta", null);
        Directory.CreateDirectory(Path.Combine(beta, "backlog"));

        string missing = ChangeFingerprint.Stories(new ProjectFolder("Alpha", alpha));
        string empty = ChangeFingerprint.Stories(new ProjectFolder("Beta", beta));

        Assert.NotEqual(missing, empty);
        Assert.Equal(missing, ChangeFingerprint.Stories(new ProjectFolder("Other", alpha)));
    }

    [Fact]
    public void Stories_SymlinkedStoryIsNeverFollowed()
    {
        using TempFactoryRoot root = new();
        string target = OutsideFile(root, "story.md");
        string alpha = root.AddProject("Alpha", null);
        Directory.CreateDirectory(Path.Combine(alpha, "backlog"));
        File.CreateSymbolicLink(Path.Combine(alpha, "backlog", "001-one.md"), target);
        ProjectFolder project = new("Alpha", alpha);
        string before = ChangeFingerprint.Stories(project);

        File.WriteAllText(target, "# Changed and longer\n");
        File.SetLastWriteTimeUtc(target, Later);

        Assert.Equal(before, ChangeFingerprint.Stories(project));
    }

    [Fact]
    public void Stories_SymlinkedFolderIsNeverFollowed()
    {
        using TempFactoryRoot root = new();
        string outside = Path.Combine(root.Path, ".outside");
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "001-one.md"), "# One\n");
        string alpha = root.AddProject("Alpha", null);
        Directory.CreateSymbolicLink(Path.Combine(alpha, "backlog"), outside);
        ProjectFolder project = new("Alpha", alpha);
        string before = ChangeFingerprint.Stories(project);

        File.WriteAllText(Path.Combine(outside, "001-one.md"), "# Changed and longer\n");
        File.WriteAllText(Path.Combine(outside, "002-two.md"), "# Two\n");

        Assert.Equal(before, ChangeFingerprint.Stories(project));
        Assert.NotEqual(ChangeFingerprint.Stories(new ProjectFolder("Missing", Path.Combine(root.Path, "none"))), before);
    }

    [Fact]
    public void Build_ReadsNoFileContent()
    {
        using TempFactoryRoot root = TempFactoryRoot.WithSamples();
        string alpha = root.AddProject("Alpha", "- [ ] One\n");
        string story = TempFactoryRoot.AddStory(alpha, "001-one.md", "# One\n");
        string state = Path.Combine(root.AddJob("1-first", State), "state.json");
        root.Lock(story);
        root.Lock(state);
        root.Lock(Path.Combine(alpha, "backlog.md"));
        IReadOnlyList<string> snapshot = root.Snapshot();

        ChangesResponse first = ChangeFingerprint.Build(root.Path);
        ChangesResponse second = ChangeFingerprint.Build(root.Path);

        Assert.Equal(first, second, new ResponseComparer());
        Assert.Equal(snapshot, root.Snapshot());
    }

    [Fact]
    public void NoFollowStat_ReadsTheKindLengthAndLastWriteTime()
    {
        using TempFactoryRoot root = new();
        string file = Path.Combine(root.Path, "file.md");
        File.WriteAllText(file, "twelve bytes");
        File.SetLastWriteTimeUtc(file, Later);
        string link = Path.Combine(root.Path, "link.md");
        File.CreateSymbolicLink(link, file);

        Assert.Equal(new PathStat(PathKind.File, 12, Later.Ticks), NoFollowStat.Of(file));
        Assert.Equal(PathKind.Link, NoFollowStat.Of(link).Kind);
        Assert.Equal(PathKind.Directory, NoFollowStat.Of(root.Path).Kind);
        Assert.Equal(PathKind.Missing, NoFollowStat.Of(Path.Combine(root.Path, "none")).Kind);
        IReadOnlyList<(string Name, PathStat Stat)>? listed = NoFollowStat.ListFolder(root.Path, ".md");
        Assert.NotNull(listed);
        Assert.Equal(["file.md", "link.md"], listed.Select(entry => entry.Name).ToArray());
        Assert.Equal(new PathStat(PathKind.File, 12, Later.Ticks), listed[0].Stat);
        Assert.Equal(PathKind.Link, listed[1].Stat.Kind);
        Assert.Null(NoFollowStat.ListFolder(link, ".md"));
    }

    private static string OutsideFile(TempFactoryRoot root, string name)
    {
        string outside = Path.Combine(root.Path, ".outside");
        Directory.CreateDirectory(outside);
        string target = Path.Combine(outside, name);
        File.WriteAllText(target, "- [ ] Outside\n");
        return target;
    }

    private static async Task<JsonDocument> Get(HttpClient client)
    {
        using HttpResponseMessage response = await client.GetAsync("/api/changes");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    private sealed class ResponseComparer : IEqualityComparer<ChangesResponse>
    {
        public bool Equals(ChangesResponse? x, ChangesResponse? y)
        {
            return x is not null && y is not null && x.Jobs == y.Jobs
                && x.Projects.Count == y.Projects.Count
                && x.Projects.All(pair => y.Projects.TryGetValue(pair.Key, out ProjectChanges? other) && other == pair.Value);
        }

        public int GetHashCode(ChangesResponse obj)
        {
            return obj.Jobs.GetHashCode(StringComparison.Ordinal);
        }
    }

    [GeneratedRegex(
        "\\A\\{\"jobs\":\"[0-9a-f]{64}\",\"projects\":\\{(\"[^\"]+\":\\{\"backlog\":\"[0-9a-f]{64}\",\"stories\":\"[0-9a-f]{64}\"\\}(,(?=\"))?)*\\}\\}\\z")]
    private static partial Regex Shape();
}
