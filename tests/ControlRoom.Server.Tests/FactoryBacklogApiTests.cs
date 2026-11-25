using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ControlRoom.Server.Tests;

/// <summary>The factory's own backlog (#147) in <c>/api/backlogs</c>, <c>/api/story</c> and
/// <c>/api/backlog/change</c>: read and written in the factory's develop worktree (<c>FACTORY_DEVELOP_DIR</c>, set
/// through <see cref="ServerFactory.FactoryDevelopDir"/>), on the local <c>backlog</c> branch while a factory job
/// runs, never in the root checkout or on <c>main</c>. Each test has its own temp root (a real git repo), its own
/// develop worktree and remotes in its own temp folder and its own token folder.</summary>
public sealed class FactoryBacklogApiTests
{
    private const string Route = "/api/backlog/change";

    private const string RootBacklog = "# Root copy\n\n- [ ] [#1](backlog/001-one.md) Root only\n";

    private const string RootStory = "# 001 Root only\n\nRoot story.\n";

    private const string DevelopBacklog =
        "# Factory backlog\n"                                    // 0
        + "- [x] [#1](backlog/001-one.md) Done one\n"            // 1
        + "- [ ] [#2](backlog/002-two.md) Open two\n"            // 2
        + "- [?] [#3](backlog/003-three.md) Proposal three\n"    // 3
        + "- [h] [#4](backlog/004-four.md) Hand four\n"          // 4
        + "- [ ] [#5](backlog/005-five.md) Five (after: #2)\n";  // 5

    private const string StoryTwo = "# 002 Open two\n\nCategory: bug\n\nDevelop story two.\n";

    public static TheoryData<string, bool> RunningStages()
    {
        TheoryData<string, bool> data = [];
        foreach (string stage in new[] { "spike", "spec", "build", "review", "approve", "needs-human" })
        {
            data.Add(stage, false);
            data.Add(stage, true);
        }

        return data;
    }

    [Fact]
    public async Task Backlogs_ListFactoryLastFromTheDevelopWorktree()
    {
        using Setup setup = Setup.Create(withAlpha: true);

        JsonElement factory = await setup.FactoryEntry();

        JsonElement[] projects = [.. (await setup.Backlogs()).RootElement.GetProperty("projects").EnumerateArray()];
        Assert.Equal(["Alpha", "factory"], projects.Select(project => project.GetProperty("name").GetString()));
        Assert.True(factory.GetProperty("readable").GetBoolean());
        Assert.False(factory.GetProperty("pending").GetBoolean());
        Assert.False(factory.TryGetProperty("reason", out _));
        Assert.False(projects[0].TryGetProperty("reason", out _));
        Assert.Equal(Sha(File.ReadAllBytes(setup.DevelopBacklogPath)), factory.GetProperty("version").GetString());
        Assert.Equal(
            [2, 5], factory.GetProperty("open").EnumerateArray().Select(entry => entry.GetProperty("number").GetInt32()));
        Assert.Equal(3, factory.GetProperty("approval")[0].GetProperty("number").GetInt32());
        Assert.Equal("bug", factory.GetProperty("open")[0].GetProperty("category").GetString());
        Assert.Equal(2, factory.GetProperty("next").GetProperty("number").GetInt32());
        Assert.Equal(RootBacklog, File.ReadAllText(setup.RootBacklogPath));
    }

    [Fact]
    public async Task Story_ComesFromTheDevelopWorktree()
    {
        using Setup setup = Setup.Create();
        using HttpClient client = setup.Factory.CreateTokenClient();

        using JsonDocument story = JsonDocument.Parse(await client.GetStringAsync("/api/story?project=factory&number=2"));
        Assert.Equal("factory", story.RootElement.GetProperty("project").GetString());
        Assert.Equal(StoryTwo, story.RootElement.GetProperty("markdown").GetString());
        Assert.Equal(Sha(Encoding.UTF8.GetBytes(StoryTwo)), story.RootElement.GetProperty("storyVersion").GetString());
        using HttpResponseMessage rootOnly = await client.GetAsync("/api/story?project=factory&number=9");
        Assert.Equal(HttpStatusCode.NotFound, rootOnly.StatusCode);
    }

