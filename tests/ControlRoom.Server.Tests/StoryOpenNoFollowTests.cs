using System.Net;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace ControlRoom.Server.Tests;

/// <summary><see cref="SafeFileReader.ReadBytesAt(string, string, long)"/> and the folder-handle read behind
/// <see cref="StoryReader"/>: the <c>backlog</c> folder is opened once without following a symlink and the story
/// file relative to it, so a symlink swapped in after the open can't redirect the read. Each test has its own
/// temp root with an <c>outside</c> folder inside it. Unix only (Windows keeps the managed check-then-open).</summary>
public sealed class StoryOpenNoFollowTests
{
    private const long Max = 64;

    private const string Name = "004-story.md";

    private static readonly byte[] Story = Encoding.UTF8.GetBytes("# Story four\n");

    private static readonly byte[] Secret = Encoding.UTF8.GetBytes("secret\n");

    [Fact]
    public void ReadBytesAt_ReturnsTheExactBytesOfARegularFile()
    {
        using Layout layout = new();
        byte[] bytes = [0xEF, 0xBB, 0xBF, (byte)'a', 0x00, 0xFF, (byte)'\n'];
        File.WriteAllBytes(Path.Combine(layout.Backlog, Name), bytes);

        Assert.Equal(bytes, SafeFileReader.ReadBytesAt(layout.Backlog, Name, Max));
    }

    [Fact]
    public void ReadBytesAt_ReadsAFileOfExactlyMaxBytes()
    {
        using Layout layout = new();
        byte[] bytes = Encoding.UTF8.GetBytes(new string('a', (int)Max));
        File.WriteAllBytes(Path.Combine(layout.Backlog, Name), bytes);

        Assert.Equal(bytes, SafeFileReader.ReadBytesAt(layout.Backlog, Name, Max));
    }

    [Fact]
    public void ReadBytesAt_FolderThatIsASymlinkToARealFolderIsRefused()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using Layout layout = new();
        string link = Path.Combine(layout.Project, "linked");
        Directory.CreateSymbolicLink(link, layout.Outside);

