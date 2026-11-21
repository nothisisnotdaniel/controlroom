using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ControlRoom.Server.Tests;

/// <summary>Backlog changes while a job has the checkout on <c>factory/x</c>: they go to the local <c>backlog</c>
/// branch in the server's worktree next to the token file (a <see cref="TempTokenFolder"/>), never into the
/// checkout and never pushed. Real local repos; the remote is a local bare repo.</summary>
public sealed class BacklogWorktreeApiTests
{
    private const string Route = "/api/backlog/change";

    private const string AlphaBacklog =
        "# Alpha backlog\n"                                     // 0
        + "\n"                                                  // 1
        + "- [x] [#1](backlog/001-done.md) Done one\n"          // 2
        + "- [ ] [#2](backlog/002-open.md) Open two\n"          // 3
        + "Prose line.\n"                                       // 4
        + "- [?] [#3](backlog/003-proposal.md) Proposal three\n"; // 5

    private const string StoryTwo = "# 002 Open two\n\nStory two.\n";

    [Fact]
    public async Task Approve_OnAJobBranch_CommitsOnANewBacklogWorktreeAndLeavesTheCheckout()
    {
        using Setup setup = Setup.Create(withRemote: true);
        Snapshot before = setup.Checkout();
        string remoteDevelop = TempFactoryRoot.Git(setup.Remote!, "rev-parse", "refs/heads/develop");

        using JsonDocument result = await setup.Change(new { action = "approve", line = 5 });

        Assert.True(result.RootElement.GetProperty("committed").GetBoolean());
        Assert.False(result.RootElement.GetProperty("pushed").GetBoolean());
        Assert.False(result.RootElement.TryGetProperty("warning", out _));
        Assert.True(Directory.Exists(setup.WorktreePath));
        Assert.Equal("backlog", TempFactoryRoot.Git(setup.WorktreePath, "branch", "--show-current"));
        Assert.Equal(Sha(File.ReadAllBytes(setup.WorktreeBacklog)), result.RootElement.GetProperty("version").GetString());
        Assert.Contains("- [ ] [#3]", File.ReadAllText(setup.WorktreeBacklog), StringComparison.Ordinal);
        setup.AssertBacklogCommits(1, "backlog: approve #3", ["backlog.md"]);
        Assert.Equal(before, setup.Checkout());
        Assert.Equal(remoteDevelop, TempFactoryRoot.Git(setup.Remote!, "rev-parse", "refs/heads/develop"));
        Assert.Equal("", TempFactoryRoot.Git(setup.Remote!, "for-each-ref", "refs/heads/backlog"));
    }

    [Fact]
    public async Task Add_OnAJobBranch_CommitsOnlyTheBacklogAndTheNewStory()
    {
        using Setup setup = Setup.Create();

        using JsonDocument result = await setup.Change(new
        {
            action = "add", title = "New idea", what = "Do it.", why = "Because.", where = "bottom",
        });

        string story = Path.GetFileName(
            Directory.GetFiles(Path.Combine(setup.WorktreePath, "backlog"), "004-*.md").Single());
        setup.AssertBacklogCommits(1, "backlog: add #4", ["backlog.md", "backlog/" + story]);
        Assert.False(File.Exists(Path.Combine(setup.Folder, "backlog", story)));
    }

    [Fact]
    public async Task Note_OnAJobBranch_CommitsOnlyTheStory()
    {
        using Setup setup = Setup.Create();

        using JsonDocument result = await setup.Change(new
        {
            action = "note", line = 3, storyVersion = Sha(Encoding.UTF8.GetBytes(StoryTwo)), note = "Remember this",
        });

        setup.AssertBacklogCommits(1, "backlog: note on #2", ["backlog/002-open.md"]);
        Assert.Equal(StoryTwo, File.ReadAllText(Path.Combine(setup.Folder, "backlog", "002-open.md")));
    }

