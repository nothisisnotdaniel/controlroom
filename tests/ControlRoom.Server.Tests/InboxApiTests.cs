using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ControlRoom.Server.Tests;

/// <summary><c>GET /api/inbox</c> (<see cref="InboxEndpoint"/>): the gates, the item shape, the proposing job from
/// the story's Notes, the order, unreadable backlogs, and approve and drop through <c>POST /api/backlog/change</c>
/// with the fields the inbox returned (against a real local git repo in a temp factory root).</summary>
public sealed class InboxApiTests
{
    private const string Route = "/api/inbox";

    private const string AlphaBacklog =
        "# Alpha\n"                                                   // 0
        + "- [ ] [#1](backlog/001-open.md) Open one\n"                // 1
        + "- [?] [#2](backlog/002-proposal.md) Proposal two → 7-x\n"  // 2
        + "- [h] [#3](backlog/003-hand.md) Hand three\n"              // 3
        + "- [x] [#4](backlog/004-done.md) Done four\n"               // 4
        + "- [?] Old proposal: no number\n";                          // 5

    private const string BetaBacklog =
        "# Beta\n"                                                    // 0
        + "- [?] [#7](backlog/007-beta.md) Beta seven\n"              // 1
        + "- [ ] Beta open: not listed\n";                            // 2

    private static string Notes(string id)
    {
        return "# Story\n\n## What\n\nDo it.\n\n## Notes\n\nProposed by job " + id + " (`factory/jobs/" + id + "/`).\n";
    }