        Assert.Null(SafeFileReader.ReadBytesAt(link, Name, Max));
    }

    [Fact]
    public void ReadBytesAt_FolderThatIsADanglingSymlinkIsRefused()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using Layout layout = new();
        string link = Path.Combine(layout.Project, "dangling");
        Directory.CreateSymbolicLink(link, Path.Combine(layout.Root.Path, "nowhere"));

        Assert.Null(SafeFileReader.ReadBytesAt(link, Name, Max));
    }

    [Fact]
    public void ReadBytesAt_FolderThatIsARegularFileIsRefused()
    {
        using Layout layout = new();
        string file = Path.Combine(layout.Project, "file");
        File.WriteAllBytes(file, Secret);

        Assert.Null(SafeFileReader.ReadBytesAt(file, Name, Max));
    }

    [Fact]
    public void ReadBytesAt_MissingFolderIsRefused()
    {
        using Layout layout = new();

        Assert.Null(SafeFileReader.ReadBytesAt(Path.Combine(layout.Project, "missing"), Name, Max));
    }

    [Fact]
    public void ReadBytesAt_FileThatIsASymlinkToARegularFileIsRefused()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using Layout layout = new();
        File.CreateSymbolicLink(Path.Combine(layout.Backlog, Name), layout.OutsideSecret);

        Assert.Null(SafeFileReader.ReadBytesAt(layout.Backlog, Name, Max));
    }

    [Fact]
    public void ReadBytesAt_FileThatIsADanglingSymlinkIsRefused()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using Layout layout = new();
        File.CreateSymbolicLink(Path.Combine(layout.Backlog, Name), Path.Combine(layout.Root.Path, "nowhere.md"));

        Assert.Null(SafeFileReader.ReadBytesAt(layout.Backlog, Name, Max));
    }

    [Fact]
    public void ReadBytesAt_SubfolderIsRefused()
    {
        using Layout layout = new();
        Directory.CreateDirectory(Path.Combine(layout.Backlog, Name));

        Assert.Null(SafeFileReader.ReadBytesAt(layout.Backlog, Name, Max));
    }

    [Fact]
    public async Task ReadBytesAt_FifoIsRefusedWithoutBlocking()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using Layout layout = new();
        TempFactoryRoot.MakeFifo(Path.Combine(layout.Backlog, Name));

        // Bounded so a blocking open fails the test instead of hanging it; no reader is ever attached.
        TimeSpan fifoTimeout = TimeSpan.FromSeconds(10);
        byte[]? bytes = await Task.Run(() => SafeFileReader.ReadBytesAt(layout.Backlog, Name, Max))
            .WaitAsync(fifoTimeout);

        Assert.Null(bytes);
    }

    [Fact]
    public void ReadBytesAt_FileOverMaxBytesIsRefused()
    {
        using Layout layout = new();
        File.WriteAllBytes(Path.Combine(layout.Backlog, Name), Encoding.UTF8.GetBytes(new string('a', (int)Max + 1)));

        Assert.Null(SafeFileReader.ReadBytesAt(layout.Backlog, Name, Max));
    }

    [Fact]
    public void ReadBytesAt_MissingFileIsRefused()
    {
        using Layout layout = new();

        Assert.Null(SafeFileReader.ReadBytesAt(layout.Backlog, Name, Max));
    }

    [Theory]
    [InlineData("")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("sub/004-story.md")]
    [InlineData("../outside/004-story.md")]
    [InlineData("/004-story.md")]
    [InlineData("004-story.md\0")]
    [InlineData("004\0-story.md")]
    public void ReadBytesAt_NameThatIsNotOnePathComponentIsRefused(string name)
    {
        using Layout layout = new();
        File.WriteAllBytes(Path.Combine(layout.Backlog, Name), Story);
        Directory.CreateDirectory(Path.Combine(layout.Backlog, "sub"));
        File.WriteAllBytes(Path.Combine(layout.Backlog, "sub", Name), Secret);
        File.WriteAllBytes(Path.Combine(layout.Project, Name), Secret);

        Assert.Null(SafeFileReader.ReadBytesAt(layout.Backlog, name, Max));
        if (!OperatingSystem.IsWindows())
        {
            using SafeFileHandle folder = SafeFileReader.OpenFolder(layout.Backlog);
            Assert.False(folder.IsInvalid);
            Assert.Null(SafeFileReader.ReadBytesAt(folder, name, Max));
        }
    }

    [Fact]
    public void OpenFolder_IsInvalidForASymlinkAFileOrAMissingPath()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using Layout layout = new();
        string link = Path.Combine(layout.Project, "linked");
        Directory.CreateSymbolicLink(link, layout.Outside);
        string file = Path.Combine(layout.Project, "file");
        File.WriteAllBytes(file, Secret);

        foreach (string path in new[] { link, file, Path.Combine(layout.Project, "missing") })
        {
            using SafeFileHandle handle = SafeFileReader.OpenFolder(path);
            Assert.True(handle.IsInvalid, path);
        }
    }

    [Fact]
    public void ReadBytesAt_FolderSwappedForASymlinkAfterTheOpenStillReadsTheOriginalFolder()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using Layout layout = new();
        File.WriteAllBytes(Path.Combine(layout.Backlog, Name), Story);
        File.WriteAllBytes(Path.Combine(layout.Outside, Name), Secret);
        using SafeFileHandle folder = SafeFileReader.OpenFolder(layout.Backlog);
        Assert.False(folder.IsInvalid);

        Directory.Move(layout.Backlog, Path.Combine(layout.Project, "backlog-moved"));
        Directory.CreateSymbolicLink(layout.Backlog, layout.Outside);

        Assert.Equal(Story, SafeFileReader.ReadBytesAt(folder, Name, Max));
        Assert.Null(SafeFileReader.ReadBytesAt(layout.Backlog, Name, Max));
    }

    [Fact]
    public void ReadBytesAt_FileSwappedForASymlinkAfterTheOpenIsRefused()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using Layout layout = new();
        string story = Path.Combine(layout.Backlog, Name);
        File.WriteAllBytes(story, Story);
        using SafeFileHandle folder = SafeFileReader.OpenFolder(layout.Backlog);
        Assert.False(folder.IsInvalid);

        File.Delete(story);
        File.CreateSymbolicLink(story, layout.OutsideSecret);

        Assert.Null(SafeFileReader.ReadBytesAt(folder, Name, Max));
    }

    public static TheoryData<string, string, string?> StoryCases()
    {
        return new TheoryData<string, string, string?>
        {
            { "Alpha", "[#4](backlog/004-edit-backlogs.md) Edit backlogs", "# Story four\n\nWhy it matters.\n" },
            { "Alpha", "[#3](backlog/003-done.md) Done three", "Done story\n" },
            { "Alpha", "[#6](backlog/../006-up.md) Dot dot", null },
            { "Alpha", "[#7](backlog/sub/007-sub.md) Sub folder", null },
            { "Alpha", "[#8](/backlog/008-root.md) Leading slash", null },
            { "Alpha", "[#9](backlog\\009-back.md) Backslash", null },
            { "Alpha", "[#10](backlog/%2e%2e/010-enc.md) Encoded dots", null },
            { "Alpha", "[#11](backlog/012-other.md) Other number", null },
            { "Alpha", "[#12](backlog/012-missing.md) Missing file", null },
            { "Alpha", "[#13](backlog/013-link.md) Symlinked file", null },
            { "Alpha", "[#15](backlog/015-big.md) Oversized", null },
            { "Alpha", "[#16](backlog/016-x.txt) Not markdown", null },
            { "Alpha", "[#17](backlog/17-short.md) Short number", null },
            { "Alpha", "[#18](backlog/018-a_b.md) Underscore", null },
            { "Alpha", "[#19](CLAUDE.md) Other file", null },
            { "Alpha", "Old line: no number", null },
            { "LinkedFolder", "[#1](backlog/001-a.md) Linked", null },
            { "FileFolder", "[#1](backlog/001-a.md) File", null },
            { "NoBacklog", "[#1](backlog/001-a.md) None", null },
        };
    }

    [Theory]
    [MemberData(nameof(StoryCases))]
    public void StoryReader_GivesTheSameResultsAsBeforeForEverySampleCase(string project, string line, string? expected)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using TempFactoryRoot root = Sample();
        ProjectFolder folder = new(project, Path.Combine(root.Path, project));
        BacklogEntry entry = BacklogParser.ToEntry(line);

        byte[]? bytes = StoryReader.ReadBytes(folder, entry);
        string? text = StoryReader.Read(folder, entry);

        Assert.Equal(expected, bytes is null ? null : Encoding.UTF8.GetString(bytes));
        Assert.Equal(expected, text);
    }

    /// <summary>The same files as <c>StoryApiTests.Sample()</c>: one good story, a file behind every bad target, and
    /// projects whose <c>backlog</c> is a symlink, a file or missing.</summary>
    private static TempFactoryRoot Sample()
    {
        TempFactoryRoot root = new();
        File.WriteAllText(Path.Combine(root.Path, "CLAUDE.md"), "# root file\n");
        string alpha = root.AddProject("Alpha", null);
        string good = TempFactoryRoot.AddStory(alpha, "004-edit-backlogs.md", "# Story four\n\nWhy it matters.\n");
        TempFactoryRoot.AddStory(alpha, "003-done.md", "Done story\n");
        File.WriteAllText(Path.Combine(alpha, "006-up.md"), "secret up\n");
        Directory.CreateDirectory(Path.Combine(alpha, "backlog", "sub"));
        File.WriteAllText(Path.Combine(alpha, "backlog", "sub", "007-sub.md"), "secret sub\n");
        TempFactoryRoot.AddStory(alpha, "008-root.md", "secret root\n");
        TempFactoryRoot.AddStory(alpha, "009-back.md", "secret back\n");
        File.WriteAllText(Path.Combine(alpha, "010-enc.md"), "secret enc\n");
        TempFactoryRoot.AddStory(alpha, "012-other.md", "secret other\n");
        File.CreateSymbolicLink(Path.Combine(alpha, "backlog", "013-link.md"), good);
        TempFactoryRoot.AddStory(alpha, "015-big.md", new string('a', (int)StoryReader.MaxBytes + 1));
        TempFactoryRoot.AddStory(alpha, "016-x.txt", "secret txt\n");
        TempFactoryRoot.AddStory(alpha, "17-short.md", "secret short\n");
        TempFactoryRoot.AddStory(alpha, "018-a_b.md", "secret underscore\n");

        string linked = root.AddProject("LinkedFolder", null);
        string inside = Path.Combine(linked, "real");
        Directory.CreateDirectory(inside);
        File.WriteAllText(Path.Combine(inside, "001-a.md"), "secret linked\n");
        Directory.CreateSymbolicLink(Path.Combine(linked, "backlog"), inside);

        string fileFolder = root.AddProject("FileFolder", null);
        File.WriteAllText(Path.Combine(fileFolder, "backlog"), "secret file\n");

        root.AddProject("NoBacklog", null);
        return root;
    }

    /// <summary>A temp root with <c>Alpha/backlog/</c> and a sibling <c>outside/</c> folder holding
    /// <c>secret.md</c>; both are deleted with the root.</summary>
    private sealed class Layout : IDisposable
    {
        public Layout()
        {
            Project = Root.AddProject("Alpha", null);
            Backlog = Path.Combine(Project, "backlog");
            Directory.CreateDirectory(Backlog);
            Outside = Path.Combine(Root.Path, "outside");
            Directory.CreateDirectory(Outside);
            OutsideSecret = Path.Combine(Outside, "secret.md");
            File.WriteAllBytes(OutsideSecret, Secret);
        }

        public TempFactoryRoot Root { get; } = new();

        public string Project { get; }

        public string Backlog { get; }

        public string Outside { get; }

        public string OutsideSecret { get; }

        public void Dispose()
        {
            Root.Dispose();
        }
    }
}

