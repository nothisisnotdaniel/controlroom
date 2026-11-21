using System.Net;
using System.Text.Json;

namespace ControlRoom.Server.Tests;

public sealed class StoryApiTests
{
    private const string NotFound = "{\"error\":\"not found\"}";

    private const string AlphaBacklog =
        "- [ ] [#4](backlog/004-edit-backlogs.md) Edit backlogs (after: #3)\n"
        + "- [x] [#3](backlog/003-done.md) Done three → 1-x\n"
        + "- [ ] [#6](backlog/../006-up.md) Dot dot\n"
        + "- [ ] [#7](backlog/sub/007-sub.md) Sub folder\n"
        + "- [ ] [#8](/backlog/008-root.md) Leading slash\n"
        + "- [ ] [#9](backlog\\009-back.md) Backslash\n"
        + "- [ ] [#10](backlog/%2e%2e/010-enc.md) Encoded dots\n"
        + "- [ ] [#11](backlog/012-other.md) Other number\n"
        + "- [ ] [#12](backlog/012-missing.md) Missing file\n"
        + "- [ ] [#13](backlog/013-link.md) Symlinked file\n"
        + "- [ ] [#15](backlog/015-big.md) Oversized\n"
        + "- [ ] [#16](backlog/016-x.txt) Not markdown\n"
        + "- [ ] [#17](backlog/17-short.md) Short number\n"
        + "- [ ] [#18](backlog/018-a_b.md) Underscore\n"
        + "- [ ] [#19](CLAUDE.md) Other file\n"
        + "- [ ] Old line: no number\n";

