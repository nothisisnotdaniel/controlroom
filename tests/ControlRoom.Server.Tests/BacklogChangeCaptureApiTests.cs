using System.Net;
using System.Text;
using System.Text.Json;

namespace ControlRoom.Server.Tests;

/// <summary>The <c>capture</c> action of <c>POST /api/backlog/change</c>: a numbered <c>[?]</c> draft after the last
/// item and a draft story, committed together as <c>backlog: capture #N</c>. The Notes date comes from the fake clock
/// and zone, never the machine's.</summary>
public sealed partial class BacklogChangeApiTests
{
    private const string LongIdea =
        "  Show the jobs board on the phone\n with a compact layout and big buttons.  ";

    private const string LongTitle = "Show the jobs board on the phone with a compact layout and";

    private const string LongStoryPath = "backlog/004-show-the-jobs-board-on-the.md";

    [Fact]
    public async Task Capture_AppendsADraftLineAndStoryInOneCommitAndPushes()
    {
        using Repo repo = Repo.Create();
        string remote = repo.Root.AddBareRemote(repo.Folder);
        repo.Factory.Time.Now = new DateTimeOffset(2026, 3, 1, 23, 30, 0, TimeSpan.Zero);
        repo.Factory.Time.Zone = TimeZoneInfo.CreateCustomTimeZone("Test+2", TimeSpan.FromHours(2), "Test+2", "Test+2");

        JsonElement result = await repo.Change(new { action = "capture", text = LongIdea });

        Assert.Equal(
            AlphaBacklog + "- [?] [#4](" + LongStoryPath + ") " + LongTitle + "\n",
            File.ReadAllText(repo.BacklogPath));
        Assert.Equal(
            Encoding.UTF8.GetBytes(
                "# 004 " + LongTitle + "\n\n## What\n\nShow the jobs board on the phone with a compact layout and big "
                + "buttons.\n\n## Why\n\nStill to be written: this is a quick-capture draft.\n\n## Notes\n\n"
                + "- 2026-03-02: Draft from ControlRoom\n"),
            File.ReadAllBytes(Path.Combine(repo.Folder, LongStoryPath)));
        repo.AssertOneCommit("backlog: capture #4", ["backlog.md", LongStoryPath]);
        Assert.True(result.GetProperty("committed").GetBoolean());
        Assert.True(result.GetProperty("pushed").GetBoolean());
        Assert.Equal(repo.Version(), result.GetProperty("version").GetString());
        Assert.False(result.TryGetProperty("warning", out _));
        Assert.Equal(
            TempFactoryRoot.Git(repo.Folder, "rev-parse", "HEAD"),
            TempFactoryRoot.Git(remote, "rev-parse", "refs/heads/develop"));
        Assert.Equal("", TempFactoryRoot.Git(repo.Folder, "status", "--porcelain"));
    }

    [Fact]
    public async Task Capture_InACrlfBacklogGoesAfterTheLastItemAndKeepsCrlf()
    {
        using Repo repo = Repo.Create("# B\r\n- [ ] [#1](backlog/001-done.md) One\r\nProse.\r\n");

        JsonElement result = await repo.Change(new { action = "capture", text = "Idea." });

        Assert.Equal(
            Encoding.UTF8.GetBytes("# B\r\n- [ ] [#1](backlog/001-done.md) One\r\n- [?] [#4](backlog/004-idea.md) Idea\r\n"
                + "Prose.\r\n"),
            File.ReadAllBytes(repo.BacklogPath));
        Assert.Equal(
            "# 004 Idea\n\n## What\n\nIdea.\n\n## Why\n\nStill to be written: this is a quick-capture draft.\n\n"
            + "## Notes\n\n- 2026-03-01: Draft from ControlRoom\n",
            File.ReadAllText(Path.Combine(repo.Folder, "backlog", "004-idea.md")));
        repo.AssertOneCommit("backlog: capture #4", ["backlog.md", "backlog/004-idea.md"]);
        Assert.False(result.GetProperty("pushed").GetBoolean());
    }

    [Fact]
    public async Task Capture_WithoutItemLinesGoesAtTheEnd()
    {
        using Repo repo = Repo.Create("# B\n\nNothing yet.\n");

        await repo.Change(new { action = "capture", text = "First idea" });

        Assert.Equal("# B\n\nNothing yet.\n- [?] [#4](backlog/004-first-idea.md) First idea\n",
            File.ReadAllText(repo.BacklogPath));
        repo.AssertOneCommit("backlog: capture #4", ["backlog.md", "backlog/004-first-idea.md"]);
    }