/// <summary>Story edits on an item whose story file is a symlink to a file outside the project: every read goes
/// through <see cref="StoryReader.ReadBytes"/>, so the outside file is never read, changed or committed.</summary>
public sealed partial class BacklogChangeApiTests
{
    public static TheoryData<string> SymlinkedStoryEdits()
    {
        return
        [
            "{\"action\":\"note\",\"line\":3,\"storyVersion\":\"O\",\"note\":\"n\"}",
            "{\"action\":\"story\",\"line\":3,\"storyVersion\":\"O\",\"markdown\":\"# changed\\n\"}",
            "{\"action\":\"category\",\"line\":3,\"storyVersion\":\"O\",\"category\":\"bug\"}",
        ];
    }

    [Theory]
    [MemberData(nameof(SymlinkedStoryEdits))]
    public async Task StoryEdit_OnASymlinkedStoryIsRefusedAndTheOutsideFileStays(string fields)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using Repo repo = Repo.Create();
        string outside = LinkStoryTwoOutside(repo);
        string count = TempFactoryRoot.Git(repo.Folder, "rev-list", "--count", "HEAD");
        string body = "{\"project\":\"Alpha\",\"version\":\"" + repo.Version() + "\"," + fields[1..];
        body = body.Replace("\"O\"", "\"" + Sha(Encoding.UTF8.GetBytes(StoryTwo)) + "\"", StringComparison.Ordinal);
        using HttpClient client = repo.Factory.CreateTokenClient();
        IReadOnlyList<string> before = repo.State();