    [Fact]
    public async Task MissingFolder_IsNoWorktreeAndNothingIsCreated()
    {
        using Setup setup = Setup.Create(develop: DevelopKind.Missing);
        string worktrees = TempFactoryRoot.Git(setup.Root.Path, "worktree", "list", "--porcelain");
        string branches = TempFactoryRoot.Git(setup.Root.Path, "for-each-ref", "--format=%(refname)", "refs/heads/");

        JsonElement factory = await setup.FactoryEntry();
        using HttpResponseMessage change = await setup.Post(new { action = "approve", line = 3 }, new string('0', 64));
        using HttpClient client = setup.Factory.CreateTokenClient();
        using HttpResponseMessage story = await client.GetAsync("/api/story?project=factory&number=1");

        Assert.False(factory.GetProperty("readable").GetBoolean());
        Assert.Equal("no_worktree", factory.GetProperty("reason").GetString());
        Assert.Equal(JsonValueKind.Null, factory.GetProperty("version").ValueKind);
        await AssertError(change, HttpStatusCode.Conflict, "no_worktree");
        Assert.Equal(HttpStatusCode.NotFound, story.StatusCode);
        Assert.False(Directory.Exists(setup.DevelopPath));
        Assert.False(Directory.Exists(setup.WorktreesFolder));
        Assert.Equal(worktrees, TempFactoryRoot.Git(setup.Root.Path, "worktree", "list", "--porcelain"));
        Assert.Equal(
            branches, TempFactoryRoot.Git(setup.Root.Path, "for-each-ref", "--format=%(refname)", "refs/heads/"));
    }

    [Theory]
    [InlineData(DevelopKind.PlainFolder)]
    [InlineData(DevelopKind.OtherBranch)]
    [InlineData(DevelopKind.Detached)]
    public async Task FolderNotOnDevelop_IsNotDevelopAndChangesAreRefused(DevelopKind kind)
    {
        using Setup setup = Setup.Create(develop: kind);
        string head = TempFactoryRoot.Git(setup.Root.Path, "rev-parse", "refs/heads/develop");
        byte[] before = File.ReadAllBytes(setup.DevelopBacklogPath);

        JsonElement factory = await setup.FactoryEntry();
        using HttpResponseMessage change = await setup.Post(new { action = "approve", line = 3 });

        Assert.False(factory.GetProperty("readable").GetBoolean());
        Assert.Equal("not_develop", factory.GetProperty("reason").GetString());
        await AssertError(change, HttpStatusCode.Conflict, "not_develop");
        Assert.Equal(before, File.ReadAllBytes(setup.DevelopBacklogPath));
        Assert.Equal(head, TempFactoryRoot.Git(setup.Root.Path, "rev-parse", "refs/heads/develop"));
        Assert.False(Directory.Exists(setup.WorktreesFolder));
    }