    [Fact]
    public async Task Capture_KeepsTheCommitWhenThePushFails()
    {
        using Repo repo = Repo.Create();
        TempFactoryRoot.Git(repo.Folder, "remote", "add", "origin", Path.Combine(repo.Root.Path, "missing.git"));

        JsonElement result = await repo.Change(new { action = "capture", text = "Idea" });

        Assert.True(result.GetProperty("committed").GetBoolean());
        Assert.False(result.GetProperty("pushed").GetBoolean());
        Assert.Equal("push_failed", result.GetProperty("warning").GetString());
        repo.AssertOneCommit("backlog: capture #4", ["backlog.md", "backlog/004-idea.md"]);
    }

    [Fact]
    public async Task Capture_ShowsInTheInboxWithoutAJobAndInTheProposedGroup()
    {
        using Repo repo = Repo.Create();
        await repo.Change(new { action = "capture", text = "Idea" });
        using HttpClient client = repo.Factory.CreateTokenClient();

        using JsonDocument inbox = JsonDocument.Parse(await client.GetStringAsync("/api/inbox"));
        using JsonDocument backlogs = JsonDocument.Parse(await client.GetStringAsync("/api/backlogs"));

        JsonElement item = inbox.RootElement.GetProperty("items").EnumerateArray()
            .Single(entry => entry.GetProperty("number").ValueKind == JsonValueKind.Number
                && entry.GetProperty("number").GetInt32() == 4);
        Assert.Equal("Alpha", item.GetProperty("project").GetString());
        Assert.Equal("Idea", item.GetProperty("title").GetString());
        Assert.Equal(8, item.GetProperty("line").GetInt32());
        Assert.Equal(JsonValueKind.Null, item.GetProperty("job").ValueKind);
        JsonElement alpha = backlogs.RootElement.GetProperty("projects").EnumerateArray()
            .Single(project => project.GetProperty("name").GetString() == "Alpha");
        JsonElement draft = alpha.GetProperty("approval").EnumerateArray()
            .Single(entry => entry.GetProperty("number").ValueKind == JsonValueKind.Number
                && entry.GetProperty("number").GetInt32() == 4);
        Assert.Equal("Idea", draft.GetProperty("title").GetString());
        Assert.Equal("backlog/004-idea.md", draft.GetProperty("story").GetString());
    }

    public static TheoryData<string> InvalidCaptureBodies()
    {
        return
        [
            "{\"action\":\"capture\"}",
            "{\"action\":\"capture\",\"text\":5}",
            "{\"action\":\"capture\",\"text\":null}",
            "{\"action\":\"capture\",\"text\":\"\"}",
            "{\"action\":\"capture\",\"text\":\" \\n\\t \"}",
            "{\"action\":\"capture\",\"text\":\"" + new string('a', 2001) + "\"}",
            "{\"action\":\"capture\",\"text\":\"bell \\u0007\"}",
            "{\"action\":\"capture\",\"text\":\"lone \\ud800\"}",
            "{\"action\":\"capture\",\"text\":\"see PROPOSED by\\njob 9-x\"}",
            "{\"action\":\"capture\",\"text\":\"Move it \\u2192 later\"}",
            "{\"action\":\"capture\",\"text\":\"Wait (after: #3)\"}",
            "{\"action\":\"capture\",\"text\":\"?!.\"}",
            "{\"action\":\"capture\",\"text\":\"Idea\",\"unknown\":1}",
        ];
    }

    [Theory]
    [MemberData(nameof(InvalidCaptureBodies))]
    public async Task Capture_InvalidRequest_Is400InvalidAndWritesNothing(string fields)
    {
        using Repo repo = Repo.Create();
        await AssertCaptureRefused(repo, fields, HttpStatusCode.BadRequest, "invalid");
    }

    [Fact]
    public async Task Capture_OfExactly2000Runes_IsAccepted()
    {
        using Repo repo = Repo.Create();

        await repo.Change(new { action = "capture", text = new string('a', 2000) });

        repo.AssertOneCommit("backlog: capture #4", ["backlog.md", "backlog/004-" + new string('a', 50) + ".md"]);
    }

