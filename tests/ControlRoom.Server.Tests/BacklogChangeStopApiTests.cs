using System.Net;
using System.Text;
using System.Text.Json;

namespace ControlRoom.Server.Tests;

/// <summary>The <c>stop_add</c>, <c>stop_move</c> and <c>stop_remove</c> actions of <c>POST /api/backlog/change</c>,
/// and <c>place</c>, <c>top</c> and <c>add</c> next to the stop line: only <c>backlog.md</c> committed as
/// <c>backlog: stop line</c>, the exact bytes after each change, and every refusal without a write or commit.</summary>
public sealed partial class BacklogChangeApiTests
{
    private const string StopLine = "- [stop] Auto stops here\n";

    private const string NoStopBacklog =
        "# Alpha backlog\n"                                       // 0
        + "- [x] [#1](backlog/001-done.md) Done one\n"            // 1
        + "- [ ] [#2](backlog/002-open.md) Open two\n"            // 2
        + "Prose line.\n"                                         // 3
        + "- [ ] [#4](backlog/004-four.md) Four\n"                // 4
        + "- [?] [#3](backlog/003-proposal.md) Proposal three\n"  // 5
        + "- [ ] [#5](backlog/005-five.md) Five\n";               // 6

    private const string StopBacklog =
        "# Alpha backlog\n"                                       // 0
        + "- [x] [#1](backlog/001-done.md) Done one\n"            // 1
        + "- [ ] [#2](backlog/002-open.md) Open two\n"            // 2
        + StopLine                                                // 3
        + "Prose line.\n"                                         // 4
        + "- [ ] [#4](backlog/004-four.md) Four\n"                // 5
        + "- [?] [#3](backlog/003-proposal.md) Proposal three\n"  // 6
        + "- [ ] [#5](backlog/005-five.md) Five\n";               // 7

    [Fact]
    public async Task StopAdd_InsertsTheLineAfterTheItemCommitsOnlyTheBacklogAndPushes()
    {
        using Repo repo = Repo.Create(NoStopBacklog);
        string remote = repo.Root.AddBareRemote(repo.Folder);

        JsonElement result = await repo.Change(new { action = "stop_add", line = 4 });

        Assert.Equal(
            "# Alpha backlog\n- [x] [#1](backlog/001-done.md) Done one\n- [ ] [#2](backlog/002-open.md) Open two\n"
            + "Prose line.\n- [ ] [#4](backlog/004-four.md) Four\n" + StopLine
            + "- [?] [#3](backlog/003-proposal.md) Proposal three\n- [ ] [#5](backlog/005-five.md) Five\n",
            File.ReadAllText(repo.BacklogPath));
        repo.AssertOneCommit("backlog: stop line", ["backlog.md"]);
        Assert.True(result.GetProperty("committed").GetBoolean());
        Assert.True(result.GetProperty("pushed").GetBoolean());
        Assert.Equal(repo.Version(), result.GetProperty("version").GetString());
        Assert.Equal(
            TempFactoryRoot.Git(repo.Folder, "rev-parse", "HEAD"),
            TempFactoryRoot.Git(remote, "rev-parse", "refs/heads/develop"));
        Assert.Equal("", TempFactoryRoot.Git(repo.Folder, "status", "--porcelain"));

        using HttpClient client = repo.Factory.CreateTokenClient();
        using JsonDocument backlogs = JsonDocument.Parse(await client.GetStringAsync("/api/backlogs"));
        JsonElement alpha = backlogs.RootElement.GetProperty("projects")[0];
        Assert.Equal(5, alpha.GetProperty("stopLine").GetInt32());
        Assert.False(alpha.GetProperty("next").GetProperty("belowStop").GetBoolean());
    }