    [Fact]
    public async Task Approve_CommitsOnDevelopOnly()
    {
        using Setup setup = Setup.Create();
        await setup.AssertDevelopCommit(new { action = "approve", line = 3 }, "backlog: approve #3", ["factory/backlog.md"]);
        Assert.Contains("- [ ] [#3]", File.ReadAllText(setup.DevelopBacklogPath), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Drop_CommitsTheBacklogAndTheDeletedStory()
    {
        using Setup setup = Setup.Create();
        await setup.AssertDevelopCommit(
            new { action = "drop", line = 3 }, "backlog: drop #3", ["factory/backlog.md", "factory/backlog/003-three.md"]);
        Assert.False(File.Exists(Path.Combine(setup.DevelopFolder, "backlog", "003-three.md")));
    }

    [Fact]
    public async Task TopAndPlace_CommitMoves()
    {
        using Setup setup = Setup.Create();
        string version = await setup.AssertDevelopCommit(
            new { action = "top", line = 5 }, "backlog: move #5 to top", ["factory/backlog.md"]);
        await setup.AssertDevelopCommit(
            new { action = "place", line = 2, target = 3, where = "after" }, "backlog: move #5", ["factory/backlog.md"],
            version);
    }

    [Fact]
    public async Task StopLineActions_CommitStopLine()
    {
        using Setup setup = Setup.Create();
        string version = await setup.AssertDevelopCommit(
            new { action = "stop_add", line = 2 }, "backlog: stop line", ["factory/backlog.md"]);
        version = await setup.AssertDevelopCommit(
            new { action = "stop_move", line = 3, target = 6, where = "after" }, "backlog: stop line",
            ["factory/backlog.md"], version);
        await setup.AssertDevelopCommit(
            new { action = "stop_remove", line = 6 }, "backlog: stop line", ["factory/backlog.md"], version);
        Assert.DoesNotContain("[stop]", File.ReadAllText(setup.DevelopBacklogPath), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AddAndCapture_CommitTheBacklogAndTheNewStory()
    {
        using Setup setup = Setup.Create();
        string version = await setup.AssertDevelopCommit(
            new { action = "add", title = "New idea", what = "Do it.", why = "Because.", where = "bottom" },
            "backlog: add #6", ["factory/backlog.md", "factory/backlog/006-new-idea.md"]);
        await setup.AssertDevelopCommit(
            new { action = "capture", text = "Captured idea" }, "backlog: capture #7",
            ["factory/backlog.md", "factory/backlog/007-captured-idea.md"], version);
        Assert.Contains(
            "- 2026-01-02: Draft from ControlRoom",
            File.ReadAllText(Path.Combine(setup.DevelopFolder, "backlog", "007-captured-idea.md")),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task TitleAndStory_CommitTheirFiles()
    {
        using Setup setup = Setup.Create();
        string version = await setup.AssertDevelopCommit(
            new { action = "title", line = 2, title = "Renamed two" }, "backlog: edit title of #2",
            ["factory/backlog.md", "factory/backlog/002-two.md"]);
        string story = File.ReadAllText(Path.Combine(setup.DevelopFolder, "backlog", "002-two.md"));
        await setup.AssertDevelopCommit(
            new { action = "story", line = 2, storyVersion = Sha(Encoding.UTF8.GetBytes(story)), markdown = "# 002 X\n" },
            "backlog: edit story #2", ["factory/backlog/002-two.md"], version);
    }

    [Fact]
    public async Task DoneNoteAndCategory_CommitTheirFiles()
    {
        using Setup setup = Setup.Create();
        string version = await setup.AssertDevelopCommit(
            new { action = "done", line = 4 }, "backlog: done #4", ["factory/backlog.md"]);
        version = await setup.AssertDevelopCommit(
            new { action = "note", line = 2, storyVersion = Sha(Encoding.UTF8.GetBytes(StoryTwo)), note = "Remember" },
            "backlog: note on #2", ["factory/backlog/002-two.md"], version);
        string story = File.ReadAllText(Path.Combine(setup.DevelopFolder, "backlog", "002-two.md"));
        Assert.Contains("- 2026-01-02: Remember", story, StringComparison.Ordinal);
        await setup.AssertDevelopCommit(
            new { action = "category", line = 2, storyVersion = Sha(Encoding.UTF8.GetBytes(story)), category = "docs" },
            "backlog: category #2", ["factory/backlog/002-two.md"], version);
    }

    [Fact]
    public async Task FinishedFactoryJob_StillWritesDevelop()
    {
        using Setup setup = Setup.Create();
        setup.Root.AddJob("1-done", JobSamples.State("factory", "done", 1));
        setup.Root.AddJob("2-other", JobSamples.State("Alpha", "build", 1));

        await setup.AssertDevelopCommit(new { action = "approve", line = 3 }, "backlog: approve #3", ["factory/backlog.md"]);
        Assert.Equal("", TempFactoryRoot.Git(setup.Root.Path, "for-each-ref", "refs/heads/backlog"));
    }

    [Theory]
    [MemberData(nameof(RunningStages))]
    public async Task RunningFactoryJob_CommitsOnTheBacklogBranch(string stage, bool checkedOut)
    {
        using Setup setup = Setup.Create();
        string? own = checkedOut ? setup.AddBacklogWorktree() : null;
        setup.Root.AddJob("1-running", JobSamples.State("factory", stage, 1));
        string develop = TempFactoryRoot.Git(setup.Root.Path, "rev-parse", "refs/heads/develop");
        string main = TempFactoryRoot.Git(setup.Root.Path, "rev-parse", "refs/heads/main");
        byte[] developBytes = File.ReadAllBytes(setup.DevelopBacklogPath);

        using JsonDocument result = await setup.Change(new { action = "approve", line = 3 });

        string worktree = own ?? Path.Combine(setup.WorktreesFolder, "factory");
        string written = Path.Combine(worktree, "factory", "backlog.md");
        Assert.False(result.RootElement.GetProperty("pushed").GetBoolean());
        Assert.False(result.RootElement.TryGetProperty("warning", out _));
        Assert.Equal(Sha(File.ReadAllBytes(written)), result.RootElement.GetProperty("version").GetString());
        Assert.Equal("1", TempFactoryRoot.Git(setup.Root.Path, "rev-list", "--count", "refs/heads/develop..refs/heads/backlog"));
        Assert.Equal(
            "backlog: approve #3", TempFactoryRoot.Git(setup.Root.Path, "log", "-1", "--format=%s", "refs/heads/backlog"));
        Assert.Equal(
            "factory/backlog.md",
            TempFactoryRoot.Git(setup.Root.Path, "show", "--name-only", "--format=", "refs/heads/backlog"));
        Assert.Equal(develop, TempFactoryRoot.Git(setup.Root.Path, "rev-parse", "refs/heads/develop"));
        Assert.Equal(main, TempFactoryRoot.Git(setup.Root.Path, "rev-parse", "refs/heads/main"));
        Assert.Equal(developBytes, File.ReadAllBytes(setup.DevelopBacklogPath));
        Assert.Equal(RootBacklog, File.ReadAllText(setup.RootBacklogPath));
        Assert.Equal(checkedOut, !Directory.Exists(Path.Combine(setup.WorktreesFolder, "factory")));

        JsonElement factory = await setup.FactoryEntry();
        Assert.True(factory.GetProperty("pending").GetBoolean());
        Assert.Equal(result.RootElement.GetProperty("version").GetString(), factory.GetProperty("version").GetString());
        Assert.Equal(0, factory.GetProperty("approval").GetArrayLength());
    }

    [Fact]
    public async Task RunningFactoryJob_StoryAndSecondChangeUseTheBacklogWorktree()
    {
        using Setup setup = Setup.Create();
        setup.Root.AddJob("1-running", JobSamples.State("factory", "build", 1));
        using JsonDocument first = await setup.Change(new { action = "approve", line = 3 });

        using JsonDocument second = await setup.Change(
            new { action = "note", line = 2, storyVersion = Sha(Encoding.UTF8.GetBytes(StoryTwo)), note = "Later" },
            first.RootElement.GetProperty("version").GetString());

        using HttpClient client = setup.Factory.CreateTokenClient();
        using JsonDocument story = JsonDocument.Parse(await client.GetStringAsync("/api/story?project=factory&number=2"));
        Assert.Contains("- 2026-01-02: Later", story.RootElement.GetProperty("markdown").GetString(), StringComparison.Ordinal);
        Assert.Equal(StoryTwo, File.ReadAllText(Path.Combine(setup.DevelopFolder, "backlog", "002-two.md")));
        Assert.Equal("2", TempFactoryRoot.Git(setup.Root.Path, "rev-list", "--count", "refs/heads/develop..refs/heads/backlog"));
    }

    [Fact]
    public async Task Push_GoesToOriginDevelop()
    {
        using Setup setup = Setup.Create();
        string origin = setup.AddOrigin();

        using JsonDocument result = await setup.Change(new { action = "approve", line = 3 });

        Assert.True(result.RootElement.GetProperty("pushed").GetBoolean());
        Assert.False(result.RootElement.TryGetProperty("warning", out _));
        Assert.Equal(
            TempFactoryRoot.Git(setup.DevelopPath, "rev-parse", "HEAD"),
            TempFactoryRoot.Git(origin, "rev-parse", "refs/heads/develop"));
    }

    [Fact]
    public async Task RejectedPush_KeepsTheCommitAndWarns()
    {
        using Setup setup = Setup.Create();
        string origin = setup.AddOrigin();
        string clone = Path.Combine(setup.Own, "clone");
        TempFactoryRoot.Git(setup.Own, "clone", "-q", "-b", "develop", origin, clone);
        TempFactoryRoot.Git(clone, "config", "user.name", "Other");
        TempFactoryRoot.Git(clone, "config", "user.email", "other@example.invalid");
        TempFactoryRoot.Git(clone, "config", "commit.gpgsign", "false");
        File.WriteAllText(Path.Combine(clone, "other.txt"), "x\n");
        TempFactoryRoot.Git(clone, "add", "other.txt");
        TempFactoryRoot.Git(clone, "commit", "-q", "-m", "other");
        TempFactoryRoot.Git(clone, "push", "-q", "origin", "develop");
        string remote = TempFactoryRoot.Git(origin, "rev-parse", "refs/heads/develop");

        await AssertPushFailed(setup, origin, remote);
    }

    [Fact]
    public async Task FailedPush_KeepsTheCommitAndWarns()
    {
        using Setup setup = Setup.Create();
        string origin = setup.AddOrigin();
        string remote = TempFactoryRoot.Git(origin, "rev-parse", "refs/heads/develop");
        TempFactoryRoot.Git(setup.Root.Path, "remote", "set-url", "origin", Path.Combine(setup.Own, "missing.git"));

        await AssertPushFailed(setup, origin, remote);
    }

    [Fact]
    public async Task BacklogBranchChange_IsNeverPushed()
    {
        using Setup setup = Setup.Create();
        string origin = setup.AddOrigin();
        string remote = TempFactoryRoot.Git(origin, "rev-parse", "refs/heads/develop");
        setup.Root.AddJob("1-running", JobSamples.State("factory", "review", 1));

        using JsonDocument result = await setup.Change(new { action = "approve", line = 3 });

        Assert.False(result.RootElement.GetProperty("pushed").GetBoolean());
        Assert.False(result.RootElement.TryGetProperty("warning", out _));
        Assert.Equal("", TempFactoryRoot.Git(origin, "for-each-ref", "refs/heads/backlog"));
        Assert.Equal(remote, TempFactoryRoot.Git(origin, "rev-parse", "refs/heads/develop"));
    }

    [Fact]
    public async Task StaleVersion_IsChanged()
    {
        using Setup setup = Setup.Create();
        await setup.AssertRefused(new { action = "approve", line = 3 }, HttpStatusCode.Conflict, "changed", new string('0', 64));
    }

    [Fact]
    public async Task DirtyBacklog_IsDirty()
    {
        using Setup setup = Setup.Create();
        File.AppendAllText(Path.Combine(setup.DevelopFolder, "backlog", "002-two.md"), "uncommitted\n");
        await setup.AssertRefused(new { action = "approve", line = 3 }, HttpStatusCode.Conflict, "dirty");
    }

    [Fact]
    public async Task SymlinkedStory_IsNotFollowed()
    {
        using Setup setup = Setup.Create();
        string story = Path.Combine(setup.DevelopFolder, "backlog", "002-two.md");
        string outside = Path.Combine(setup.Own, "outside.md");
        File.WriteAllText(outside, StoryTwo);
        File.Delete(story);
        File.CreateSymbolicLink(story, outside);
        TempFactoryRoot.Git(setup.DevelopPath, "add", "-A");
        TempFactoryRoot.Git(setup.DevelopPath, "commit", "-q", "-m", "link");

        await setup.AssertRefused(
            new { action = "story", line = 2, storyVersion = Sha(Encoding.UTF8.GetBytes(StoryTwo)), markdown = "# x\n" },
            HttpStatusCode.BadRequest, "invalid");
        Assert.Equal(StoryTwo, File.ReadAllText(outside));
    }

    [Fact]
    public async Task SecondStopLine_IsStopLine()
    {
        using Setup setup = Setup.Create();
        File.WriteAllText(
            setup.DevelopBacklogPath, DevelopBacklog + "- [stop] Auto stops here\n- [stop] Auto stops here\n");
        TempFactoryRoot.Git(setup.DevelopPath, "commit", "-q", "-am", "stops");

        await setup.AssertRefused(new { action = "stop_add", line = 2 }, HttpStatusCode.Conflict, "stop_line");
    }

    [Fact]
    public async Task BrokenServerWorktree_IsWorktreeFailed()
    {
        using Setup setup = Setup.Create();
        setup.Root.AddJob("1-running", JobSamples.State("factory", "build", 1));
        Directory.CreateDirectory(setup.WorktreesFolder);
        File.WriteAllText(Path.Combine(setup.WorktreesFolder, "factory"), "not a folder\n");

        await setup.AssertRefused(new { action = "approve", line = 3 }, HttpStatusCode.InternalServerError, "worktree_failed");
        Assert.Equal("", TempFactoryRoot.Git(setup.Root.Path, "for-each-ref", "refs/heads/backlog"));
    }

    [Fact]
    public async Task RootCheckoutOnBacklog_IsNeverWritten()
    {
        using Setup setup = Setup.Create();
        TempFactoryRoot.Git(setup.Root.Path, "checkout", "-q", "-b", "backlog", "develop");
        setup.Root.AddJob("1-running", JobSamples.State("factory", "build", 1));
        string backlog = TempFactoryRoot.Git(setup.Root.Path, "rev-parse", "refs/heads/backlog");
        byte[] root = File.ReadAllBytes(setup.RootBacklogPath);

        await setup.AssertRefused(
            new { action = "approve", line = 3 }, HttpStatusCode.InternalServerError, "worktree_failed",
            rootBacklog: root);

        Assert.Equal(backlog, TempFactoryRoot.Git(setup.Root.Path, "rev-parse", "refs/heads/backlog"));
        Assert.Equal("", TempFactoryRoot.Git(setup.Root.Path, "status", "--porcelain", "--", "factory/backlog.md", "factory/backlog"));
    }

    [Fact]
    public async Task ProjectDependency_OnFactory_IsMetOnceDoneInDevelop()
    {
        using Setup setup = Setup.Create(withAlpha: true);

        JsonElement alpha = (await setup.Backlogs()).RootElement.GetProperty("projects")[0];
        JsonElement factory = await setup.FactoryEntry();
        Assert.True(alpha.GetProperty("open")[0].GetProperty("waiting").GetBoolean());
        JsonElement after = alpha.GetProperty("blocked")[0].GetProperty("after")[0];
        Assert.Equal("factory", after.GetProperty("project").GetString());
        Assert.False(after.GetProperty("missing").GetBoolean());
        Assert.True(factory.GetProperty("open")[1].GetProperty("waiting").GetBoolean());

        File.WriteAllText(setup.DevelopBacklogPath, DevelopBacklog.Replace("- [ ] [#2]", "- [x] [#2]", StringComparison.Ordinal));
        TempFactoryRoot.Git(setup.DevelopPath, "commit", "-q", "-am", "done two");

        alpha = (await setup.Backlogs()).RootElement.GetProperty("projects")[0];
        factory = await setup.FactoryEntry();
        Assert.False(alpha.GetProperty("open")[0].GetProperty("waiting").GetBoolean());
        Assert.Equal(0, alpha.GetProperty("blocked").GetArrayLength());
        Assert.False(factory.GetProperty("open")[0].GetProperty("waiting").GetBoolean());
    }

    [Fact]
    public async Task ProjectDependency_OnUnreadableFactory_WaitsAndIsNotMissing()
    {
        using Setup setup = Setup.Create(withAlpha: true, develop: DevelopKind.Missing);

        JsonElement alpha = (await setup.Backlogs()).RootElement.GetProperty("projects")[0];

        Assert.True(alpha.GetProperty("open")[0].GetProperty("waiting").GetBoolean());
        Assert.False(alpha.GetProperty("blocked")[0].GetProperty("after")[0].GetProperty("missing").GetBoolean());
        Assert.False(alpha.GetProperty("stopped").GetBoolean());
    }

    private static async Task AssertPushFailed(Setup setup, string origin, string remote)
    {
        using JsonDocument result = await setup.Change(new { action = "approve", line = 3 });

        Assert.True(result.RootElement.GetProperty("committed").GetBoolean());
        Assert.False(result.RootElement.GetProperty("pushed").GetBoolean());
        Assert.Equal("push_failed", result.RootElement.GetProperty("warning").GetString());
        Assert.Equal("backlog: approve #3", TempFactoryRoot.Git(setup.DevelopPath, "log", "-1", "--format=%s"));
        Assert.Equal(remote, TempFactoryRoot.Git(origin, "rev-parse", "refs/heads/develop"));
    }

    private static async Task AssertError(HttpResponseMessage response, HttpStatusCode status, string error)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("{\"error\":\"" + error + "\"}", await response.Content.ReadAsStringAsync());
    }

    private static string Sha(byte[] bytes)
    {
        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }

    public enum DevelopKind
    {
        Worktree,
        Missing,
        PlainFolder,
        OtherBranch,
        Detached,
    }

    /// <summary>One test's factory root (a git repo on <c>main</c> with the root copy of <c>factory/backlog.md</c>),
    /// its develop folder at <c>&lt;own&gt;/develop</c> (by default a worktree on <c>develop</c> holding the develop
    /// copy), token folder and server.</summary>
    private sealed class Setup : IDisposable
    {
        private Setup(TempFactoryRoot root, string own)
        {
            Root = root;
            Own = own;
            Tokens = new TempTokenFolder();
            Factory = new ServerFactory(root.Path, Path.Combine(Tokens.Path, "Server data", AccessToken.FileName))
            {
                FactoryDevelopDir = DevelopPath,
            };
            Factory.Time.Now = new DateTimeOffset(2026, 1, 2, 12, 0, 0, TimeSpan.Zero);
            using HttpClient start = Factory.CreateClient();
        }

        public TempFactoryRoot Root { get; }

        public string Own { get; }

        public TempTokenFolder Tokens { get; }

        public ServerFactory Factory { get; }

        public string DevelopPath => Path.Combine(Own, "develop");

        public string DevelopFolder => Path.Combine(DevelopPath, "factory");

        public string DevelopBacklogPath => Path.Combine(DevelopFolder, "backlog.md");

        public string RootBacklogPath => Path.Combine(Root.Path, "factory", "backlog.md");

        public string WorktreesFolder => Path.Combine(Path.GetDirectoryName(Factory.TokenFile)!, BacklogWorktree.FolderName);

        public static Setup Create(bool withAlpha = false, DevelopKind develop = DevelopKind.Worktree)
        {
            TempFactoryRoot root = new();
            string own = Path.Combine(Path.GetTempPath(), "controlroom-factory-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(own);
            string factory = Path.Combine(root.Path, "factory");
            Directory.CreateDirectory(factory);
            File.WriteAllText(Path.Combine(factory, "backlog.md"), RootBacklog);
            TempFactoryRoot.AddStory(factory, "001-one.md", RootStory);
            if (withAlpha)
            {
                string alpha = root.AddProject("Alpha", "- [ ] [#1](backlog/001-a.md) Alpha waits (after: factory#2)\n");
                TempFactoryRoot.AddStory(alpha, "001-a.md", "# 001 Alpha waits\n");
            }

            TempFactoryRoot.InitGit(root.Path, "main");
            TempFactoryRoot.Git(root.Path, "branch", "develop");
            string path = Path.Combine(own, "develop");
            switch (develop)
            {
                case DevelopKind.Worktree:
                    TempFactoryRoot.Git(root.Path, "worktree", "add", "-q", path, "develop");
                    break;
                case DevelopKind.OtherBranch:
                    TempFactoryRoot.Git(root.Path, "worktree", "add", "-q", "-b", "other", path, "develop");
                    break;
                case DevelopKind.Detached:
                    TempFactoryRoot.Git(root.Path, "worktree", "add", "-q", "--detach", path, "develop");
                    break;
                case DevelopKind.PlainFolder:
                    Directory.CreateDirectory(Path.Combine(path, "factory"));
                    break;
                default:
                    break;
            }

            if (develop != DevelopKind.Missing)
            {
                WriteDevelopCopy(Path.Combine(path, "factory"));
                if (develop != DevelopKind.PlainFolder)
                {
                    TempFactoryRoot.Git(path, "add", "-A");
                    TempFactoryRoot.Git(path, "commit", "-q", "-m", "develop copy");
                }
            }

            if (develop is DevelopKind.OtherBranch or DevelopKind.Detached)
            {
                // The develop branch gets the same content, so only the worktree's branch differs.
                TempFactoryRoot.Git(root.Path, "branch", "-f", "develop", TempFactoryRoot.Git(path, "rev-parse", "HEAD"));
            }

            return new Setup(root, own);
        }

        private static void WriteDevelopCopy(string folder)
        {
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "backlog.md"), DevelopBacklog);
            TempFactoryRoot.AddStory(folder, "001-one.md", "# 001 Done one\n");
            TempFactoryRoot.AddStory(folder, "002-two.md", StoryTwo);
            TempFactoryRoot.AddStory(folder, "003-three.md", "# 003 Proposal three\n");
            TempFactoryRoot.AddStory(folder, "004-four.md", "# 004 Hand four\n");
            TempFactoryRoot.AddStory(folder, "005-five.md", "# 005 Five\n");
        }

        /// <summary>A test-made worktree of a new <c>backlog</c> branch (from develop) at
        /// <c>&lt;own&gt;/backlog</c>.</summary>
        public string AddBacklogWorktree()
        {
            string path = Path.Combine(Own, "backlog");
            TempFactoryRoot.Git(Root.Path, "worktree", "add", "-q", "-b", "backlog", path, "develop");
            return path;
        }

        /// <summary>A bare <c>&lt;own&gt;/origin.git</c> as the factory repo's <c>origin</c>, with develop pushed.</summary>
        public string AddOrigin()
        {
            string origin = Path.Combine(Own, "origin.git");
            TempFactoryRoot.Git(Own, "init", "-q", "--bare", origin);
            TempFactoryRoot.Git(Root.Path, "remote", "add", "origin", origin);
            TempFactoryRoot.Git(DevelopPath, "push", "-q", "origin", "develop");
            return origin;
        }

        public async Task<JsonDocument> Backlogs()
        {
            using HttpClient client = Factory.CreateTokenClient();
            return JsonDocument.Parse(await client.GetStringAsync("/api/backlogs"));
        }

        public async Task<JsonElement> FactoryEntry()
        {
            using JsonDocument backlogs = await Backlogs();
            JsonElement last = backlogs.RootElement.GetProperty("projects").EnumerateArray().Last();
            Assert.Equal("factory", last.GetProperty("name").GetString());
            return last.Clone();
        }

        /// <summary>Posts a <c>factory</c> change with the develop copy's version, or <paramref name="version"/>.</summary>
        public async Task<HttpResponseMessage> Post(object fields, string? version = null)
        {
            Dictionary<string, object?> body = JsonSerializer.Deserialize<Dictionary<string, object?>>(
                JsonSerializer.Serialize(fields))!;
            body["project"] = "factory";
            body["version"] = version ?? Sha(File.ReadAllBytes(DevelopBacklogPath));
            using HttpClient client = Factory.CreateTokenClient();
            using StringContent content = new(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
            return await client.PostAsync(Route, content);
        }

        public async Task<JsonDocument> Change(object fields, string? version = null)
        {
            using HttpResponseMessage response = await Post(fields, version);
            string text = await response.Content.ReadAsStringAsync();
            Assert.True(response.StatusCode == HttpStatusCode.OK, text);
            return JsonDocument.Parse(text);
        }

        /// <summary>The change commits exactly <paramref name="files"/> with <paramref name="message"/> on develop in
        /// the develop worktree; <c>main</c>, the root files and the worktree's status stay clean; no push without an
        /// <c>origin</c>. Returns the new version.</summary>
        public async Task<string> AssertDevelopCommit(object fields, string message, string[] files, string? version = null)
        {
            string main = TempFactoryRoot.Git(Root.Path, "rev-parse", "refs/heads/main");
            string head = TempFactoryRoot.Git(DevelopPath, "rev-parse", "HEAD");

            using JsonDocument result = await Change(fields, version);

            Assert.True(result.RootElement.GetProperty("committed").GetBoolean());
            Assert.False(result.RootElement.GetProperty("pushed").GetBoolean());
            Assert.False(result.RootElement.TryGetProperty("warning", out _));
            Assert.Equal("develop", TempFactoryRoot.Git(DevelopPath, "branch", "--show-current"));
            Assert.Equal(head, TempFactoryRoot.Git(DevelopPath, "rev-parse", "HEAD~1"));
            Assert.Equal(message, TempFactoryRoot.Git(DevelopPath, "log", "-1", "--format=%s"));
            string[] changed = TempFactoryRoot.Git(DevelopPath, "show", "--name-only", "--format=", "HEAD")
                .Split('\n', StringSplitOptions.RemoveEmptyEntries);
            Assert.Equal(files.Order(StringComparer.Ordinal), changed.Order(StringComparer.Ordinal));
            Assert.Equal("", TempFactoryRoot.Git(DevelopPath, "status", "--porcelain"));
            Assert.Equal(main, TempFactoryRoot.Git(Root.Path, "rev-parse", "refs/heads/main"));
            Assert.Equal("main", TempFactoryRoot.Git(Root.Path, "branch", "--show-current"));
            Assert.Equal(RootBacklog, File.ReadAllText(RootBacklogPath));
            Assert.Equal(RootStory, File.ReadAllText(Path.Combine(Root.Path, "factory", "backlog", "001-one.md")));
            Assert.Equal(Sha(File.ReadAllBytes(DevelopBacklogPath)), result.RootElement.GetProperty("version").GetString());
            return result.RootElement.GetProperty("version").GetString()!;
        }

        /// <summary>The change is refused with the status and code, and nothing is committed or written.</summary>
        public async Task AssertRefused(
            object fields, HttpStatusCode status, string error, string? version = null, byte[]? rootBacklog = null)
        {
            string head = TempFactoryRoot.Git(Root.Path, "rev-parse", "refs/heads/develop");
            byte[] before = File.ReadAllBytes(DevelopBacklogPath);

            using HttpResponseMessage response = await Post(fields, version);

            await AssertError(response, status, error);
            Assert.Equal(head, TempFactoryRoot.Git(Root.Path, "rev-parse", "refs/heads/develop"));
            Assert.Equal(before, File.ReadAllBytes(DevelopBacklogPath));
            Assert.Equal(rootBacklog ?? Encoding.UTF8.GetBytes(RootBacklog), File.ReadAllBytes(RootBacklogPath));
        }

        public void Dispose()
        {
            Factory.Dispose();
            Tokens.Dispose();
            Root.Dispose();
            if (Directory.Exists(Own))
            {
                Directory.Delete(Own, recursive: true);
            }
        }
    }
}
