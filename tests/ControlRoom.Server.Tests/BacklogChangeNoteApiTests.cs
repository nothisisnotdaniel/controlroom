using System.Net;
using System.Text;
using System.Text.Json;

namespace ControlRoom.Server.Tests;

/// <summary>The <c>note</c> action of <c>POST /api/backlog/change</c>: a dated line under the story's
/// <c>## Notes</c>, only the story file committed. The date comes from the fake clock and zone, never the
/// machine's.</summary>
public sealed partial class BacklogChangeApiTests
{
    private const string NotesStory = "# 002 Open two\n\n## Notes\n\nProposed by job 12-x.\n\n## Later\n\nMore.\n";

    [Fact]
    public async Task Note_AppendsADatedLineOnTheLocalDateAndCommitsOnlyTheStory()
    {
        using Repo repo = Repo.Create();
        File.WriteAllText(Path.Combine(repo.Folder, "backlog", "002-open.md"), NotesStory);
        repo.CommitAll("notes");
        string remote = repo.Root.AddBareRemote(repo.Folder);
        repo.Factory.Time.Now = new DateTimeOffset(2026, 3, 1, 23, 30, 0, TimeSpan.Zero);
        repo.Factory.Time.Zone = TimeZoneInfo.CreateCustomTimeZone("Test+2", TimeSpan.FromHours(2), "Test+2", "Test+2");
        string version = repo.Version();

        JsonElement result = await repo.Change(new
        {
            action = "note", line = 3, storyVersion = repo.StoryVersion(2), note = "  Use the\n\nblue\tone. ",
        });

        Assert.Equal(
            "# 002 Open two\n\n## Notes\n\nProposed by job 12-x.\n\n- 2026-03-02: Use the blue one.\n\n## Later\n\nMore.\n",
            File.ReadAllText(Path.Combine(repo.Folder, "backlog", "002-open.md")));
        repo.AssertOneCommit("backlog: note on #2", ["backlog/002-open.md"]);
        Assert.True(result.GetProperty("committed").GetBoolean());
        Assert.True(result.GetProperty("pushed").GetBoolean());
        Assert.Equal(version, result.GetProperty("version").GetString());
        Assert.Equal(version, repo.Version());
        Assert.False(result.TryGetProperty("warning", out _));
        Assert.Equal(
            TempFactoryRoot.Git(repo.Folder, "rev-parse", "HEAD"),
            TempFactoryRoot.Git(remote, "rev-parse", "refs/heads/develop"));
        Assert.Equal("", TempFactoryRoot.Git(repo.Folder, "status", "--porcelain"));
    }

    [Fact]
    public async Task Note_OnAProposalWithoutANotesSectionAddsTheSection()
    {
        using Repo repo = Repo.Create();

        JsonElement result = await repo.Change(new
        {
            action = "note", line = 5, storyVersion = repo.StoryVersion(3), note = "Ask first.",
        });

        Assert.Equal(
            "# Something else\n\n## Notes\n\n- 2026-03-01: Ask first.\n",
            File.ReadAllText(Path.Combine(repo.Folder, "backlog", "003-proposal.md")));
        repo.AssertOneCommit("backlog: note on #3", ["backlog/003-proposal.md"]);
        Assert.False(result.GetProperty("pushed").GetBoolean());
    }

    [Fact]
    public async Task Note_OnAHandItemKeepsCrlfAndTheBom()
    {
        using Repo repo = Repo.Create(HandBacklog);
        string path = Path.Combine(repo.Folder, "backlog", "002-open.md");
        File.WriteAllBytes(path, Encoding.UTF8.GetBytes("\uFEFF# 002 Hand two\r\n\r\n## Notes\r\n\r\n- 2026-01-01: a\r\n"));
        repo.CommitAll("crlf");

        await repo.Change(new { action = "note", line = 2, storyVersion = repo.StoryVersion(2), note = "b" });

        Assert.Equal(
            Encoding.UTF8.GetBytes("\uFEFF# 002 Hand two\r\n\r\n## Notes\r\n\r\n- 2026-01-01: a\r\n- 2026-03-01: b\r\n"),
            File.ReadAllBytes(path));
        repo.AssertOneCommit("backlog: note on #2", ["backlog/002-open.md"]);
    }