    [Fact]
    public async Task Story_ReturnsTitleAndBlocks()
    {
        using TempFactoryRoot root = Sample();
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();

        using HttpResponseMessage response = await client.GetAsync("/api/story?project=Alpha&number=4");
        string body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.True(response.Headers.CacheControl?.NoStore);
        using JsonDocument json = JsonDocument.Parse(body);
        Assert.Equal(
            ["project", "number", "title", "blocks", "markdown", "storyVersion", "category"],
            json.RootElement.EnumerateObject().Select(property => property.Name));
        Assert.Equal("Alpha", json.RootElement.GetProperty("project").GetString());
        Assert.Equal(4, json.RootElement.GetProperty("number").GetInt32());
        Assert.Equal("Edit backlogs", json.RootElement.GetProperty("title").GetString());
        JsonElement[] blocks = json.RootElement.GetProperty("blocks").EnumerateArray().ToArray();
        Assert.Equal(["heading", "paragraph"], blocks.Select(block => block.GetProperty("kind").GetString()));
        Assert.Equal("Story four", blocks[0].GetProperty("inlines")[0].GetProperty("text").GetString());
        Assert.DoesNotContain(root.Path, body, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/api/story?project=Alpha&number=3")]
    [InlineData("/api/story?number=4&project=Alpha&path=..%2F..%2FCLAUDE.md")]
    public async Task Story_ReadsOnlyProjectAndNumber(string url)
    {
        using TempFactoryRoot root = Sample();
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();

        using HttpResponseMessage response = await client.GetAsync(url);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("root file", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Story_HeadIsServedAndNothingIsWritten()
    {
        using TempFactoryRoot root = Sample();
        IReadOnlyList<string> before = root.Snapshot();
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();

        using HttpRequestMessage request = new(HttpMethod.Head, "/api/story?project=Alpha&number=4");
        using HttpResponseMessage response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(before, root.Snapshot());
    }

    [Fact]
    public async Task Story_FileExactlyAtTheLimitIsRead()
    {
        using TempFactoryRoot root = new();
        string alpha = root.AddProject("Alpha", "- [ ] [#5](backlog/005-edge.md) Edge\n");
        string head = "Edge text\n";
        TempFactoryRoot.AddStory(alpha, "005-edge.md", head + new string('a', (int)StoryReader.MaxBytes - head.Length));
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();

        using HttpResponseMessage response = await client.GetAsync("/api/story?project=Alpha&number=5");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("project=alpha&number=4")]
    [InlineData("project=ALPHA&number=4")]
    [InlineData("project=Alpha%20&number=4")]
    [InlineData("project=Nope&number=4")]
    [InlineData("project=factory&number=4")]
    [InlineData("project=.hidden&number=4")]
    [InlineData("project=..&number=4")]
    [InlineData("project=Alpha%2F..&number=4")]
    [InlineData("project=&number=4")]
    [InlineData("number=4")]
    [InlineData("project=Alpha")]
    [InlineData("project=Alpha&number=")]
    [InlineData("project=Alpha&number=abc")]
    [InlineData("project=Alpha&number=-4")]
    [InlineData("project=Alpha&number=%2B4")]
    [InlineData("project=Alpha&number=4%20")]
    [InlineData("project=Alpha&number=4.0")]
    [InlineData("project=Alpha&number=99999999999")]
    [InlineData("project=Alpha&number=99")]
    [InlineData("project=Alpha&number=4&number=4")]
    [InlineData("project=Alpha&project=Alpha&number=4")]
    [InlineData("project=Alpha&number=6")]
    [InlineData("project=Alpha&number=7")]
    [InlineData("project=Alpha&number=8")]
    [InlineData("project=Alpha&number=9")]
    [InlineData("project=Alpha&number=10")]
    [InlineData("project=Alpha&number=11")]
    [InlineData("project=Alpha&number=12")]
    [InlineData("project=Alpha&number=13")]
    [InlineData("project=Alpha&number=15")]
    [InlineData("project=Alpha&number=16")]
    [InlineData("project=Alpha&number=17")]
    [InlineData("project=Alpha&number=18")]
    [InlineData("project=Alpha&number=19")]
    [InlineData("project=LinkedFolder&number=1")]
    [InlineData("project=FileFolder&number=1")]
    [InlineData("project=NoBacklog&number=1")]
    public async Task Story_RefusalsGiveTheFixedNotFound(string query)
    {
        using TempFactoryRoot root = Sample();
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();

        await AssertNotFound(client, "/api/story?" + query, root.Path);
    }

    [Fact]
    public async Task Story_UnreadableFileGivesTheFixedNotFound()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using TempFactoryRoot root = new();
        string alpha = root.AddProject("Alpha", "- [ ] [#5](backlog/005-locked.md) Locked\n");
        root.Lock(TempFactoryRoot.AddStory(alpha, "005-locked.md", "secret\n"));
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();

        await AssertNotFound(client, "/api/story?project=Alpha&number=5", root.Path);
    }

    [Fact]
    public async Task Story_FifoIsRefusedWithoutHanging()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using TempFactoryRoot root = new();
        string alpha = root.AddProject("Alpha", "- [ ] [#5](backlog/005-fifo.md) Fifo\n");
        Directory.CreateDirectory(Path.Combine(alpha, "backlog"));
        TempFactoryRoot.MakeFifo(Path.Combine(alpha, "backlog", "005-fifo.md"));
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();

        using HttpResponseMessage response = await client.GetAsync("/api/story?project=Alpha&number=5")
            .WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(NotFound, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Story_DeeplyNestedLinksDoNotCrashTheServer()
    {
        using TempFactoryRoot root = new();
        string alpha = root.AddProject("Alpha", "- [ ] [#5](backlog/005-deep.md) Deep\n");
        int depth = ((int)StoryReader.MaxBytes - 1) / 5;
        TempFactoryRoot.AddStory(
            alpha, "005-deep.md", new string('[', depth) + "a" + string.Concat(Enumerable.Repeat("](x)", depth)));
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();

        using HttpResponseMessage story = await client.GetAsync("/api/story?project=Alpha&number=5")
            .WaitAsync(TimeSpan.FromSeconds(20));
        using HttpResponseMessage after = await client.GetAsync("/api/backlogs").WaitAsync(TimeSpan.FromSeconds(20));

        Assert.Equal(HttpStatusCode.OK, story.StatusCode);
        Assert.Equal(HttpStatusCode.OK, after.StatusCode);
        using JsonDocument json = JsonDocument.Parse(await story.Content.ReadAsStringAsync());
        JsonElement[] inlines = json.RootElement.GetProperty("blocks")[0].GetProperty("inlines").EnumerateArray()
            .ToArray();
        Assert.All(inlines, inline => Assert.Equal("text", inline.GetProperty("kind").GetString()));
        Assert.Contains("a", string.Concat(inlines.Select(inline => inline.GetProperty("text").GetString())),
            StringComparison.Ordinal);
    }

    private static async Task AssertNotFound(HttpClient client, string url, string rootPath)
    {
        using HttpResponseMessage response = await client.GetAsync(url);
        string body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(NotFound, body);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.DoesNotContain(rootPath, body, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", body, StringComparison.Ordinal);
    }

    /// <summary>Project <c>Alpha</c> with one good story and a file behind every bad target, so each refusal comes
    /// from the check, not from a missing file; plus projects whose <c>backlog</c> folder is a symlink or a file,
    /// and <c>factory</c> / <c>.hidden</c> folders that look like projects.</summary>
    private static TempFactoryRoot Sample()
    {
        TempFactoryRoot root = new();
        File.WriteAllText(Path.Combine(root.Path, "CLAUDE.md"), "# root file\n");
        string alpha = root.AddProject("Alpha", AlphaBacklog);
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

        string linked = root.AddProject("LinkedFolder", "- [ ] [#1](backlog/001-a.md) Linked\n");
        string inside = Path.Combine(linked, "real");
        Directory.CreateDirectory(inside);
        File.WriteAllText(Path.Combine(inside, "001-a.md"), "secret linked\n");
        Directory.CreateSymbolicLink(Path.Combine(linked, "backlog"), inside);

        string fileFolder = root.AddProject("FileFolder", "- [ ] [#1](backlog/001-a.md) File\n");
        File.WriteAllText(Path.Combine(fileFolder, "backlog"), "secret file\n");

        root.AddProject("NoBacklog", null);

        foreach (string name in new[] { "factory", ".hidden" })
        {
            string folder = root.AddProject(name, "- [ ] [#4](backlog/004-edit-backlogs.md) Hidden\n");
            TempFactoryRoot.AddStory(folder, "004-edit-backlogs.md", "secret hidden\n");
        }

        return root;
    }
}