    [Fact]
    public async Task Inbox_WithoutTheToken_Is401()
    {
        using TempFactoryRoot root = new();
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(Route);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("{\"error\":\"unauthorized\"}", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Inbox_WithAWrongToken_Is401()
    {
        using TempFactoryRoot root = new();
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateClient();
        using HttpRequestMessage request = new(HttpMethod.Get, Route);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", new string('A', 43));

        using HttpResponseMessage response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("{\"error\":\"unauthorized\"}", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Inbox_WithABadHost_Is421()
    {
        using TempFactoryRoot root = new();
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();
        using HttpRequestMessage request = new(HttpMethod.Get, Route);
        request.Headers.Host = "evil.example:8100";

        using HttpResponseMessage response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.MisdirectedRequest, response.StatusCode);
    }

    [Fact]
    public async Task Inbox_Post_Is405()
    {
        using TempFactoryRoot root = new();
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();

        using HttpResponseMessage response = await client.PostAsync(Route, new StringContent("{}"));

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    [Fact]
    public async Task Inbox_GetIs200WithNoStore_AndHeadIsAllowed()
    {
        using TempFactoryRoot root = new();
        root.AddProject("Alpha", AlphaBacklog);
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();

        using HttpResponseMessage response = await client.GetAsync(Route);
        using HttpResponseMessage head = await client.SendAsync(new HttpRequestMessage(HttpMethod.Head, Route));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal(HttpStatusCode.OK, head.StatusCode);
        Assert.True(head.Headers.CacheControl?.NoStore);
    }

    [Fact]
    public async Task Inbox_ListsOnlyProposalLinesWithTheExactShapeAndTheBacklogsVersion()
    {
        using TempFactoryRoot root = new();
        string alpha = root.AddProject("Alpha", AlphaBacklog);
        TempFactoryRoot.AddStory(alpha, "002-proposal.md", Notes("12-first"));
        root.AddProject("Beta", BetaBacklog);
        root.AddProject("Gamma", "- [ ] Nothing waiting\n- [h] Hand: task\n");
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();

        using JsonDocument inbox = JsonDocument.Parse(await client.GetStringAsync(Route));
        using JsonDocument backlogs = JsonDocument.Parse(await client.GetStringAsync("/api/backlogs"));

        Assert.Equal(["items", "unreadable"], inbox.RootElement.EnumerateObject().Select(p => p.Name).ToArray());
        Assert.Empty(inbox.RootElement.GetProperty("unreadable").EnumerateArray());
        JsonElement[] items = inbox.RootElement.GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(3, items.Length);
        foreach (JsonElement item in items)
        {
            Assert.Equal(
                ["project", "version", "line", "number", "title", "rest", "job"],
                item.EnumerateObject().Select(p => p.Name).ToArray());
        }

        Dictionary<string, string> versions = backlogs.RootElement.GetProperty("projects").EnumerateArray()
            .ToDictionary(p => p.GetProperty("name").GetString()!, p => p.GetProperty("version").GetString()!);

        JsonElement first = items[0];
        Assert.Equal("Alpha", first.GetProperty("project").GetString());
        Assert.Equal(versions["Alpha"], first.GetProperty("version").GetString());
        Assert.Equal(Sha(File.ReadAllBytes(Path.Combine(alpha, "backlog.md"))), first.GetProperty("version").GetString());
        Assert.Equal(2, first.GetProperty("line").GetInt32());
        Assert.Equal(2, first.GetProperty("number").GetInt32());
        Assert.Equal("Proposal two", first.GetProperty("title").GetString());
        Assert.Equal("→ 7-x", first.GetProperty("rest").GetString());
        Assert.Equal("12-first", first.GetProperty("job").GetString());

        // No job: project order, then line.
        JsonElement old = items[1];
        Assert.Equal("Alpha", old.GetProperty("project").GetString());
        Assert.Equal(5, old.GetProperty("line").GetInt32());
        Assert.Equal(JsonValueKind.Null, old.GetProperty("number").ValueKind);
        Assert.Equal("Old proposal", old.GetProperty("title").GetString());
        Assert.Equal("no number", old.GetProperty("rest").GetString());
        Assert.Equal(JsonValueKind.Null, old.GetProperty("job").ValueKind);

        JsonElement beta = items[2];
        Assert.Equal("Beta", beta.GetProperty("project").GetString());
        Assert.Equal(versions["Beta"], beta.GetProperty("version").GetString());
        Assert.Equal(1, beta.GetProperty("line").GetInt32());
        Assert.Equal(7, beta.GetProperty("number").GetInt32());
        Assert.Equal(JsonValueKind.Null, beta.GetProperty("job").ValueKind);
    }

    public static TheoryData<string, string?> Stories()
    {
        return new TheoryData<string, string?>
        {
            { Notes("123-scriptable-cli"), "123-scriptable-cli" },
            { "# S\n\n## Notes\n\nProposed by job 9-a.\n", "9-a" },
            { "# S\n\n## Notes\r\n\r\nSee below.\r\nProposed by job 45-x-y\r\n", "45-x-y" },
            { "# S\n\n## Notes\n\nProposed by job 1-first. Proposed by job 2-second.\n", "1-first" },
            { "# S\n\n## Notes\n\nProposed by job 1-" + new string('a', 78) + "\n", "1-" + new string('a', 78) },
            { "# S\n\n## Notes\n\nProposed by job 1-" + new string('a', 79) + "\n", null },
            { "# S\n\n## Why\n\nProposed by job 12-outside\n", null },
            { "# S\n\nProposed by job 12-outside\n\n## Notes\n\nNothing here.\n", null },
            { "# S\n\n## Notes\n\nNone.\n\n## Later\n\nProposed by job 12-after\n", null },
            { "# S\n\n## What\n\nNo notes at all.\n", null },
            { "# S\n\n## Notes\n\nProposed by job 12-Upper\n", null },
            { "# S\n\n## Notes\n\nProposed by job 1234567-long\n", null },
            { "# S\n\n## Notes\n\nProposed by job x-12\n", null },
            { "# S\n\n## Notes\n\nProposed by job 12-a--b\n", null },
            { "# S\n\n## Notes\n\nProposed by job ../../etc\n", null },
            { "# S\n\n## Notes\n\nProposed by job 12-<b>x</b>\n", null },
            { "# S\n\n### Notes\n\nProposed by job 12-deep\n", null },
            { Notes("405-auto-pause." + JobSamples.Uuid), "405-auto-pause." + JobSamples.Uuid },
            { "# S\n\n## Notes\n\nProposed by job 405-a." + JobSamples.Uuid + ".\n", "405-a." + JobSamples.Uuid },
            { "# S\n\n## Notes\n\nProposed by job 1-" + new string('a', 78) + "." + JobSamples.Uuid + "\n",
                "1-" + new string('a', 78) + "." + JobSamples.Uuid },
            { "# S\n\n## Notes\n\nProposed by job 1-" + new string('a', 79) + "." + JobSamples.Uuid + "\n", null },
            { "# S\n\n## Notes\n\nProposed by job 405-a." + JobSamples.Uuid.ToUpperInvariant() + "\n", null },
            { "# S\n\n## Notes\n\nProposed by job 405-a." + JobSamples.Uuid[..31] + "\n", null },
            { "# S\n\n## Notes\n\nProposed by job 405-a." + JobSamples.Uuid + "0\n", null },
            { "# S\n\n## Notes\n\nProposed by job 405-a.." + JobSamples.Uuid + "\n", null },
        };
    }

    [Theory]
    [MemberData(nameof(Stories))]
    public async Task Inbox_JobComesOnlyFromAValidIdInTheNotesSection(string story, string? expected)
    {
        using TempFactoryRoot root = new();
        string alpha = root.AddProject("Alpha", "- [?] [#2](backlog/002-proposal.md) Proposal two\n");
        TempFactoryRoot.AddStory(alpha, "002-proposal.md", story);
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();

        JsonElement item = await SingleItem(client);

        Assert.Equal(expected, item.GetProperty("job").GetString());
        Assert.Equal(2, item.GetProperty("number").GetInt32());
    }

    [Fact]
    public async Task Inbox_ItemWithoutAStoryFileHasNoJob()
    {
        using TempFactoryRoot root = new();
        root.AddProject("Alpha", "- [?] [#2](backlog/002-proposal.md) Proposal two\n");
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();

        JsonElement item = await SingleItem(client);

        Assert.Equal(JsonValueKind.Null, item.GetProperty("job").ValueKind);
    }

    [Fact]
    public async Task Inbox_ItemWithALinkTargetOutsideTheBacklogFolderHasNoJob()
    {
        using TempFactoryRoot root = new();
        string alpha = root.AddProject("Alpha", "- [?] [#2](../002-proposal.md) Proposal two\n");
        File.WriteAllText(Path.Combine(root.Path, "002-proposal.md"), Notes("12-outside"));
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();

        JsonElement item = await SingleItem(client);

        Assert.Equal(JsonValueKind.Null, item.GetProperty("job").ValueKind);
        Assert.True(Directory.Exists(alpha));
    }

    [Fact]
    public async Task Inbox_SymlinkedStoryFileIsRefusedAndTheItemIsStillListed()
    {
        using TempFactoryRoot root = new();
        string alpha = root.AddProject("Alpha", "- [?] [#2](backlog/002-proposal.md) Proposal two\n");
        string outside = Path.Combine(root.Path, ".outside");
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "story.md"), Notes("12-linked"));
        Directory.CreateDirectory(Path.Combine(alpha, "backlog"));
        File.CreateSymbolicLink(Path.Combine(alpha, "backlog", "002-proposal.md"), Path.Combine(outside, "story.md"));
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();

        JsonElement item = await SingleItem(client);

        Assert.Equal("Proposal two", item.GetProperty("title").GetString());
        Assert.Equal(JsonValueKind.Null, item.GetProperty("job").ValueKind);
    }

    [Fact]
    public async Task Inbox_SymlinkedBacklogFolderIsRefusedAndTheItemIsStillListed()
    {
        using TempFactoryRoot root = new();
        string alpha = root.AddProject("Alpha", "- [?] [#2](backlog/002-proposal.md) Proposal two\n");
        string outside = Path.Combine(root.Path, ".outside");
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "002-proposal.md"), Notes("12-linked"));
        Directory.CreateSymbolicLink(Path.Combine(alpha, "backlog"), outside);
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();

        JsonElement item = await SingleItem(client);

        Assert.Equal(JsonValueKind.Null, item.GetProperty("job").ValueKind);
    }

