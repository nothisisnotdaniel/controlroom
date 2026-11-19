using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ControlRoom.Server.Tests;

/// <summary><c>POST /api/backlog/change</c> against a real local git repo in a temp factory root (and a bare repo
/// in the same temp folder as <c>origin</c>). No network; git never prompts.</summary>
public sealed partial class BacklogChangeApiTests
{
    private const string Route = "/api/backlog/change";

    private const string AlphaBacklog =
        "# Alpha backlog\n"                                     // 0
        + "\n"                                                  // 1
        + "- [x] [#1](backlog/001-done.md) Done one\n"          // 2
        + "- [ ] [#2](backlog/002-open.md) Open two (after: #1) → 7-x\n" // 3
        + "Prose line.\n"                                       // 4
        + "- [?] [#3](backlog/003-proposal.md) Proposal three\n" // 5
        + "- [?] Old proposal: no number\n"                     // 6
        + "- [ ] Plain open: old line\n";                       // 7

    private const string StoryTwo = "# 002 Open two\n\nStory two.\n";

    [Fact]
    public async Task Backlogs_HaveVersionAndLine_AndStoryHasMarkdownAndStoryVersion()
    {
        using Repo repo = Repo.Create();
        using HttpClient client = repo.Factory.CreateTokenClient();

        using JsonDocument backlogs = JsonDocument.Parse(await client.GetStringAsync("/api/backlogs"));
        JsonElement alpha = backlogs.RootElement.GetProperty("projects")[0];
        Assert.Equal(repo.Version(), alpha.GetProperty("version").GetString());
        Assert.Equal(Sha(File.ReadAllBytes(repo.BacklogPath)), alpha.GetProperty("version").GetString());
        Assert.Equal(3, alpha.GetProperty("open")[0].GetProperty("line").GetInt32());
        Assert.Equal(7, alpha.GetProperty("open")[1].GetProperty("line").GetInt32());
        Assert.Equal(5, alpha.GetProperty("approval")[0].GetProperty("line").GetInt32());
        Assert.Equal(2, alpha.GetProperty("done")[0].GetProperty("line").GetInt32());

        using JsonDocument story = JsonDocument.Parse(await client.GetStringAsync("/api/story?project=Alpha&number=2"));
        Assert.Equal(StoryTwo, story.RootElement.GetProperty("markdown").GetString());
        Assert.Equal(Sha(Encoding.UTF8.GetBytes(StoryTwo)), story.RootElement.GetProperty("storyVersion").GetString());
        Assert.Equal("Open two", story.RootElement.GetProperty("title").GetString());
    }