    [Fact]
    public async Task Note_ComesBackFromTheStoryAsPlainText()
    {
        using Repo repo = Repo.Create();
        await repo.Change(new
        {
            action = "note", line = 3, storyVersion = repo.StoryVersion(2), note = "<script>alert(1)</script>",
        });
        using HttpClient client = repo.Factory.CreateTokenClient();

        using JsonDocument story = JsonDocument.Parse(await client.GetStringAsync("/api/story?project=Alpha&number=2"));

        Assert.EndsWith("- 2026-03-01: <script>alert(1)</script>\n", story.RootElement.GetProperty("markdown").GetString(),
            StringComparison.Ordinal);
        JsonElement list = story.RootElement.GetProperty("blocks").EnumerateArray()
            .Single(block => block.GetProperty("kind").GetString() == "list");
        JsonElement[] inlines = list.GetProperty("items")[0].EnumerateArray().ToArray();
        Assert.All(inlines, inline => Assert.Equal("text", inline.GetProperty("kind").GetString()));
        Assert.Equal(
            "2026-03-01: <script>alert(1)</script>",
            string.Concat(inlines.Select(inline => inline.GetProperty("text").GetString())));
    }

    public static TheoryData<string> InvalidNoteBodies()
    {
        return
        [
            "{\"action\":\"note\",\"line\":3,\"storyVersion\":\"S\",\"note\":\"\"}",
            "{\"action\":\"note\",\"line\":3,\"storyVersion\":\"S\",\"note\":\" \\n\\t \"}",
            "{\"action\":\"note\",\"line\":3,\"storyVersion\":\"S\",\"note\":\"" + new string('a', 1001) + "\"}",
            "{\"action\":\"note\",\"line\":3,\"storyVersion\":\"S\",\"note\":\"bell \\u0007\"}",
            "{\"action\":\"note\",\"line\":3,\"storyVersion\":\"S\",\"note\":\"lone \\ud800\"}",
            "{\"action\":\"note\",\"line\":3,\"storyVersion\":\"S\",\"note\":\"see PROPOSED by\\njob 9-x\"}",
            "{\"action\":\"note\",\"line\":3,\"storyVersion\":\"S\",\"note\":5}",
            "{\"action\":\"note\",\"line\":3,\"storyVersion\":\"S\"}",
            "{\"action\":\"note\",\"line\":3,\"note\":\"n\"}",
            "{\"action\":\"note\",\"line\":3,\"storyVersion\":7,\"note\":\"n\"}",
            "{\"action\":\"note\",\"line\":\"3\",\"storyVersion\":\"S\",\"note\":\"n\"}",
            "{\"action\":\"note\",\"storyVersion\":\"S\",\"note\":\"n\"}",
            "{\"action\":\"note\",\"line\":2,\"storyVersion\":\"S1\",\"note\":\"n\"}",
            "{\"action\":\"note\",\"line\":6,\"storyVersion\":\"S\",\"note\":\"n\"}",
            "{\"action\":\"note\",\"line\":7,\"storyVersion\":\"S\",\"note\":\"n\"}",
        ];
    }

    [Theory]
    [MemberData(nameof(InvalidNoteBodies))]
    public async Task Note_InvalidRequest_Is400InvalidAndWritesNothing(string fields)
    {
        using Repo repo = Repo.Create();
        await AssertNoteRefused(repo, fields, HttpStatusCode.BadRequest, "invalid");
    }

    [Fact]
    public async Task Note_OnAMissingStory_Is400Invalid()
    {
        using Repo repo = Repo.Create();
        string version = repo.StoryVersion(2);
        File.Delete(Path.Combine(repo.Folder, "backlog", "002-open.md"));
        repo.CommitAll("gone");

        await AssertNoteRefused(
            repo, "{\"action\":\"note\",\"line\":3,\"storyVersion\":\"" + version + "\",\"note\":\"n\"}",
            HttpStatusCode.BadRequest, "invalid");
    }

    [Fact]
    public async Task Note_OnASymlinkedStory_Is400InvalidAndLeavesTheTargetAlone()
    {
        using Repo repo = Repo.Create();
        string story = Path.Combine(repo.Folder, "backlog", "002-open.md");
        string outside = Path.Combine(repo.Root.Path, "outside.md");
        File.WriteAllText(outside, StoryTwo);
        File.Delete(story);
        File.CreateSymbolicLink(story, outside);
        repo.CommitAll("link");

        await AssertNoteRefused(repo, NoteFields(StoryTwo), HttpStatusCode.BadRequest, "invalid");
        Assert.Equal(StoryTwo, File.ReadAllText(outside));
    }