    [Fact]
    public async Task Inbox_UnreadableStoryFileHasNoJob()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using TempFactoryRoot root = new();
        string alpha = root.AddProject("Alpha", "- [?] [#2](backlog/002-proposal.md) Proposal two\n");
        string story = TempFactoryRoot.AddStory(alpha, "002-proposal.md", Notes("12-locked"));
        root.Lock(story);
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();

        JsonElement item = await SingleItem(client);

        Assert.Equal(JsonValueKind.Null, item.GetProperty("job").ValueKind);
    }

    [Fact]
    public async Task Inbox_SortsByJobNumberNumerically_ThenNoJob_TiesByProjectThenLine()
    {
        using TempFactoryRoot root = new();
        string alpha = root.AddProject(
            "Alpha",
            "- [?] [#1](backlog/001-a.md) A one\n"
            + "- [?] [#2](backlog/002-a.md) A two\n"
            + "- [?] Plain alpha: no story\n");
        TempFactoryRoot.AddStory(alpha, "001-a.md", Notes("250-c"));
        TempFactoryRoot.AddStory(alpha, "002-a.md", Notes("10-b"));
        string beta = root.AddProject(
            "Beta",
            "- [?] Plain beta: no story\n"
            + "- [?] [#5](backlog/005-b.md) B five\n"
            + "- [?] [#6](backlog/006-b.md) B six\n");
        TempFactoryRoot.AddStory(beta, "005-b.md", Notes("9-a"));
        TempFactoryRoot.AddStory(beta, "006-b.md", Notes("10-b"));
        string capital = root.AddProject("Zeta", "- [?] [#3](backlog/003-z.md) Z three\n");
        TempFactoryRoot.AddStory(capital, "003-z.md", Notes("10-b"));
        string lower = root.AddProject("alpha2", "- [?] [#4](backlog/004-l.md) L four\n");
        TempFactoryRoot.AddStory(lower, "004-l.md", Notes("10-b"));
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();

        using JsonDocument inbox = JsonDocument.Parse(await client.GetStringAsync(Route));

        string[] order = inbox.RootElement.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("project").GetString() + ":" + item.GetProperty("line").GetInt32() + ":"
                + (item.GetProperty("job").GetString() ?? "-"))
            .ToArray();
        Assert.Equal(
            [
                "Beta:1:9-a",
                "Alpha:1:10-b",
                "Beta:2:10-b",
                "Zeta:0:10-b",
                "alpha2:0:10-b",
                "Alpha:0:250-c",
                "Alpha:2:-",
                "Beta:0:-",
            ],
            order);
    }

    [Fact]
    public async Task Inbox_UnreadableBacklogsAreNamedAndOtherProjectsStillListed()
    {
        using TempFactoryRoot root = new();
        root.AddProject("Alpha", "- [?] Alpha proposal: yes\n");
        string outside = Path.Combine(root.Path, ".outside");
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "backlog.md"), "- [?] Secret: outside\n");
        string linked = root.AddProject("Linked", null);
        File.CreateSymbolicLink(Path.Combine(linked, "backlog.md"), Path.Combine(outside, "backlog.md"));
        string folder = root.AddProject("Folder", null);
        Directory.CreateDirectory(Path.Combine(folder, "backlog.md"));
        root.AddProject("Missing", null);
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();

        using JsonDocument inbox = JsonDocument.Parse(await client.GetStringAsync(Route));

        string[] unreadable = inbox.RootElement.GetProperty("unreadable").EnumerateArray()
            .Select(name => name.GetString()!)
            .ToArray();
        Assert.Equal(["Folder", "Linked", "Missing"], unreadable.Order(StringComparer.Ordinal).ToArray());
        JsonElement item = Assert.Single(inbox.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal("Alpha", item.GetProperty("project").GetString());
        Assert.DoesNotContain("Secret", inbox.RootElement.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Approve_WithTheInboxFields_ChangesTheMarkInOneCommitAndLeavesTheInbox()
    {
        using Repo repo = Repo.Create();
        using HttpClient client = repo.Factory.CreateTokenClient();
        JsonElement item = await FindItem(client, 2);
        string head = repo.Head();

        using HttpResponseMessage response = await Send(client, item, "approve");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            AlphaBacklog.Replace("- [?] [#2]", "- [ ] [#2]", StringComparison.Ordinal),
            File.ReadAllText(repo.BacklogPath));
        Assert.True(File.Exists(repo.StoryPath));
        Assert.Equal(head, TempFactoryRoot.Git(repo.Folder, "rev-parse", "HEAD~1"));
        Assert.Equal("develop", TempFactoryRoot.Git(repo.Folder, "branch", "--show-current"));
        Assert.Null(await FindItemOrNull(client, 2));
    }

    [Fact]
    public async Task Drop_WithTheInboxFields_RemovesTheLineAndStoryInOneCommitAndLeavesTheInbox()
    {
        using Repo repo = Repo.Create();
        using HttpClient client = repo.Factory.CreateTokenClient();
        JsonElement item = await FindItem(client, 2);
        string head = repo.Head();

        using HttpResponseMessage response = await Send(client, item, "drop");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            AlphaBacklog.Replace("- [?] [#2](backlog/002-proposal.md) Proposal two → 7-x\n", "", StringComparison.Ordinal),
            File.ReadAllText(repo.BacklogPath));
        Assert.False(File.Exists(repo.StoryPath));
        Assert.Equal(head, TempFactoryRoot.Git(repo.Folder, "rev-parse", "HEAD~1"));
        Assert.Equal("", TempFactoryRoot.Git(repo.Folder, "status", "--porcelain"));
        Assert.Null(await FindItemOrNull(client, 2));
    }

    [Theory]
    [InlineData("approve")]
    [InlineData("drop")]
    public async Task Change_AfterTheBacklogChangedSinceTheInboxWasRead_Is409AndTouchesNothing(string action)
    {
        using Repo repo = Repo.Create();
        using HttpClient client = repo.Factory.CreateTokenClient();
        JsonElement item = await FindItem(client, 2);
        string changed = AlphaBacklog + "- [ ] Added meanwhile: line\n";
        File.WriteAllText(repo.BacklogPath, changed);
        TempFactoryRoot.Git(repo.Folder, "commit", "-q", "-am", "meanwhile");
        string head = repo.Head();
        byte[] story = File.ReadAllBytes(repo.StoryPath);

        using HttpResponseMessage response = await Send(client, item, action);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("changed", body.RootElement.GetProperty("error").GetString());
        Assert.Equal(changed, File.ReadAllText(repo.BacklogPath));
        Assert.Equal(story, File.ReadAllBytes(repo.StoryPath));
        Assert.Equal(head, repo.Head());
    }

    private static async Task<HttpResponseMessage> Send(HttpClient client, JsonElement item, string action)
    {
        string body = JsonSerializer.Serialize(new
        {
            project = item.GetProperty("project").GetString(),
            version = item.GetProperty("version").GetString(),
            line = item.GetProperty("line").GetInt32(),
            action,
        });
        using StringContent content = new(body, Encoding.UTF8, "application/json");
        return await client.PostAsync("/api/backlog/change", content);
    }

    private static async Task<JsonElement> SingleItem(HttpClient client)
    {
        using JsonDocument inbox = JsonDocument.Parse(await client.GetStringAsync(Route));
        return Assert.Single(inbox.RootElement.GetProperty("items").EnumerateArray()).Clone();
    }

    private static async Task<JsonElement> FindItem(HttpClient client, int number)
    {
        JsonElement? item = await FindItemOrNull(client, number);
        Assert.NotNull(item);
        return item.Value;
    }

    private static async Task<JsonElement?> FindItemOrNull(HttpClient client, int number)
    {
        using JsonDocument inbox = JsonDocument.Parse(await client.GetStringAsync(Route));
        foreach (JsonElement item in inbox.RootElement.GetProperty("items").EnumerateArray())
        {
            if (item.GetProperty("number").ValueKind == JsonValueKind.Number
                && item.GetProperty("number").GetInt32() == number)
            {
                return item.Clone();
            }
        }

        return null;
    }

    private static string Sha(byte[] bytes)
    {
        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }

    private sealed class Repo : IDisposable
    {
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

        public string StoryPath => Path.Combine(Folder, "backlog", "002-proposal.md");

        public static Repo Create()
        {
            TempFactoryRoot root = new();
            string folder = root.AddProject("Alpha", AlphaBacklog);
            TempFactoryRoot.AddStory(folder, "001-open.md", "# 001 Open one\n");
            TempFactoryRoot.AddStory(folder, "002-proposal.md", Notes("12-first"));
            TempFactoryRoot.InitGit(folder, "develop");
            return new Repo(root, folder);
        }

        public string Head()
        {
            return TempFactoryRoot.Git(Folder, "rev-parse", "HEAD");
        }

        public void Dispose()
        {
            Factory.Dispose();
            Root.Dispose();
        }
    }
}