        using HttpResponseMessage response = await client.PostAsync(
            Route, new StringContent(body, Encoding.UTF8, "application/json"));

        await AssertError(response, HttpStatusCode.BadRequest, "invalid");
        Assert.Equal(before, repo.State());
        Assert.Equal(StoryTwo, File.ReadAllText(outside));
        Assert.Equal(count, TempFactoryRoot.Git(repo.Folder, "rev-list", "--count", "HEAD"));
    }

    [Fact]
    public async Task Title_OnASymlinkedStoryRetitlesOnlyTheBacklogLine()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using Repo repo = Repo.Create();
        string outside = LinkStoryTwoOutside(repo);

        await repo.Change(new { action = "title", line = 3, title = "Renamed two" });

        Assert.Equal(
            AlphaBacklog.Replace("Open two (after", "Renamed two (after", StringComparison.Ordinal),
            File.ReadAllText(repo.BacklogPath));
        Assert.Equal(StoryTwo, File.ReadAllText(outside));
        repo.AssertOneCommit("backlog: edit title of #2", ["backlog.md"]);
    }

    /// <summary>Replaces <c>backlog/002-open.md</c> with a symlink to <c>outside.md</c> in the temp root (holding
    /// the same text) and commits that as setup; returns the outside file's path.</summary>
    private static string LinkStoryTwoOutside(Repo repo)
    {
        string story = Path.Combine(repo.Folder, "backlog", "002-open.md");
        string outside = Path.Combine(repo.Root.Path, "outside.md");
        File.WriteAllText(outside, StoryTwo);
        File.Delete(story);
        File.CreateSymbolicLink(story, outside);
        repo.CommitAll("link");
        return outside;
    }
}