    [Fact]
    public async Task StopAdd_KeepsCrlfAndTheBom()
    {
        using Repo repo = Repo.Create("﻿- [ ] [#2](backlog/002-open.md) Open two\r\n- [ ] B\r\n");

        await repo.Change(new { action = "stop_add", line = 0 });

        Assert.Equal(
            Encoding.UTF8.GetBytes("﻿- [ ] [#2](backlog/002-open.md) Open two\r\n- [stop] Auto stops here\r\n- [ ] B\r\n"),
            File.ReadAllBytes(repo.BacklogPath));
        repo.AssertOneCommit("backlog: stop line", ["backlog.md"]);
    }

    [Theory]
    [InlineData(NoStopBacklog, "{\"action\":\"stop_add\",\"line\":1}")]   // done line
    [InlineData(NoStopBacklog, "{\"action\":\"stop_add\",\"line\":3}")]   // prose
    [InlineData(NoStopBacklog, "{\"action\":\"stop_add\",\"line\":5}")]   // [?] line
    [InlineData(NoStopBacklog, "{\"action\":\"stop_add\",\"line\":99}")]
    [InlineData(NoStopBacklog, "{\"action\":\"stop_add\",\"line\":-1}")]
    [InlineData(NoStopBacklog, "{\"action\":\"stop_add\"}")]
    [InlineData(NoStopBacklog, "{\"action\":\"stop_add\",\"line\":\"2\"}")]
    [InlineData(StopBacklog, "{\"action\":\"stop_add\",\"line\":2}")]     // already has a stop line
    [InlineData(StopBacklog, "{\"action\":\"stop_add\",\"line\":7}")]
    [InlineData(StopBacklog, "{\"action\":\"stop_move\",\"line\":3,\"target\":5,\"where\":\"before\"}")] // no-op
    [InlineData(StopBacklog, "{\"action\":\"stop_move\",\"line\":3,\"target\":2,\"where\":\"after\"}")]  // no-op
    [InlineData(StopBacklog, "{\"action\":\"stop_move\",\"line\":2,\"target\":7,\"where\":\"after\"}")]  // not the stop line
    [InlineData(StopBacklog, "{\"action\":\"stop_move\",\"line\":3,\"target\":4,\"where\":\"after\"}")]  // prose target
    [InlineData(StopBacklog, "{\"action\":\"stop_move\",\"line\":3,\"target\":6,\"where\":\"after\"}")]  // [?] target
    [InlineData(StopBacklog, "{\"action\":\"stop_move\",\"line\":3,\"target\":1,\"where\":\"after\"}")]  // done target
    [InlineData(StopBacklog, "{\"action\":\"stop_move\",\"line\":3,\"target\":3,\"where\":\"after\"}")]
    [InlineData(StopBacklog, "{\"action\":\"stop_move\",\"line\":3,\"target\":99,\"where\":\"after\"}")]
    [InlineData(StopBacklog, "{\"action\":\"stop_move\",\"line\":3,\"target\":7}")]
    [InlineData(StopBacklog, "{\"action\":\"stop_move\",\"line\":3,\"target\":7,\"where\":\"top\"}")]
    [InlineData(StopBacklog, "{\"action\":\"stop_move\",\"line\":3,\"where\":\"after\"}")]
    [InlineData(NoStopBacklog, "{\"action\":\"stop_move\",\"line\":3,\"target\":6,\"where\":\"after\"}")]
    [InlineData(StopBacklog, "{\"action\":\"stop_remove\",\"line\":2}")]
    [InlineData(StopBacklog, "{\"action\":\"stop_remove\",\"line\":4}")]
    [InlineData(StopBacklog, "{\"action\":\"stop_remove\"}")]
    [InlineData(NoStopBacklog, "{\"action\":\"stop_remove\",\"line\":3}")]
    [InlineData(StopBacklog, "{\"action\":\"place\",\"line\":6,\"target\":3,\"where\":\"after\"}")] // [?] next to the stop line
    [InlineData(StopBacklog, "{\"action\":\"place\",\"line\":3,\"target\":7,\"where\":\"after\"}")] // the stop line by place
    public async Task Stop_InvalidRequest_Is400InvalidAndWritesNothing(string backlog, string fields)
    {
        using Repo repo = Repo.Create(backlog);
        await AssertNoteRefused(repo, fields, HttpStatusCode.BadRequest, "invalid");
    }