    [Fact]
    public async Task Backlogs_UnreadableProjectHasNullVersion()
    {
        using TempFactoryRoot root = new();
        root.AddProject("Missing", null);
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();

        using JsonDocument json = JsonDocument.Parse(await client.GetStringAsync("/api/backlogs"));

        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("projects")[0].GetProperty("version").ValueKind);
    }

    [Fact]
    public async Task Change_WithoutToken_Is401AndWritesNothing()
    {
        using Repo repo = Repo.Create();
        using HttpClient client = repo.Factory.CreateClient();
        IReadOnlyList<string> before = repo.State();

        using HttpResponseMessage response = await client.PostAsync(
            Route, Json(new { project = "Alpha", version = repo.Version(), action = "approve", line = 5 }));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(before, repo.State());
    }

    [Theory]
    [InlineData("text/plain")]
    [InlineData("application/x-www-form-urlencoded")]
    [InlineData("application/json; charset=latin1")]
    public async Task Change_WithWrongContentType_Is415(string contentType)
    {
        using Repo repo = Repo.Create();
        using HttpClient client = repo.Factory.CreateTokenClient();
        IReadOnlyList<string> before = repo.State();
        string body = JsonSerializer.Serialize(new { project = "Alpha", version = repo.Version(), action = "approve", line = 5 });
        using StringContent content = new(body, Encoding.UTF8);
        content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);

        using HttpResponseMessage response = await client.PostAsync(Route, content);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        Assert.Equal("{\"error\":\"invalid\"}", await response.Content.ReadAsStringAsync());
        Assert.Equal(before, repo.State());
    }

    [Fact]
    public async Task Change_WithABodyOver64KiB_Is400Invalid()
    {
        using Repo repo = Repo.Create();
        using HttpClient client = repo.Factory.CreateTokenClient();
        IReadOnlyList<string> before = repo.State();
        string padding = new('a', 64 * 1024);

        using HttpResponseMessage response = await client.PostAsync(Route, Json(new
        {
            project = "Alpha", version = repo.Version(), action = "add", where = "top", title = "T", what = padding,
            why = "y",
        }));

        await AssertError(response, HttpStatusCode.BadRequest, "invalid");
        Assert.Equal(before, repo.State());
    }

    public static TheoryData<string> InvalidBodies()
    {
        return
        [
            "{\"project\":\"Nope\",\"version\":\"V\",\"action\":\"approve\",\"line\":5}",
            "{\"project\":\"factory\",\"version\":\"V\",\"action\":\"approve\",\"line\":5}",
            "{\"project\":\"alpha\",\"version\":\"V\",\"action\":\"approve\",\"line\":5}",
            "{\"project\":\"Alpha\",\"version\":\"V\",\"action\":\"delete\",\"line\":5}",
            "{\"project\":\"Alpha\",\"version\":\"V\",\"action\":\"approve\",\"line\":\"5\"}",
            "{\"project\":\"Alpha\",\"version\":\"V\",\"action\":\"approve\",\"line\":-1}",
            "{\"project\":\"Alpha\",\"version\":\"V\",\"action\":\"approve\",\"line\":5.5}",
            "{\"project\":\"Alpha\",\"version\":\"V\",\"action\":\"approve\"}",
            "{\"project\":\"Alpha\",\"version\":\"V\",\"action\":\"approve\",\"line\":99}",
            "{\"project\":\"Alpha\",\"version\":\"V\",\"action\":\"approve\",\"line\":5,\"path\":\"x\"}",
            "{\"project\":\"Alpha\",\"version\":\"V\",\"action\":\"approve\",\"line\":5,\"line\":6}",
            "{\"project\":\"Alpha\",\"version\":null,\"action\":\"approve\",\"line\":5}",
            "{\"project\":[\"Alpha\"],\"version\":\"V\",\"action\":\"approve\",\"line\":5}",
            "{\"project\":\"Alpha\",\"version\":\"V\",\"action\":\"title\",\"line\":3,\"title\":\"a → b\"}",
            "{\"project\":\"Alpha\",\"version\":\"V\",\"action\":\"title\",\"line\":3}",
            "{\"project\":\"Alpha\",\"version\":\"V\",\"action\":\"add\",\"where\":\"middle\",\"title\":\"T\",\"what\":\"w\",\"why\":\"y\"}",
            "{\"project\":\"Alpha\",\"version\":\"V\",\"action\":\"add\",\"where\":\"top\",\"title\":\"T\",\"what\":\" \",\"why\":\"y\"}",
            "{\"project\":\"Alpha\",\"version\":\"V\",\"action\":\"add\",\"where\":\"top\",\"title\":\"T\",\"what\":\"w\"}",
            "{\"project\":\"Alpha\",\"version\":\"V\",\"action\":\"add\",\"where\":\"top\",\"title\":\"\",\"what\":\"w\",\"why\":\"y\"}",
            "{\"project\":\"Alpha\",\"version\":\"V\",\"action\":\"story\",\"line\":3,\"storyVersion\":\"S\",\"markdown\":\"a\\r\\nb\"}",
            "{\"project\":\"Alpha\",\"version\":\"V\",\"action\":\"story\",\"line\":3,\"markdown\":\"a\"}",
            "[1,2]",
            "not json",
            "",
        ];
    }

    [Theory]
    [MemberData(nameof(InvalidBodies))]
    public async Task Change_WithAnInvalidBody_Is400InvalidAndWritesNothing(string template)
    {
        using Repo repo = Repo.Create();
        using HttpClient client = repo.Factory.CreateTokenClient();
        IReadOnlyList<string> before = repo.State();

        using HttpResponseMessage response = await client.PostAsync(
            Route, new StringContent(template.Replace("\"V\"", "\"" + repo.Version() + "\"", StringComparison.Ordinal),
                Encoding.UTF8, "application/json"));

        await AssertError(response, HttpStatusCode.BadRequest, "invalid");
        Assert.Equal(before, repo.State());
    }

    [Theory]
    [InlineData("approve", 3)]
    [InlineData("approve", 2)]
    [InlineData("approve", 4)]
    [InlineData("drop", 3)]
    [InlineData("drop", 7)]
    [InlineData("up", 2)]
    [InlineData("down", 7)]
    [InlineData("top", 3)]
    [InlineData("top", 4)]
    [InlineData("title", 2)]
    [InlineData("title", 7)]
    [InlineData("story", 2)]
    [InlineData("story", 7)]
    public async Task Change_OnALineTheActionDoesNotAllow_Is400Invalid(string action, int line)
    {
        using Repo repo = Repo.Create();
        using HttpClient client = repo.Factory.CreateTokenClient();
        IReadOnlyList<string> before = repo.State();

        using HttpResponseMessage response = await client.PostAsync(Route, Json(new
        {
            project = "Alpha", version = repo.Version(), action, line, title = "New", storyVersion = repo.StoryVersion(2),
            markdown = "# x\n",
        }));

        await AssertError(response, HttpStatusCode.BadRequest, "invalid");
        Assert.Equal(before, repo.State());
    }

    [Fact]
    public async Task Change_InAFolderWithoutItsOwnRepo_Is409NotDevelopBeforeOtherChecks()
    {
        using TempFactoryRoot root = new();
        string folder = root.AddProject("Alpha", AlphaBacklog);
        TempFactoryRoot.AddStory(folder, "003-proposal.md", "# 003 Proposal three\n");
        File.AppendAllText(Path.Combine(folder, "backlog.md"), "dirty\n");
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();
        IReadOnlyList<string> before = root.Snapshot();

        using HttpResponseMessage response = await client.PostAsync(
            Route, Json(new { project = "Alpha", version = "stale", action = "approve", line = 5 }));

        await AssertError(response, HttpStatusCode.Conflict, "not_develop");
        Assert.Equal(before, root.Snapshot());
        Assert.False(Directory.Exists(Path.Combine(Path.GetDirectoryName(factory.TokenFile)!, "worktrees")));
    }

    [Fact]
    public async Task Change_WithADirtyBacklog_Is409DirtyBeforeTheVersionCheck()
    {
        using Repo repo = Repo.Create();
        File.AppendAllText(repo.BacklogPath, "- [ ] Uncommitted: line\n");
        using HttpClient client = repo.Factory.CreateTokenClient();
        IReadOnlyList<string> before = repo.State();

        using HttpResponseMessage response = await client.PostAsync(
            Route, Json(new { project = "Alpha", version = "stale", action = "approve", line = 5 }));

        await AssertError(response, HttpStatusCode.Conflict, "dirty");
        Assert.Equal(before, repo.State());
    }

    [Fact]
    public async Task Change_WithAnUntrackedStoryFile_Is409Dirty()
    {
        using Repo repo = Repo.Create();
        TempFactoryRoot.AddStory(repo.Folder, "009-untracked.md", "# 009 Untracked\n");
        using HttpClient client = repo.Factory.CreateTokenClient();
        IReadOnlyList<string> before = repo.State();

        using HttpResponseMessage response = await client.PostAsync(
            Route, Json(new { project = "Alpha", version = repo.Version(), action = "approve", line = 5 }));

        await AssertError(response, HttpStatusCode.Conflict, "dirty");
        Assert.Equal(before, repo.State());
    }

    [Fact]
    public async Task Change_WithAStaleVersion_Is409Changed()
    {
        using Repo repo = Repo.Create();
        using HttpClient client = repo.Factory.CreateTokenClient();
        IReadOnlyList<string> before = repo.State();

        using HttpResponseMessage response = await client.PostAsync(
            Route, Json(new { project = "Alpha", version = new string('0', 64), action = "approve", line = 5 }));

        await AssertError(response, HttpStatusCode.Conflict, "changed");
        Assert.Equal(before, repo.State());
    }

    [Fact]
    public async Task StoryEdit_WithAStaleStoryVersion_Is409Changed()
    {
        using Repo repo = Repo.Create();
        using HttpClient client = repo.Factory.CreateTokenClient();
        IReadOnlyList<string> before = repo.State();

        using HttpResponseMessage response = await client.PostAsync(Route, Json(new
        {
            project = "Alpha", version = repo.Version(), action = "story", line = 3, storyVersion = new string('0', 64),
            markdown = "# 002 Open two\n\nNew.\n",
        }));

        await AssertError(response, HttpStatusCode.Conflict, "changed");
        Assert.Equal(before, repo.State());
    }

    [Fact]
    public async Task Approve_MakesTheLineOpenInOneCommit()
    {
        using Repo repo = Repo.Create();

        JsonElement result = await repo.Change(new { action = "approve", line = 5 });

        Assert.Equal(
            AlphaBacklog.Replace("- [?] [#3]", "- [ ] [#3]", StringComparison.Ordinal), File.ReadAllText(repo.BacklogPath));
        repo.AssertOneCommit("backlog: approve #3", ["backlog.md"]);
        Assert.Equal(repo.Version(), result.GetProperty("version").GetString());
        Assert.True(result.GetProperty("committed").GetBoolean());
        Assert.False(result.GetProperty("pushed").GetBoolean());
        Assert.False(result.TryGetProperty("warning", out _));
    }

    [Fact]
    public async Task Approve_AnUnnumberedLineNamesItsLineNumber()
    {
        using Repo repo = Repo.Create();

        await repo.Change(new { action = "approve", line = 6 });

        Assert.Contains("- [ ] Old proposal: no number\n", File.ReadAllText(repo.BacklogPath), StringComparison.Ordinal);
        repo.AssertOneCommit("backlog: approve line 7", ["backlog.md"]);
    }

    [Fact]
    public async Task Drop_RemovesTheLineAndItsStoryInOneCommit()
    {
        using Repo repo = Repo.Create();

        await repo.Change(new { action = "drop", line = 5 });

        Assert.Equal(
            AlphaBacklog.Replace("- [?] [#3](backlog/003-proposal.md) Proposal three\n", "", StringComparison.Ordinal),
            File.ReadAllText(repo.BacklogPath));
        Assert.False(File.Exists(Path.Combine(repo.Folder, "backlog", "003-proposal.md")));
        repo.AssertOneCommit("backlog: drop #3", ["backlog.md", "backlog/003-proposal.md"]);
        Assert.Equal("", TempFactoryRoot.Git(repo.Folder, "status", "--porcelain"));
    }

    [Fact]
    public async Task Drop_AnUnnumberedLineTouchesOnlyTheBacklog()
    {
        using Repo repo = Repo.Create();

        await repo.Change(new { action = "drop", line = 6 });

        Assert.DoesNotContain("Old proposal", File.ReadAllText(repo.BacklogPath), StringComparison.Ordinal);
        repo.AssertOneCommit("backlog: drop line 7", ["backlog.md"]);
    }

    [Fact]
    public async Task Add_WritesTheStoryAndTheLineInOneCommit()
    {
        using Repo repo = Repo.Create();
        TempFactoryRoot.AddStory(repo.Folder, "011-gap.md", "# 011 Gap\n");
        repo.CommitAll("gap");

        await repo.Change(new
        {
            action = "add", where = "top", title = "  Edit   backlogs ", what = "Do it.", why = "Because.",
            story = "As a user.",
        });

        string[] lines = File.ReadAllText(repo.BacklogPath).Split('\n');
        Assert.Equal("- [ ] [#12](backlog/012-edit-backlogs.md) Edit backlogs", lines[3]);
        Assert.StartsWith("- [ ] [#2]", lines[4], StringComparison.Ordinal);
        Assert.Equal(
            "# 012 Edit   backlogs\n\nAs a user.\n\n## What\n\nDo it.\n\n## Why\n\nBecause.\n",
            File.ReadAllText(Path.Combine(repo.Folder, "backlog", "012-edit-backlogs.md")));
        repo.AssertOneCommit("backlog: add #12", ["backlog.md", "backlog/012-edit-backlogs.md"]);
        Assert.Equal("", TempFactoryRoot.Git(repo.Folder, "status", "--porcelain"));
    }

    [Fact]
    public async Task Add_AtTheBottomGoesAfterTheLastItem()
    {
        using Repo repo = Repo.Create();

        await repo.Change(new { action = "add", where = "bottom", title = "Last", what = "w", why = "y" });

        Assert.Equal(AlphaBacklog + "- [ ] [#4](backlog/004-last.md) Last\n", File.ReadAllText(repo.BacklogPath));
        repo.AssertOneCommit("backlog: add #4", ["backlog.md", "backlog/004-last.md"]);
    }

    [Theory]
    [InlineData("up", 7, 6, 4, "backlog: move line 8 up")]
    [InlineData("up", 3, 2, 4, "backlog: move #2 up")]
    [InlineData("down", 3, 5, 4, "backlog: move #2 down")]
    [InlineData("top", 5, 3, 5, "backlog: move #3 to top")]
    public async Task Move_ReordersItemLinesAndKeepsProse(string action, int line, int to, int prose, string message)
    {
        using Repo repo = Repo.Create();
        string moved = AlphaBacklog.Split('\n')[line];

        await repo.Change(new { action, line });

        string[] lines = File.ReadAllText(repo.BacklogPath).Split('\n');
        Assert.Equal(moved, lines[to]);
        Assert.Equal("Prose line.", lines[prose]);
        Assert.Equal(AlphaBacklog.Length, File.ReadAllText(repo.BacklogPath).Length);
        repo.AssertOneCommit(message, ["backlog.md"]);
    }

    [Fact]
    public async Task Title_ReplacesTheTitleAndTheStoryHeadingInOneCommit()
    {
        using Repo repo = Repo.Create();

        await repo.Change(new { action = "title", line = 3, title = "Renamed two" });

        Assert.Equal(
            AlphaBacklog.Replace("Open two (after", "Renamed two (after", StringComparison.Ordinal),
            File.ReadAllText(repo.BacklogPath));
        Assert.Equal(
            "# 002 Renamed two\n\nStory two.\n", File.ReadAllText(Path.Combine(repo.Folder, "backlog", "002-open.md")));
        repo.AssertOneCommit("backlog: edit title of #2", ["backlog.md", "backlog/002-open.md"]);
    }

    [Fact]
    public async Task Title_LeavesAStoryWithAnotherHeadingAlone()
    {
        using Repo repo = Repo.Create();

        await repo.Change(new { action = "title", line = 5, title = "Renamed three" });

        Assert.Equal("# Something else\n", File.ReadAllText(Path.Combine(repo.Folder, "backlog", "003-proposal.md")));
        repo.AssertOneCommit("backlog: edit title of #3", ["backlog.md"]);
    }

    [Fact]
    public async Task Story_ReplacesTheStoryFileOnlyAndKeepsTheBacklogVersion()
    {
        using Repo repo = Repo.Create();
        string version = repo.Version();

        JsonElement result = await repo.Change(new
        {
            action = "story", line = 3, storyVersion = repo.StoryVersion(2), markdown = "# 002 Open two\n\nNew text.\n",
        });

        Assert.Equal("# 002 Open two\n\nNew text.\n", File.ReadAllText(Path.Combine(repo.Folder, "backlog", "002-open.md")));
        Assert.Equal(AlphaBacklog, File.ReadAllText(repo.BacklogPath));
        Assert.Equal(version, result.GetProperty("version").GetString());
        repo.AssertOneCommit("backlog: edit story #2", ["backlog/002-open.md"]);
    }

    [Fact]
    public async Task Story_ThatIsASymlinkIsRefused()
    {
        using Repo repo = Repo.Create();
        string story = Path.Combine(repo.Folder, "backlog", "002-open.md");
        string outside = Path.Combine(repo.Root.Path, "outside.md");
        File.WriteAllText(outside, StoryTwo);
        File.Delete(story);
        File.CreateSymbolicLink(story, outside);
        repo.CommitAll("link");
        using HttpClient client = repo.Factory.CreateTokenClient();
        IReadOnlyList<string> before = repo.State();

        using HttpResponseMessage response = await client.PostAsync(Route, Json(new
        {
            project = "Alpha", version = repo.Version(), action = "story", line = 3,
            storyVersion = Sha(Encoding.UTF8.GetBytes(StoryTwo)), markdown = "# changed\n",
        }));

        await AssertError(response, HttpStatusCode.BadRequest, "invalid");
        Assert.Equal(before, repo.State());
        Assert.Equal(StoryTwo, File.ReadAllText(outside));
    }

    [Fact]
    public async Task Change_KeepsAMissingFinalNewlineAndCrlfLines()
    {
        using Repo repo = Repo.Create("- [?] A\r\n- [ ] B\r\nno final newline");

        await repo.Change(new { action = "approve", line = 0 });

        Assert.Equal("- [ ] A\r\n- [ ] B\r\nno final newline", File.ReadAllText(repo.BacklogPath));
    }

    private const string HandBacklog =
        "# Alpha backlog\n"                                           // 0
        + "- [x] [#1](backlog/001-done.md) Done one\n"                // 1
        + "- [h] [#2](backlog/002-open.md) Hand two (after: #1)\n"    // 2
        + "Prose line.\n"                                             // 3
        + "- [h] Old hand: no number\n"                               // 4
        + "- [?] [#3](backlog/003-proposal.md) Proposal three\n"      // 5
        + "- [ ] Plain open: old line\n";                             // 6

    [Fact]
    public async Task Done_TicksOffAHandLineInOneBacklogOnlyCommitAndPushes()
    {
        using Repo repo = Repo.Create(HandBacklog);
        string remote = repo.Root.AddBareRemote(repo.Folder);
        string storyBefore = repo.StoryVersion(2);

        JsonElement result = await repo.Change(new { action = "done", line = 2 });

        Assert.Equal(
            Encoding.UTF8.GetBytes(HandBacklog.Replace("- [h] [#2]", "- [x] [#2]", StringComparison.Ordinal)),
            File.ReadAllBytes(repo.BacklogPath));
        repo.AssertOneCommit("backlog: done #2", ["backlog.md"]);
        Assert.Equal(storyBefore, repo.StoryVersion(2));
        Assert.Equal(repo.Version(), result.GetProperty("version").GetString());
        Assert.True(result.GetProperty("committed").GetBoolean());
        Assert.True(result.GetProperty("pushed").GetBoolean());
        Assert.Equal(
            TempFactoryRoot.Git(repo.Folder, "rev-parse", "HEAD"),
            TempFactoryRoot.Git(remote, "rev-parse", "refs/heads/develop"));
        Assert.Equal("", TempFactoryRoot.Git(repo.Folder, "status", "--porcelain"));
    }

    [Fact]
    public async Task Done_AnUnnumberedLineNamesItsLineNumber()
    {
        using Repo repo = Repo.Create(HandBacklog);

        JsonElement result = await repo.Change(new { action = "done", line = 4 });

        Assert.Contains("- [x] Old hand: no number\n", File.ReadAllText(repo.BacklogPath), StringComparison.Ordinal);
        repo.AssertOneCommit("backlog: done line 5", ["backlog.md"]);
        Assert.False(result.GetProperty("pushed").GetBoolean());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(99)]
    [InlineData(null)]
    public async Task Done_OnALineThatIsNotHandOrWithoutALine_Is400InvalidAndWritesNothing(int? line)
    {
        using Repo repo = Repo.Create(HandBacklog);
        using HttpClient client = repo.Factory.CreateTokenClient();
        IReadOnlyList<string> before = repo.State();
        object body = line is null
            ? new { project = "Alpha", version = repo.Version(), action = "done" }
            : new { project = "Alpha", version = repo.Version(), action = "done", line = line.Value };

        using HttpResponseMessage response = await client.PostAsync(Route, Json(body));

        await AssertError(response, HttpStatusCode.BadRequest, "invalid");
        Assert.Equal(before, repo.State());
    }

    [Fact]
    public async Task Done_WithAStaleVersion_Is409ChangedAndHeadStays()
    {
        using Repo repo = Repo.Create(HandBacklog);
        using HttpClient client = repo.Factory.CreateTokenClient();
        IReadOnlyList<string> before = repo.State();

        using HttpResponseMessage response = await client.PostAsync(
            Route, Json(new { project = "Alpha", version = new string('0', 64), action = "done", line = 2 }));

        await AssertError(response, HttpStatusCode.Conflict, "changed");
        Assert.Equal(before, repo.State());
    }

    [Fact]
    public async Task Done_WithADirtyBacklog_Is409DirtyAndHeadStays()
    {
        using Repo repo = Repo.Create(HandBacklog);
        File.AppendAllText(repo.BacklogPath, "- [ ] Uncommitted: line\n");
        using HttpClient client = repo.Factory.CreateTokenClient();
        IReadOnlyList<string> before = repo.State();

        using HttpResponseMessage response = await client.PostAsync(
            Route, Json(new { project = "Alpha", version = repo.Version(), action = "done", line = 2 }));

        await AssertError(response, HttpStatusCode.Conflict, "dirty");
        Assert.Equal(before, repo.State());
    }

    [Fact]
    public async Task Done_OnAnotherBranch_CommitsOnBacklogAndTheCheckoutStays()
    {
        using Repo repo = Repo.Create(HandBacklog);
        TempFactoryRoot.Git(repo.Folder, "checkout", "-q", "-b", "feature");
        using HttpClient client = repo.Factory.CreateTokenClient();
        IReadOnlyList<string> before = repo.State();

        using HttpResponseMessage response = await client.PostAsync(
            Route, Json(new { project = "Alpha", version = repo.Version(), action = "done", line = 2 }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(before, repo.State());
        Assert.Equal("feature", TempFactoryRoot.Git(repo.Folder, "branch", "--show-current"));
        repo.AssertBacklogCommit("backlog: done #2", ["backlog.md"]);
        Assert.StartsWith("- [x] [#2]", File.ReadAllLines(Path.Combine(repo.WorktreePath, "backlog.md"))[2], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Change_PushesToOrigin()
    {
        using Repo repo = Repo.Create();
        string remote = repo.Root.AddBareRemote(repo.Folder);

        JsonElement result = await repo.Change(new { action = "approve", line = 5 });

        Assert.True(result.GetProperty("pushed").GetBoolean());
        Assert.False(result.TryGetProperty("warning", out _));
        Assert.Equal(
            TempFactoryRoot.Git(repo.Folder, "rev-parse", "HEAD"),
            TempFactoryRoot.Git(remote, "rev-parse", "refs/heads/develop"));
    }

    [Fact]
    public async Task Change_KeepsTheCommitWhenThePushFails()
    {
        using Repo repo = Repo.Create();
        string remote = repo.Root.AddBareRemote(repo.Folder);
        Directory.Delete(remote, recursive: true);

        JsonElement result = await repo.Change(new { action = "approve", line = 5 });

        Assert.True(result.GetProperty("committed").GetBoolean());
        Assert.False(result.GetProperty("pushed").GetBoolean());
        Assert.Equal("push_failed", result.GetProperty("warning").GetString());
        repo.AssertOneCommit("backlog: approve #3", ["backlog.md"]);
    }

    [Fact]
    public async Task Change_WhenTheCommitFails_RestoresTheFilesAndReturns500()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using Repo repo = Repo.Create();
        string hook = Path.Combine(repo.Folder, ".git", "hooks", "pre-commit");
        File.WriteAllText(hook, "#!/bin/sh\nexit 1\n");
        File.SetUnixFileMode(hook, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        using HttpClient client = repo.Factory.CreateTokenClient();
        IReadOnlyList<string> before = repo.State();

        using HttpResponseMessage response = await client.PostAsync(Route, Json(new
        {
            project = "Alpha", version = repo.Version(), action = "add", where = "top", title = "Hooked", what = "w",
            why = "y",
        }));

        await AssertError(response, HttpStatusCode.InternalServerError, "commit_failed");
        Assert.Equal(before, repo.State());
        Assert.Equal("", TempFactoryRoot.Git(repo.Folder, "status", "--porcelain"));
    }

    [Fact]
    public async Task TwoChangesWithTheSameVersion_OneSucceedsAndOneIsChanged()
    {
        using Repo repo = Repo.Create();
        using HttpClient client = repo.Factory.CreateTokenClient();
        string version = repo.Version();

        Task<HttpResponseMessage> first = client.PostAsync(
            Route, Json(new { project = "Alpha", version, action = "approve", line = 5 }));
        Task<HttpResponseMessage> second = client.PostAsync(
            Route, Json(new { project = "Alpha", version, action = "approve", line = 6 }));
        HttpResponseMessage[] responses = await Task.WhenAll(first, second);

        HttpStatusCode[] codes = responses.Select(response => response.StatusCode).Order().ToArray();
        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.Conflict], codes);
        HttpResponseMessage refused = responses.Single(response => response.StatusCode == HttpStatusCode.Conflict);
        Assert.Equal("{\"error\":\"changed\"}", await refused.Content.ReadAsStringAsync());
        Assert.Equal("2", TempFactoryRoot.Git(repo.Folder, "rev-list", "--count", "HEAD"));
        foreach (HttpResponseMessage response in responses)
        {
            response.Dispose();
        }
    }

    private static StringContent Json(object body)
    {
        return new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
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

    /// <summary>A temp factory root with project <c>Alpha</c> in a real git repo on <c>develop</c>, hosted.</summary>
    private sealed class Repo : IDisposable
    {
        private int setupCommits = 1;

        private Repo(TempFactoryRoot root, string folder)
        {
            Root = root;
            Folder = folder;
            Factory = new ServerFactory(root.Path);
        }

        public TempFactoryRoot Root { get; }

        public string Folder { get; }

        public ServerFactory Factory { get; }

        public string BacklogPath => Path.Combine(Folder, "backlog.md");

        /// <summary>The server's worktree of the <c>backlog</c> branch, next to the factory's token file.</summary>
        public string WorktreePath => Path.Combine(Path.GetDirectoryName(Factory.TokenFile)!, "worktrees", "Alpha");

        public static Repo Create(string backlog = AlphaBacklog)
        {
            TempFactoryRoot root = new();
            string folder = root.AddProject("Alpha", backlog);
            TempFactoryRoot.AddStory(folder, "001-done.md", "# 001 Done one\n");
            TempFactoryRoot.AddStory(folder, "002-open.md", StoryTwo);
            TempFactoryRoot.AddStory(folder, "003-proposal.md", "# Something else\n");
            TempFactoryRoot.InitGit(folder, "develop");
            return new Repo(root, folder);
        }

        public string Version()
        {
            return Sha(File.ReadAllBytes(BacklogPath));
        }

        public string StoryVersion(int number)
        {
            string file = Directory.GetFiles(Path.Combine(Folder, "backlog"), number.ToString("D3") + "-*.md").Single();
            return Sha(File.ReadAllBytes(file));
        }

        /// <summary>Every project file outside <c>.git</c> with its hash, the current HEAD and the branch.</summary>
        public IReadOnlyList<string> State()
        {
            List<string> state = [];
            foreach (string file in Directory.EnumerateFiles(Folder, "*", SearchOption.AllDirectories))
            {
                string relative = Path.GetRelativePath(Folder, file).Replace('\\', '/');
                if (!relative.StartsWith(".git/", StringComparison.Ordinal))
                {
                    state.Add(relative + "|" + Sha(File.ReadAllBytes(file)));
                }
            }

            state.Sort(StringComparer.Ordinal);
            state.Add(TempFactoryRoot.Git(Folder, "rev-parse", "HEAD"));
            return state;
        }

        public async Task<JsonElement> Change(object fields)
        {
            Dictionary<string, object?> body = JsonSerializer.Deserialize<Dictionary<string, object?>>(
                JsonSerializer.Serialize(fields))!;
            body["project"] = "Alpha";
            body["version"] = Version();
            using HttpClient client = Factory.CreateTokenClient();
            using HttpResponseMessage response = await client.PostAsync(Route, Json(body));
            string text = await response.Content.ReadAsStringAsync();
            Assert.True(response.StatusCode == HttpStatusCode.OK, text);
            using JsonDocument json = JsonDocument.Parse(text);
            return json.RootElement.Clone();
        }

        /// <summary>Commits everything as test setup.</summary>
        public void CommitAll(string message)
        {
            TempFactoryRoot.Git(Folder, "add", "-A");
            TempFactoryRoot.Git(Folder, "commit", "-q", "-m", message);
            setupCommits += 1;
        }

        /// <summary>Exactly one commit after the setup commits, with the message and exactly the files.</summary>
        public void AssertOneCommit(string message, string[] files)
        {
            Assert.Equal(
                (setupCommits + 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
                TempFactoryRoot.Git(Folder, "rev-list", "--count", "HEAD"));
            Assert.Equal(message, TempFactoryRoot.Git(Folder, "log", "-1", "--format=%s"));
            Assert.Equal("develop", TempFactoryRoot.Git(Folder, "branch", "--show-current"));
            string[] changed = TempFactoryRoot.Git(Folder, "show", "--name-only", "--format=", "HEAD")
                .Split('\n', StringSplitOptions.RemoveEmptyEntries);
            Assert.Equal(files.Order(StringComparer.Ordinal), changed.Order(StringComparer.Ordinal));
        }

        /// <summary>Exactly one commit on <c>backlog</c> after develop, with the message and exactly the files, and
        /// develop where it was.</summary>
        public void AssertBacklogCommit(string message, string[] files)
        {
            Assert.Equal("1", TempFactoryRoot.Git(Folder, "rev-list", "--count", "refs/heads/develop..refs/heads/backlog"));
            Assert.Equal(
                setupCommits.ToString(System.Globalization.CultureInfo.InvariantCulture),
                TempFactoryRoot.Git(Folder, "rev-list", "--count", "refs/heads/develop"));
            Assert.Equal(message, TempFactoryRoot.Git(Folder, "log", "-1", "--format=%s", "refs/heads/backlog"));
            string[] changed = TempFactoryRoot.Git(Folder, "show", "--name-only", "--format=", "refs/heads/backlog")
                .Split('\n', StringSplitOptions.RemoveEmptyEntries);
            Assert.Equal(files.Order(StringComparer.Ordinal), changed.Order(StringComparer.Ordinal));
        }

        public void Dispose()
        {
            Factory.Dispose();
            Root.Dispose();
        }
    }
}