    [Fact]
    public async Task Reads_AfterAWorktreeCommit_ShowTheWorktreeCopyAndASecondChangeCommits()
    {
        using Setup setup = Setup.Create();
        using JsonDocument first = await setup.Change(new { action = "approve", line = 5 });
        string version = first.RootElement.GetProperty("version").GetString()!;
        using HttpClient client = setup.Factory.CreateTokenClient();

        using JsonDocument backlogs = JsonDocument.Parse(await client.GetStringAsync("/api/backlogs"));
        JsonElement alpha = backlogs.RootElement.GetProperty("projects")[0];
        Assert.True(alpha.GetProperty("pending").GetBoolean());
        Assert.Equal(version, alpha.GetProperty("version").GetString());
        Assert.Equal(0, alpha.GetProperty("approval").GetArrayLength());
        Assert.Contains(
            alpha.GetProperty("open").EnumerateArray(), entry => entry.GetProperty("number").GetInt32() == 3);

        using JsonDocument inbox = JsonDocument.Parse(await client.GetStringAsync("/api/inbox"));
        Assert.Equal(0, inbox.RootElement.GetProperty("items").GetArrayLength());
        using JsonDocument search = JsonDocument.Parse(await client.GetStringAsync("/api/search?q=%233"));
        Assert.Equal("open", search.RootElement.GetProperty("items")[0].GetProperty("mark").GetString());

        using JsonDocument second = await setup.Change(
            new { action = "note", line = 3, storyVersion = Sha(Encoding.UTF8.GetBytes(StoryTwo)), note = "Later" },
            version);
        Assert.Equal(version, second.RootElement.GetProperty("version").GetString());
        setup.AssertBacklogCommits(2, "backlog: note on #2", ["backlog/002-open.md"]);

        using JsonDocument story = JsonDocument.Parse(await client.GetStringAsync("/api/story?project=Alpha&number=2"));
        Assert.Contains(": Later", story.RootElement.GetProperty("markdown").GetString(), StringComparison.Ordinal);
        Assert.Equal(
            Sha(File.ReadAllBytes(Path.Combine(setup.WorktreePath, "backlog", "002-open.md"))),
            story.RootElement.GetProperty("storyVersion").GetString());
    }

    [Fact]
    public async Task Place_OnAJobBranch_CommitsOnBacklogAndIsNotPushed()
    {
        using Setup setup = Setup.Create(withRemote: true);
        Snapshot before = setup.Checkout();
        using JsonDocument first = await setup.Change(new { action = "approve", line = 5 });
        string version = first.RootElement.GetProperty("version").GetString()!;

        using JsonDocument result = await setup.Change(
            new { action = "place", line = 5, target = 3, where = "before" }, version);

        Assert.True(result.RootElement.GetProperty("committed").GetBoolean());
        Assert.False(result.RootElement.GetProperty("pushed").GetBoolean());
        Assert.Equal(Sha(File.ReadAllBytes(setup.WorktreeBacklog)), result.RootElement.GetProperty("version").GetString());
        string[] lines = File.ReadAllText(setup.WorktreeBacklog).Split('\n');
        Assert.StartsWith("- [ ] [#3]", lines[3], StringComparison.Ordinal);
        Assert.Equal("Prose line.", lines[4]);
        Assert.StartsWith("- [ ] [#2]", lines[5], StringComparison.Ordinal);
        setup.AssertBacklogCommits(2, "backlog: move #3", ["backlog.md"]);
        Assert.Equal(before, setup.Checkout());
        Assert.Equal("", TempFactoryRoot.Git(setup.Remote!, "for-each-ref", "refs/heads/backlog"));
    }

    [Fact]
    public async Task StaleVersionAndBadLine_OnAJobBranch_AreRefusedWithoutACommit()
    {
        using Setup setup = Setup.Create();
        Snapshot before = setup.Checkout();

        using HttpResponseMessage stale = await setup.Post(new { action = "approve", line = 5 }, new string('0', 64));
        await AssertError(stale, HttpStatusCode.Conflict, "changed");
        using HttpResponseMessage bad = await setup.Post(new { action = "approve", line = 4 });
        await AssertError(bad, HttpStatusCode.BadRequest, "invalid");

        Assert.Equal("0", setup.BacklogAhead());
        Assert.Equal(AlphaBacklog, File.ReadAllText(setup.WorktreeBacklog));
        Assert.Equal(before, setup.Checkout());
    }

    [Fact]
    public async Task DirtyWorktreeBacklog_IsRefused409DirtyWithoutACommit()
    {
        using Setup setup = Setup.Create();
        using JsonDocument first = await setup.Change(new { action = "approve", line = 5 });
        string version = first.RootElement.GetProperty("version").GetString()!;
        File.AppendAllText(setup.WorktreeBacklog, "- [ ] Uncommitted\n");

        using HttpResponseMessage response = await setup.Post(new { action = "top", line = 5 }, version);

        await AssertError(response, HttpStatusCode.Conflict, "dirty");
        Assert.Equal("1", setup.BacklogAhead());
    }