    [Theory]
    [InlineData(7, "after", new[] { 2, 5, 7, 3 })]
    [InlineData(2, "before", new[] { 3, 2, 5, 7 })]
    [InlineData(5, "after", new[] { 2, 5, 3, 7 })]
    public async Task StopMove_MovesTheLineWithinTheOpenGroupInItsOwnIndexes(int target, string where, int[] order)
    {
        using Repo repo = Repo.Create(StopBacklog);
        string[] lines = StopBacklog.Split('\n');

        JsonElement result = await repo.Change(new { action = "stop_move", line = 3, target, where });

        int[] slots = [2, 3, 5, 7];
        string[] expected = [.. lines];
        for (int i = 0; i < slots.Length; i++)
        {
            expected[slots[i]] = lines[order[i]];
        }

        Assert.Equal(string.Join("\n", expected), File.ReadAllText(repo.BacklogPath));
        repo.AssertOneCommit("backlog: stop line", ["backlog.md"]);
        Assert.False(result.GetProperty("pushed").GetBoolean());
    }

    [Fact]
    public async Task StopRemove_DeletesTheLineAndNothingElse()
    {
        using Repo repo = Repo.Create(StopBacklog);

        await repo.Change(new { action = "stop_remove", line = 3 });

        Assert.Equal(
            NoStopBacklog,
            File.ReadAllText(repo.BacklogPath));
        repo.AssertOneCommit("backlog: stop line", ["backlog.md"]);
    }

    public static TheoryData<string> OddStopBacklogs()
    {
        return
        [
            StopBacklog + StopLine,
            StopBacklog + "- [STOP] Auto stops here\n",
            StopBacklog + "- [stop] Auto stops here now\n",
            StopBacklog.Replace(StopLine, "- [Stop] Auto stops here\n", StringComparison.Ordinal),
            NoStopBacklog + "- [stop]\n",
        ];
    }

    [Theory]
    [MemberData(nameof(OddStopBacklogs))]
    public async Task Stop_OnAFileWithASecondOrAnOddStopLine_Is409StopLineAndWritesNothing(string backlog)
    {
        foreach (string fields in new[]
                 {
                     "{\"action\":\"stop_add\",\"line\":2}",
                     "{\"action\":\"stop_move\",\"line\":3,\"target\":7,\"where\":\"after\"}",
                     "{\"action\":\"stop_remove\",\"line\":3}",
                     "{\"action\":\"stop_remove\",\"line\":99}",
                 })
        {
            using Repo repo = Repo.Create(backlog);
            await AssertNoteRefused(repo, fields, HttpStatusCode.Conflict, "stop_line");
        }
    }

    [Fact]
    public async Task Stop_WithAStaleVersion_Is409Changed()
    {
        using Repo repo = Repo.Create(StopBacklog);
        await AssertNoteRefused(
            repo, "{\"action\":\"stop_remove\",\"line\":3}", HttpStatusCode.Conflict, "changed",
            version: new string('0', 64));
    }

    [Fact]
    public async Task Stop_WithADirtyTree_Is409Dirty()
    {
        using Repo repo = Repo.Create(NoStopBacklog);
        TempFactoryRoot.AddStory(repo.Folder, "009-untracked.md", "# 009 Untracked\n");
        await AssertNoteRefused(repo, "{\"action\":\"stop_add\",\"line\":2}", HttpStatusCode.Conflict, "dirty");
    }