    [Fact]
    public async Task Capture_IntoAnUnknownProject_Is400Invalid()
    {
        using Repo repo = Repo.Create();
        await AssertCaptureRefused(repo, "{\"action\":\"capture\",\"text\":\"Idea\"}", HttpStatusCode.BadRequest, "invalid",
            project: "alpha");
    }

    [Fact]
    public async Task Capture_WithAStaleVersion_Is409Changed()
    {
        using Repo repo = Repo.Create();
        await AssertCaptureRefused(repo, "{\"action\":\"capture\",\"text\":\"Idea\"}", HttpStatusCode.Conflict, "changed",
            version: new string('0', 64));
    }

    [Fact]
    public async Task Capture_WithADirtyBacklog_Is409Dirty()
    {
        using Repo repo = Repo.Create();
        File.AppendAllText(repo.BacklogPath, "- [ ] Uncommitted\n");
        await AssertCaptureRefused(repo, "{\"action\":\"capture\",\"text\":\"Idea\"}", HttpStatusCode.Conflict, "dirty");
    }

    [Fact]
    public async Task Capture_OnAnotherBranch_CommitsOnBacklogAndTheCheckoutStays()
    {
        using Repo repo = Repo.Create();
        TempFactoryRoot.Git(repo.Folder, "checkout", "-q", "-b", "feature");
        IReadOnlyList<string> before = repo.State();

        JsonElement result = await repo.Change(new { action = "capture", text = "Idea" });

        Assert.False(result.GetProperty("pushed").GetBoolean());
        Assert.Equal(before, repo.State());
        Assert.Equal("feature", TempFactoryRoot.Git(repo.Folder, "branch", "--show-current"));
        string story = Path.GetFileName(Directory.GetFiles(Path.Combine(repo.WorktreePath, "backlog"), "004-*.md").Single());
        repo.AssertBacklogCommit("backlog: capture #4", ["backlog.md", "backlog/" + story]);
    }

    [Fact]
    public async Task Capture_WithASymlinkedStoryFolder_Is400InvalidAndWritesNothingThere()
    {
        using Repo repo = Repo.Create();
        string folder = Path.Combine(repo.Folder, "backlog");
        string outside = Path.Combine(repo.Root.Path, "outside");
        Directory.CreateDirectory(outside);
        Directory.Delete(folder, recursive: true);
        Directory.CreateSymbolicLink(folder, outside);
        repo.CommitAll("link");

        await AssertCaptureRefused(repo, "{\"action\":\"capture\",\"text\":\"Idea\"}", HttpStatusCode.BadRequest, "invalid");
        Assert.Empty(Directory.EnumerateFileSystemEntries(outside));
    }

    [Fact]
    public void StoryPathTaken_IsTrueForAFileAFolderAndADanglingSymlink()
    {
        using TempFactoryRoot root = new();
        string file = Path.Combine(root.Path, "004-file.md");
        string folder = Path.Combine(root.Path, "004-folder.md");
        string link = Path.Combine(root.Path, "004-link.md");
        File.WriteAllText(file, "x");
        Directory.CreateDirectory(folder);
        File.CreateSymbolicLink(link, Path.Combine(root.Path, "missing.md"));

        Assert.True(BacklogChangeEndpoint.StoryPathTaken(file));
        Assert.True(BacklogChangeEndpoint.StoryPathTaken(folder));
        Assert.True(BacklogChangeEndpoint.StoryPathTaken(link));
        Assert.False(BacklogChangeEndpoint.StoryPathTaken(Path.Combine(root.Path, "004-free.md")));
    }

    /// <summary>Posts the fields (with the project and the current backlog version unless given) and checks the
    /// refusal, that no file changed and that HEAD and the commit count stayed.</summary>
    private static async Task AssertCaptureRefused(
        Repo repo, string fields, HttpStatusCode status, string error, string project = "Alpha", string? version = null)
    {
        string count = TempFactoryRoot.Git(repo.Folder, "rev-list", "--count", "HEAD");
        string body = "{\"project\":\"" + project + "\",\"version\":\"" + (version ?? repo.Version()) + "\","
            + fields[1..];
        using HttpClient client = repo.Factory.CreateTokenClient();
        IReadOnlyList<string> before = repo.State();

        using HttpResponseMessage response = await client.PostAsync(
            Route, new StringContent(body, Encoding.UTF8, "application/json"));

        await AssertError(response, status, error);
        Assert.Equal(before, repo.State());
        Assert.Equal(count, TempFactoryRoot.Git(repo.Folder, "rev-list", "--count", "HEAD"));
    }
}