    [Fact]
    public async Task DevelopMovedOn_TheWorktreeIsFastForwardedBeforeTheVersionCheck()
    {
        using Setup setup = Setup.Create();
        // A refused change prepares the worktree at develop, with nothing of its own.
        using (HttpResponseMessage stale = await setup.Post(new { action = "approve", line = 5 }, new string('0', 64)))
        {
            await AssertError(stale, HttpStatusCode.Conflict, "changed");
        }

        TempFactoryRoot.Git(setup.Folder, "checkout", "-q", "develop");
        File.AppendAllText(Path.Combine(setup.Folder, "backlog.md"), "- [ ] Plain open: from develop\n");
        TempFactoryRoot.Git(setup.Folder, "commit", "-q", "-am", "develop moves");
        string develop = TempFactoryRoot.Git(setup.Folder, "rev-parse", "HEAD");
        string developVersion = Sha(File.ReadAllBytes(Path.Combine(setup.Folder, "backlog.md")));
        TempFactoryRoot.Git(setup.Folder, "checkout", "-q", "factory/x");

        using JsonDocument result = await setup.Change(new { action = "approve", line = 5 }, developVersion);

        Assert.Equal(develop, TempFactoryRoot.Git(setup.Folder, "rev-parse", "refs/heads/backlog~1"));
        Assert.Equal("1", setup.BacklogAhead());
        Assert.Contains("from develop", File.ReadAllText(setup.WorktreeBacklog), StringComparison.Ordinal);
    }

    [Fact]
    public async Task SymlinkedWorktreesFolder_Is500WorktreeFailedAndWritesNothing()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using Setup setup = Setup.Create();
        string elsewhere = Path.Combine(setup.Tokens.Path, "elsewhere");
        Directory.CreateDirectory(elsewhere, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        Directory.CreateSymbolicLink(setup.WorktreesFolder, elsewhere);

        await setup.AssertWorktreeFailed();
        Assert.Empty(Directory.EnumerateFileSystemEntries(elsewhere));
    }

    [Fact]
    public async Task LooseModeWorktreesFolder_Is500WorktreeFailedAndWritesNothing()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using Setup setup = Setup.Create();
        Directory.CreateDirectory(setup.WorktreesFolder);
        File.SetUnixFileMode(
            setup.WorktreesFolder,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead
            | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);