    [Fact]
    public async Task Stop_InAFolderWithoutItsOwnRepo_Is409NotDevelop()
    {
        using TempFactoryRoot root = new();
        root.AddProject("Alpha", StopBacklog);
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();
        IReadOnlyList<string> before = root.Snapshot();

        using HttpResponseMessage response = await client.PostAsync(
            Route, Json(new { project = "Alpha", version = "stale", action = "stop_remove", line = 3 }));

        await AssertError(response, HttpStatusCode.Conflict, "not_develop");
        Assert.Equal(before, root.Snapshot());
    }

    [Fact]
    public async Task Stop_WhileAJobHoldsTheCheckout_CommitsOnTheBacklogBranchAndNeverPushes()
    {
        using Repo repo = Repo.Create(NoStopBacklog);
        repo.Root.AddBareRemote(repo.Folder);
        TempFactoryRoot.Git(repo.Folder, "checkout", "-q", "-b", "factory/348-x");
        IReadOnlyList<string> before = repo.State();

        JsonElement result = await repo.Change(new { action = "stop_add", line = 2 });

        Assert.True(result.GetProperty("committed").GetBoolean());
        Assert.False(result.GetProperty("pushed").GetBoolean());
        Assert.False(result.TryGetProperty("warning", out _));
        repo.AssertBacklogCommit("backlog: stop line", ["backlog.md"]);
        Assert.Equal(
            NoStopBacklog.Replace("Open two\n", "Open two\n" + StopLine, StringComparison.Ordinal),
            File.ReadAllText(Path.Combine(repo.WorktreePath, "backlog.md")));
        Assert.Equal(before, repo.State());
        Assert.Equal("factory/348-x", TempFactoryRoot.Git(repo.Folder, "branch", "--show-current"));
    }

    [Fact]
    public async Task Place_TakesTheStopLineAsTargetAndKeepsEveryOtherLine()
    {
        using Repo repo = Repo.Create(StopBacklog);

        await repo.Change(new { action = "place", line = 7, target = 3, where = "before" });

        Assert.Equal(
            "# Alpha backlog\n- [x] [#1](backlog/001-done.md) Done one\n- [ ] [#2](backlog/002-open.md) Open two\n"
            + "- [ ] [#5](backlog/005-five.md) Five\nProse line.\n" + StopLine
            + "- [?] [#3](backlog/003-proposal.md) Proposal three\n- [ ] [#4](backlog/004-four.md) Four\n",
            File.ReadAllText(repo.BacklogPath));
        repo.AssertOneCommit("backlog: move #5", ["backlog.md"]);
    }

    [Fact]
    public async Task Top_GoesAboveTheStopLineWhenItComesFirst()
    {
        using Repo repo = Repo.Create("# A\n" + StopLine + "- [x] [#1](backlog/001-done.md) Done one\n- [ ] [#2](backlog/002-open.md) Open two\n");

        await repo.Change(new { action = "top", line = 3 });

        Assert.Equal(
            "# A\n- [ ] [#2](backlog/002-open.md) Open two\n" + StopLine + "- [x] [#1](backlog/001-done.md) Done one\n",
            File.ReadAllText(repo.BacklogPath));
        repo.AssertOneCommit("backlog: move #2 to top", ["backlog.md"]);
    }

    [Fact]
    public async Task Add_BottomGoesBelowTheStopLineAndTopAboveIt()
    {
        string backlog = "# A\n- [ ] [#2](backlog/002-open.md) Open two\nProse.\n" + StopLine + "Tail.\n";
        using Repo repo = Repo.Create(backlog);

        await repo.Change(new { action = "add", where = "bottom", title = "Bottom one", what = "w", why = "y" });
        await repo.Change(new { action = "add", where = "top", title = "Top one", what = "w", why = "y" });

        Assert.Equal(
            "# A\n- [ ] [#5](backlog/005-top-one.md) Top one\n- [ ] [#2](backlog/002-open.md) Open two\nProse.\n"
            + StopLine + "- [ ] [#4](backlog/004-bottom-one.md) Bottom one\nTail.\n",
            File.ReadAllText(repo.BacklogPath));
    }
}