    [Fact]
    public async Task Note_OnAStoryOverTheReadLimit_Is400Invalid()
    {
        using Repo repo = Repo.Create();
        string big = "# 002 Open two\n\n" + new string('x', (int)StoryReader.MaxBytes) + "\n";
        File.WriteAllText(Path.Combine(repo.Folder, "backlog", "002-open.md"), big);
        repo.CommitAll("big");

        await AssertNoteRefused(repo, NoteFields(big), HttpStatusCode.BadRequest, "invalid");
    }

    [Fact]
    public async Task Note_ThatWouldMakeTheStoryTooLarge_Is400Invalid()
    {
        using Repo repo = Repo.Create();
        string head = "# 002 Open two\n\n## Notes\n\n";
        string story = head + new string('x', BacklogChangeEndpoint.MaxStoryBytes - head.Length - 1) + "\n";
        Assert.Equal(BacklogChangeEndpoint.MaxStoryBytes, Encoding.UTF8.GetByteCount(story));
        File.WriteAllText(Path.Combine(repo.Folder, "backlog", "002-open.md"), story);
        repo.CommitAll("full");

        await AssertNoteRefused(repo, NoteFields(story), HttpStatusCode.BadRequest, "invalid");
    }

    [Fact]
    public async Task Note_WithAStaleStoryVersion_Is409Changed()
    {
        using Repo repo = Repo.Create();
        await AssertNoteRefused(repo, NoteFields("# 002 Open two\n\nOld.\n"), HttpStatusCode.Conflict, "changed");
    }

    [Fact]
    public async Task Note_WithAStaleBacklogVersion_Is409Changed()
    {
        using Repo repo = Repo.Create();
        await AssertNoteRefused(
            repo, NoteFields(StoryTwo), HttpStatusCode.Conflict, "changed", version: new string('0', 64));
    }

    [Fact]
    public async Task Note_WithADirtyStoryFolder_Is409Dirty()
    {
        using Repo repo = Repo.Create();
        TempFactoryRoot.AddStory(repo.Folder, "009-untracked.md", "# 009 Untracked\n");
        await AssertNoteRefused(repo, NoteFields(StoryTwo), HttpStatusCode.Conflict, "dirty");
    }

    [Fact]
    public async Task Note_OnAnotherBranch_Is409NotDevelop()
    {
        using Repo repo = Repo.Create();
        TempFactoryRoot.Git(repo.Folder, "checkout", "-q", "-b", "feature");
        await AssertNoteRefused(repo, NoteFields(StoryTwo), HttpStatusCode.Conflict, "not_develop");
        Assert.Equal("feature", TempFactoryRoot.Git(repo.Folder, "branch", "--show-current"));
    }

    private static string NoteFields(string story)
    {
        return "{\"action\":\"note\",\"line\":3,\"storyVersion\":\"" + Sha(Encoding.UTF8.GetBytes(story))
            + "\",\"note\":\"n\"}";
    }

    /// <summary>Posts the fields (with project and the current backlog version unless given; <c>"S"</c> becomes
    /// story #2's version, <c>"S1"</c> story #1's) and checks the refusal, that no file changed and that HEAD and
    /// the commit count stayed.</summary>
    private static async Task AssertNoteRefused(
        Repo repo, string fields, HttpStatusCode status, string error, string? version = null)
    {
        string count = TempFactoryRoot.Git(repo.Folder, "rev-list", "--count", "HEAD");
        string body = "{\"project\":\"Alpha\",\"version\":\"" + (version ?? repo.Version()) + "\"," + fields[1..];
        body = body.Replace("\"S1\"", "\"" + repo.StoryVersion(1) + "\"", StringComparison.Ordinal);
        if (body.Contains("\"S\"", StringComparison.Ordinal))
        {
            body = body.Replace("\"S\"", "\"" + repo.StoryVersion(2) + "\"", StringComparison.Ordinal);
        }

        using HttpClient client = repo.Factory.CreateTokenClient();
        IReadOnlyList<string> before = repo.State();

        using HttpResponseMessage response = await client.PostAsync(
            Route, new StringContent(body, Encoding.UTF8, "application/json"));

        await AssertError(response, status, error);
        Assert.Equal(before, repo.State());
        Assert.Equal(count, TempFactoryRoot.Git(repo.Folder, "rev-list", "--count", "HEAD"));
    }
}