        await setup.AssertWorktreeFailed();
        Assert.Empty(Directory.EnumerateFileSystemEntries(setup.WorktreesFolder));
    }

    [Fact]
    public async Task SymlinkedWorktreePath_Is500WorktreeFailedAndWritesNothing()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using Setup setup = Setup.Create();
        string elsewhere = Path.Combine(setup.Tokens.Path, "elsewhere");
        Directory.CreateDirectory(elsewhere);
        Directory.CreateDirectory(
            setup.WorktreesFolder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        Directory.CreateSymbolicLink(setup.WorktreePath, elsewhere);

        await setup.AssertWorktreeFailed();
        Assert.Empty(Directory.EnumerateFileSystemEntries(elsewhere));
    }

    [Fact]
    public async Task BacklogCheckedOutElsewhere_Is500WorktreeFailedAndWritesNothing()
    {
        using Setup setup = Setup.Create();
        string elsewhere = Path.Combine(setup.Tokens.Path, "elsewhere");
        TempFactoryRoot.Git(setup.Folder, "branch", "backlog", "develop");
        TempFactoryRoot.Git(setup.Folder, "worktree", "add", "-q", elsewhere, "backlog");
        string backlog = TempFactoryRoot.Git(setup.Folder, "rev-parse", "refs/heads/backlog");

        await setup.AssertWorktreeFailed(backlogExists: true);
        Assert.Equal(backlog, TempFactoryRoot.Git(setup.Folder, "rev-parse", "refs/heads/backlog"));
        Assert.Equal(AlphaBacklog, File.ReadAllText(Path.Combine(elsewhere, "backlog.md")));
        Assert.False(Directory.Exists(setup.WorktreePath));
    }

    [Fact]
    public async Task DeletedWorktreeFolder_IsPrunedAndRecreated()
    {
        using Setup setup = Setup.Create();
        using JsonDocument first = await setup.Change(new { action = "approve", line = 5 });
        string version = first.RootElement.GetProperty("version").GetString()!;
        Directory.Delete(setup.WorktreePath, recursive: true);

        using JsonDocument second = await setup.Change(new { action = "top", line = 5 }, version);

        Assert.True(Directory.Exists(setup.WorktreePath));
        setup.AssertBacklogCommits(2, "backlog: move #3 to top", ["backlog.md"]);
        Assert.Equal(Sha(File.ReadAllBytes(setup.WorktreeBacklog)), second.RootElement.GetProperty("version").GetString());
    }

    [Fact]
    public async Task OnDevelop_CommitsAndPushesAsBeforeWithoutAWorktree()
    {
        using Setup setup = Setup.Create(withRemote: true, jobBranch: false);

        using JsonDocument result = await setup.Change(new { action = "approve", line = 5 });

        Assert.True(result.RootElement.GetProperty("pushed").GetBoolean());
        Assert.Equal(
            TempFactoryRoot.Git(setup.Folder, "rev-parse", "refs/heads/develop"),
            TempFactoryRoot.Git(setup.Remote!, "rev-parse", "refs/heads/develop"));
        Assert.Equal("backlog: approve #3", TempFactoryRoot.Git(setup.Folder, "log", "-1", "--format=%s"));
        Assert.Equal("", TempFactoryRoot.Git(setup.Folder, "for-each-ref", "refs/heads/backlog"));
        Assert.False(Directory.Exists(setup.WorktreesFolder));
        using HttpClient client = setup.Factory.CreateTokenClient();
        using JsonDocument backlogs = JsonDocument.Parse(await client.GetStringAsync("/api/backlogs"));
        Assert.False(backlogs.RootElement.GetProperty("projects")[0].GetProperty("pending").GetBoolean());
    }

    [Fact]
    public async Task Changes_ReportsANewBacklogFingerprintAfterAWorktreeCommit()
    {
        using Setup setup = Setup.Create();
        using JsonDocument first = await setup.Change(new { action = "approve", line = 5 });
        string version = first.RootElement.GetProperty("version").GetString()!;
        Snapshot checkout = setup.Checkout();
        using HttpClient client = setup.Factory.CreateTokenClient();
        string before = await BacklogFingerprint(client);

        using JsonDocument second = await setup.Change(
            new { action = "add", title = "Another", what = "W.", why = "Y.", where = "top" }, version);

        Assert.NotEqual(before, await BacklogFingerprint(client));
        Assert.Equal(checkout, setup.Checkout());
    }

    private static async Task<string> BacklogFingerprint(HttpClient client)
    {
        using JsonDocument changes = JsonDocument.Parse(await client.GetStringAsync("/api/changes"));
        return changes.RootElement.GetProperty("projects").GetProperty("Alpha").GetProperty("backlog").GetString()!;
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

    /// <summary>The checkout as the job sees it: branch, HEAD, develop, status and every file outside
    /// <c>.git</c> with its hash.</summary>
    private sealed record Snapshot(string Branch, string Head, string Develop, string Status, string Files);

    /// <summary>One test's own factory root (project <c>Alpha</c> on <c>factory/x</c> unless asked otherwise), token
    /// folder and server.</summary>
    private sealed class Setup : IDisposable
    {
        private Setup(TempFactoryRoot root, string folder, string? remote)
        {
            Root = root;
            Folder = folder;
            Remote = remote;
            Tokens = new TempTokenFolder();
            Factory = new ServerFactory(root.Path, Tokens.TokenFile);
            // Starting the server creates the token folder.
            using HttpClient start = Factory.CreateClient();
        }

        public TempFactoryRoot Root { get; }

        public string Folder { get; }

        public string? Remote { get; }

        public TempTokenFolder Tokens { get; }

        public ServerFactory Factory { get; }

        public string WorktreesFolder => Path.Combine(Path.GetDirectoryName(Tokens.TokenFile)!, "worktrees");

        public string WorktreePath => Path.Combine(WorktreesFolder, "Alpha");

        public string WorktreeBacklog => Path.Combine(WorktreePath, "backlog.md");

        public static Setup Create(bool withRemote = false, bool jobBranch = true)
        {
            TempFactoryRoot root = new();
            string folder = root.AddProject("Alpha", AlphaBacklog);
            TempFactoryRoot.AddStory(folder, "001-done.md", "# 001 Done one\n");
            TempFactoryRoot.AddStory(folder, "002-open.md", StoryTwo);
            TempFactoryRoot.AddStory(folder, "003-proposal.md", "# 003 Proposal three\n\n## Notes\n\n- Proposed by job 5-x\n");
            TempFactoryRoot.InitGit(folder, "develop");
            string? remote = withRemote ? root.AddBareRemote(folder) : null;
            if (jobBranch)
            {
                TempFactoryRoot.Git(folder, "checkout", "-q", "-b", "factory/x");
            }

            return new Setup(root, folder, remote);
        }

        public Snapshot Checkout()
        {
            List<string> files = [];
            foreach (string file in Directory.EnumerateFiles(Folder, "*", SearchOption.AllDirectories))
            {
                string relative = Path.GetRelativePath(Folder, file).Replace('\\', '/');
                if (!relative.StartsWith(".git/", StringComparison.Ordinal))
                {
                    files.Add(relative + "|" + Sha(File.ReadAllBytes(file)));
                }
            }

            files.Sort(StringComparer.Ordinal);
            return new Snapshot(
                TempFactoryRoot.Git(Folder, "branch", "--show-current"),
                TempFactoryRoot.Git(Folder, "rev-parse", "HEAD"),
                TempFactoryRoot.Git(Folder, "rev-parse", "refs/heads/develop"),
                TempFactoryRoot.Git(Folder, "status", "--porcelain"),
                string.Join("\n", files));
        }

        public string BacklogAhead()
        {
            return TempFactoryRoot.Git(Folder, "rev-list", "--count", "refs/heads/develop..refs/heads/backlog");
        }

        /// <summary>Posts the change with the checkout's backlog version, or <paramref name="version"/>.</summary>
        public async Task<HttpResponseMessage> Post(object fields, string? version = null)
        {
            Dictionary<string, object?> body = JsonSerializer.Deserialize<Dictionary<string, object?>>(
                JsonSerializer.Serialize(fields))!;
            body["project"] = "Alpha";
            body["version"] = version ?? Sha(File.ReadAllBytes(Path.Combine(Folder, "backlog.md")));
            using HttpClient client = Factory.CreateTokenClient();
            using StringContent content = new(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
            return await client.PostAsync(Route, content);
        }

        /// <summary>Posts the change and expects <c>200</c>.</summary>
        public async Task<JsonDocument> Change(object fields, string? version = null)
        {
            using HttpResponseMessage response = await Post(fields, version);
            string text = await response.Content.ReadAsStringAsync();
            Assert.True(response.StatusCode == HttpStatusCode.OK, text);
            return JsonDocument.Parse(text);
        }

        /// <summary><paramref name="count"/> commits on <c>backlog</c> that develop lacks; the last one has the
        /// message and exactly the files.</summary>
        public void AssertBacklogCommits(int count, string message, string[] files)
        {
            Assert.Equal(count.ToString(System.Globalization.CultureInfo.InvariantCulture), BacklogAhead());
            Assert.Equal(message, TempFactoryRoot.Git(Folder, "log", "-1", "--format=%s", "refs/heads/backlog"));
            string[] changed = TempFactoryRoot.Git(Folder, "show", "--name-only", "--format=", "refs/heads/backlog")
                .Split('\n', StringSplitOptions.RemoveEmptyEntries);
            Assert.Equal(files.Order(StringComparer.Ordinal), changed.Order(StringComparer.Ordinal));
            Assert.Equal("", TempFactoryRoot.Git(WorktreePath, "status", "--porcelain"));
        }

        /// <summary>An approve gives <c>500 worktree_failed</c>, and the checkout, develop and the backlog branch (or
        /// its absence) stay.</summary>
        public async Task AssertWorktreeFailed(bool backlogExists = false)
        {
            Snapshot before = Checkout();

            using HttpResponseMessage response = await Post(new { action = "approve", line = 5 });

            await AssertError(response, HttpStatusCode.InternalServerError, "worktree_failed");
            Assert.Equal(before, Checkout());
            if (!backlogExists)
            {
                Assert.Equal("", TempFactoryRoot.Git(Folder, "for-each-ref", "refs/heads/backlog"));
            }
            else
            {
                Assert.Equal("0", BacklogAhead());
            }
        }

        public void Dispose()
        {
            Factory.Dispose();
            Tokens.Dispose();
            Root.Dispose();
        }
    }
}
