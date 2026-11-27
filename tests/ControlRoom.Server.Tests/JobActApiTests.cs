using System.Net;
using System.Text;
using System.Text.Json;

namespace ControlRoom.Server.Tests;

/// <summary><c>POST /api/job/act</c> (<see cref="JobActEndpoint"/>): writes or deletes only the job's
/// <c>human.json</c>, never through a symlink, with fixed refusals. The time comes from the fake clock; symlink cases
/// use real symlinks in the temp root.</summary>
public sealed class JobActApiTests
{
    private const string Id = "12-escalated";

    private const string Requested = "2026-03-01T09:30:00Z";

    private const string ApproveFile = "{\"action\":\"approve\",\"round\":2,\"requested\":\"" + Requested + "\"}\n";

    private static string State(string stage, int round)
    {
        return JobSamples.State("Alpha", stage, round);
    }

    [Fact]
    public async Task Approve_WritesTheApproveFormByteForByteAndNothingElse()
    {
        using TempFactoryRoot root = new();
        string project = root.AddProject("Alpha", "- [ ] One\n");
        TempFactoryRoot.InitGit(project, "develop");
        string folder = root.AddJob(Id, State("needs-human", 2));
        string head = TempFactoryRoot.Git(project, "rev-parse", "HEAD");
        string status = TempFactoryRoot.Git(project, "status", "--porcelain");
        string file = Path.Combine(folder, "human.json");
        IReadOnlyList<string> before = WithoutJobFolder(root.Snapshot(), folder, file);
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();

        (HttpStatusCode code, string body, bool noStore) = await Post(client, "{\"id\":\"" + Id + "\",\"action\":\"approve\",\"round\":2}");

        Assert.Equal(HttpStatusCode.OK, code);
        Assert.True(noStore);
        Assert.Equal("{\"request\":{\"action\":\"approve\",\"round\":2,\"requested\":\"" + Requested + "\"}}", body);
        Assert.Equal(Encoding.UTF8.GetBytes(ApproveFile), File.ReadAllBytes(file));
        Assert.Equal(before, WithoutJobFolder(root.Snapshot(), folder, file));
        Assert.Equal(head, TempFactoryRoot.Git(project, "rev-parse", "HEAD"));
        Assert.Equal(status, TempFactoryRoot.Git(project, "status", "--porcelain"));
        Assert.Equal(["human.json", "state.json"], Directory.GetFiles(folder).Select(Path.GetFileName).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Approve_UsesTheFakeClockAndRoundZeroWhenTheStateHasNoRound()
    {
        using TempFactoryRoot root = new();
        string folder = root.AddJob(Id, "{\"project\":\"Alpha\",\"stage\":\"needs-human\"}");
        using ServerFactory factory = new(root.Path);
        factory.Time.Now = new DateTimeOffset(2026, 11, 19, 23, 5, 9, TimeSpan.FromHours(2));
        using HttpClient client = factory.CreateTokenClient();

        (HttpStatusCode code, _, _) = await Post(client, "{\"id\":\"" + Id + "\",\"action\":\"approve\",\"round\":0}");

        Assert.Equal(HttpStatusCode.OK, code);
        Assert.Equal("{\"action\":\"approve\",\"round\":0,\"requested\":\"2026-11-19T21:05:09Z\"}\n",
            File.ReadAllText(Path.Combine(folder, "human.json")));
    }

    [Fact]
    public async Task Rework_WritesTheReworkFormWithTheCleanedNote()
    {
        using TempFactoryRoot root = new();
        string folder = root.AddJob(Id, State("needs-human", 3));
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();

        (HttpStatusCode code, string body, _) = await Post(client,
            "{\"id\":\"" + Id + "\",\"action\":\"rework\",\"round\":3,\"note\":\"  Fix the\\n  \\\"label\\\"\\ttext  \"}");

        Assert.Equal(HttpStatusCode.OK, code);
        Assert.Equal(
            "{\"request\":{\"action\":\"rework\",\"round\":3,\"note\":\"Fix the \\\"label\\\" text\",\"requested\":\""
            + Requested + "\"}}", body);
        string written = File.ReadAllText(Path.Combine(folder, "human.json"));
        Assert.EndsWith("}\n", written, StringComparison.Ordinal);
        JobRequest? parsed = JobAction.Parse(File.ReadAllBytes(Path.Combine(folder, "human.json")));
        Assert.Equal(new JobRequest("rework", 3, "Fix the \"label\" text", Requested), parsed);
    }

    [Theory]
    [InlineData("\U0001F697", "\U0001F697")]
    [InlineData("\u00FC", "\u00FC")]
    [InlineData("<", "<")]
    [InlineData("\"", "\\\"")]
    public async Task ANoteOf1000NonAsciiOrHtmlRunes_IsWrittenWithin4KiBAndReadBack(string rune, string jsonRune)
    {
        using TempFactoryRoot root = new();
        string folder = root.AddJob(Id, State("needs-human", 2));
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();
        string note = string.Concat(Enumerable.Repeat(rune, 1000));

        (HttpStatusCode code, _, _) = await Post(client,
            "{\"id\":\"" + Id + "\",\"action\":\"rework\",\"round\":2,\"note\":\""
            + string.Concat(Enumerable.Repeat(jsonRune, 1000)) + "\"}");

        Assert.Equal(HttpStatusCode.OK, code);
        byte[] written = File.ReadAllBytes(Path.Combine(folder, "human.json"));
        Assert.True(written.Length <= JobAction.MaxFileBytes, "wrote " + written.Length + " bytes");
        Assert.Equal(new JobRequest("rework", 2, note, Requested), JobAction.Parse(written));
        string job = await client.GetStringAsync("/api/job?id=" + Id);
        Assert.Contains("\"request\":{\"action\":\"rework\",\"round\":2,\"note\":", job, StringComparison.Ordinal);
        Assert.DoesNotContain("unreadable", job, StringComparison.Ordinal);
    }

    public static TheoryData<string> InvalidBodies()
    {
        return
        [
            "{\"id\":\"" + Id + "\",\"action\":\"rework\",\"round\":2,\"note\":\"   \"}",
            "{\"id\":\"" + Id + "\",\"action\":\"rework\",\"round\":2}",
            "{\"id\":\"" + Id + "\",\"action\":\"rework\",\"round\":2,\"note\":\"" + new string('a', 1001) + "\"}",
            "{\"id\":\"" + Id + "\",\"action\":\"rework\",\"round\":2,\"note\":\"bad\\u0007bell\"}",
            "{\"id\":\"" + Id + "\",\"action\":\"rework\",\"round\":2,\"note\":\"Proposed by job 1-x\"}",
            "{\"id\":\"" + Id + "\",\"action\":\"rework\",\"round\":2,\"note\":1}",
            "{\"id\":\"" + Id + "\",\"action\":\"approve\",\"round\":2,\"note\":\"x\"}",
            "{\"id\":\"" + Id + "\",\"action\":\"merge\",\"round\":2}",
            "{\"id\":\"" + Id + "\",\"action\":\"approve\",\"round\":2,\"extra\":1}",
            "{\"id\":\"" + Id + "\",\"action\":\"approve\",\"round\":2,\"round\":2}",
            "{\"id\":\"" + Id + "\",\"action\":\"approve\",\"round\":\"2\"}",
            "{\"id\":\"" + Id + "\",\"action\":\"approve\",\"round\":-1}",
            "{\"id\":\"" + Id + "\",\"action\":\"approve\",\"round\":2.5}",
            "{\"id\":\"" + Id + "\",\"action\":\"approve\"}",
            "{\"action\":\"approve\",\"round\":2}",
            "{\"id\":12,\"action\":\"approve\",\"round\":2}",
            "[\"approve\"]",
            "not json",
            "",
            "{\"id\":\"" + Id + "\",\"action\":\"rework\",\"round\":2,\"note\":\"" + new string(' ', 9000) + "x\"}",
        ];
    }

    [Theory]
    [MemberData(nameof(InvalidBodies))]
    public async Task BadBodies_Return400InvalidAndWriteNothing(string json)
    {
        using TempFactoryRoot root = new();
        root.AddJob(Id, State("needs-human", 2));
        IReadOnlyList<string> before = root.Snapshot();
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();

        (HttpStatusCode code, string body, bool noStore) = await Post(client, json);

        Assert.Equal(HttpStatusCode.BadRequest, code);
        Assert.Equal("{\"error\":\"invalid\"}", body);
        Assert.True(noStore);
        Assert.Equal(before, root.Snapshot());
    }

    [Theory]
    [InlineData(999)]
    [InlineData(1000)]
    public async Task Rework_WritesEveryCleanedScalarAtTheBoundary(int length)
    {
        using TempFactoryRoot root = new();
        string folder = root.AddJob(Id, State("needs-human", 2));
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();
        string prefix = new('a', length - 2);
        string raw = "\t" + prefix + " \u0085 \U0001F680\n";
        string expected = prefix + " \U0001F680";

        (HttpStatusCode code, _, _) = await PostRework(client, raw);

        Assert.Equal(HttpStatusCode.OK, code);
        Assert.Equal(new JobRequest("rework", 2, expected, Requested),
            JobAction.Parse(File.ReadAllBytes(Path.Combine(folder, "human.json"))));
    }

    [Fact]
    public async Task Rework_Rejects1001CleanedScalarsAndLeavesNoPendingRequest()
    {
        using TempFactoryRoot root = new();
        string folder = root.AddJob(Id, State("needs-human", 2));
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();
        string raw = "\t" + new string('a', 998) + " \u0085 \U0001F680!\n";

        (HttpStatusCode code, string body, _) = await PostRework(client, raw);

        Assert.Equal(HttpStatusCode.BadRequest, code);
        Assert.Equal("{\"error\":\"invalid\"}", body);
        Assert.False(File.Exists(Path.Combine(folder, "human.json")));
        using JsonDocument job = JsonDocument.Parse(await client.GetStringAsync("/api/job?id=" + Id));
        Assert.Equal(JsonValueKind.Null, job.RootElement.GetProperty("request").ValueKind);
    }

    [Theory]
    [InlineData("13-other")]
    [InlineData("../12-escalated")]
    [InlineData("12-escalated/")]
    [InlineData("12-Escalated")]
    [InlineData("")]
    [InlineData("..")]
    public async Task UnknownOrMalformedIds_Return404AndWriteNothing(string id)
    {
        using TempFactoryRoot root = new();
        root.AddJob(Id, State("needs-human", 2));
        IReadOnlyList<string> before = root.Snapshot();
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();

        (HttpStatusCode code, string body, _) = await Post(client, "{\"id\":\"" + id + "\",\"action\":\"approve\",\"round\":2}");

        Assert.Equal(HttpStatusCode.NotFound, code);
        Assert.Equal("{\"error\":\"not found\"}", body);
        Assert.Equal(before, root.Snapshot());
    }

    [Fact]
    public async Task ASymlinkedJobFolder_Returns404AndWritesNothing()
    {
        using TempFactoryRoot root = new();
        string outside = Path.Combine(root.Path, ".outside", "job");
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "state.json"), State("needs-human", 2));
        Directory.CreateDirectory(root.JobsPath);
        Directory.CreateSymbolicLink(Path.Combine(root.JobsPath, Id), outside);
        IReadOnlyList<string> before = root.Snapshot();
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();

        (HttpStatusCode code, string body, _) = await Post(client, "{\"id\":\"" + Id + "\",\"action\":\"approve\",\"round\":2}");

        Assert.Equal(HttpStatusCode.NotFound, code);
        Assert.Equal("{\"error\":\"not found\"}", body);
        Assert.False(File.Exists(Path.Combine(outside, "human.json")));
        Assert.Equal(before, root.Snapshot());
    }

    [Theory]
    [InlineData("review", 2, "approve", 409, "not_waiting")]
    [InlineData("approve", 2, "rework", 409, "not_waiting")]
    [InlineData("done", 2, "cancel", 409, "not_waiting")]
    [InlineData("needs-human", 3, "approve", 409, "changed")]
    [InlineData("needs-human", 1, "rework", 409, "changed")]
    [InlineData("needs-human", 3, "cancel", 409, "changed")]
    public async Task AJobNotWaitingOrInAnotherRound_IsRefusedAndNothingIsWritten(
        string stage, int round, string action, int status, string error)
    {
        using TempFactoryRoot root = new();
        root.AddJob(Id, State(stage, round));
        IReadOnlyList<string> before = root.Snapshot();
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();
        string note = action == "rework" ? ",\"note\":\"Again\"" : "";

        (HttpStatusCode code, string body, _) = await Post(client,
            "{\"id\":\"" + Id + "\",\"action\":\"" + action + "\",\"round\":2" + note + "}");

        Assert.Equal(status, (int)code);
        Assert.Equal("{\"error\":\"" + error + "\"}", body);
        Assert.Equal(before, root.Snapshot());
    }

    [Theory]
    [InlineData("{\"action\":\"approve\",\"round\":2,\"requested\":\"2026-01-01T00:00:00Z\"}\n")]
    [InlineData("not a request")]
    public async Task ApproveOrReworkWhilePending_Return409PendingAndKeepTheFile(string existing)
    {
        using TempFactoryRoot root = new();
        string folder = root.AddJob(Id, State("needs-human", 2));
        string file = Path.Combine(folder, "human.json");
        File.WriteAllText(file, existing);
        IReadOnlyList<string> before = root.Snapshot();
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();

        (HttpStatusCode approve, string approveBody, _) = await Post(client, "{\"id\":\"" + Id + "\",\"action\":\"approve\",\"round\":2}");
        (HttpStatusCode rework, string reworkBody, _) = await Post(client,
            "{\"id\":\"" + Id + "\",\"action\":\"rework\",\"round\":2,\"note\":\"Again\"}");

        Assert.Equal(HttpStatusCode.Conflict, approve);
        Assert.Equal("{\"error\":\"pending\"}", approveBody);
        Assert.Equal(HttpStatusCode.Conflict, rework);
        Assert.Equal("{\"error\":\"pending\"}", reworkBody);
        Assert.Equal(existing, File.ReadAllText(file));
        Assert.Equal(before, root.Snapshot());
    }

    [Theory]
    [InlineData("{\"action\":\"approve\",\"round\":2,\"requested\":\"2026-01-01T00:00:00Z\"}\n")]
    [InlineData("not a request")]
    public async Task Cancel_DeletesAPendingFile(string existing)
    {
        using TempFactoryRoot root = new();
        string folder = root.AddJob(Id, State("needs-human", 2));
        string file = Path.Combine(folder, "human.json");
        File.WriteAllText(file, existing);
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();

        (HttpStatusCode code, string body, bool noStore) = await Post(client, "{\"id\":\"" + Id + "\",\"action\":\"cancel\",\"round\":2}");

        Assert.Equal(HttpStatusCode.OK, code);
        Assert.Equal("{\"request\":null}", body);
        Assert.True(noStore);
        Assert.False(File.Exists(file));
        Assert.True(File.Exists(Path.Combine(folder, "state.json")));
    }

    [Fact]
    public async Task Cancel_WithoutAFile_Returns409NotPending()
    {
        using TempFactoryRoot root = new();
        root.AddJob(Id, State("needs-human", 2));
        IReadOnlyList<string> before = root.Snapshot();
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();

        (HttpStatusCode code, string body, _) = await Post(client, "{\"id\":\"" + Id + "\",\"action\":\"cancel\",\"round\":2}");

        Assert.Equal(HttpStatusCode.Conflict, code);
        Assert.Equal("{\"error\":\"not_pending\"}", body);
        Assert.Equal(before, root.Snapshot());
    }

    [Fact]
    public async Task ApproveThenCancelThenRework_RoundTrips()
    {
        using TempFactoryRoot root = new();
        string folder = root.AddJob(Id, State("needs-human", 2));
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();

        (HttpStatusCode first, _, _) = await Post(client, "{\"id\":\"" + Id + "\",\"action\":\"approve\",\"round\":2}");
        (HttpStatusCode cancel, _, _) = await Post(client, "{\"id\":\"" + Id + "\",\"action\":\"cancel\",\"round\":2}");
        (HttpStatusCode rework, _, _) = await Post(client,
            "{\"id\":\"" + Id + "\",\"action\":\"rework\",\"round\":2,\"note\":\"Smaller\"}");

        Assert.Equal(HttpStatusCode.OK, first);
        Assert.Equal(HttpStatusCode.OK, cancel);
        Assert.Equal(HttpStatusCode.OK, rework);
        Assert.Equal("{\"action\":\"rework\",\"round\":2,\"note\":\"Smaller\",\"requested\":\"" + Requested + "\"}\n",
            File.ReadAllText(Path.Combine(folder, "human.json")));
    }

    [Theory]
    [InlineData("approve", true)]
    [InlineData("rework", true)]
    [InlineData("cancel", true)]
    [InlineData("approve", false)]
    [InlineData("rework", false)]
    [InlineData("cancel", false)]
    public async Task ASymlinkedRequestFile_Returns500AndLeavesLinkAndTarget(string action, bool targetExists)
    {
        using TempFactoryRoot root = new();
        string folder = root.AddJob(Id, State("needs-human", 2));
        string outside = Path.Combine(root.Path, ".outside");
        Directory.CreateDirectory(outside);
        string target = Path.Combine(outside, "target.json");
        if (targetExists)
        {
            File.WriteAllText(target, ApproveFile);
        }

        string link = Path.Combine(folder, "human.json");
        File.CreateSymbolicLink(link, target);
        IReadOnlyList<string> before = root.Snapshot();
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();
        string note = action == "rework" ? ",\"note\":\"Again\"" : "";

        (HttpStatusCode code, string body, _) = await Post(client,
            "{\"id\":\"" + Id + "\",\"action\":\"" + action + "\",\"round\":2" + note + "}");

        Assert.Equal(HttpStatusCode.InternalServerError, code);
        Assert.Equal("{\"error\":\"write_failed\"}", body);
        Assert.Equal(target, new FileInfo(link).LinkTarget);
        Assert.Equal(targetExists, File.Exists(target));
        if (targetExists)
        {
            Assert.Equal(ApproveFile, File.ReadAllText(target));
        }

        Assert.Equal(before, root.Snapshot());
    }

    [Fact]
    public async Task WithoutTheToken_Returns401AndWritesNothing()
    {
        using TempFactoryRoot root = new();
        root.AddJob(Id, State("needs-human", 2));
        IReadOnlyList<string> before = root.Snapshot();
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateClient();

        (HttpStatusCode code, string body, _) = await Post(client, "{\"id\":\"" + Id + "\",\"action\":\"approve\",\"round\":2}");

        Assert.Equal(HttpStatusCode.Unauthorized, code);
        Assert.Equal("{\"error\":\"unauthorized\"}", body);
        Assert.Equal(before, root.Snapshot());
    }

    [Fact]
    public async Task ABodyOver8KiB_Returns400()
    {
        using TempFactoryRoot root = new();
        root.AddJob(Id, State("needs-human", 2));
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();
        string json = "{\"id\":\"" + Id + "\",\"action\":\"approve\",\"round\":2}" + new string(' ', 8192);

        (HttpStatusCode code, string body, _) = await Post(client, json);

        Assert.Equal(HttpStatusCode.BadRequest, code);
        Assert.Equal("{\"error\":\"invalid\"}", body);
    }

    [Fact]
    public async Task ANonJsonContentType_IsRefusedAndWritesNothing()
    {
        using TempFactoryRoot root = new();
        root.AddJob(Id, State("needs-human", 2));
        IReadOnlyList<string> before = root.Snapshot();
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();

        using HttpResponseMessage response = await client.PostAsync(JobActEndpoint.Route,
            new StringContent("{\"id\":\"" + Id + "\",\"action\":\"approve\",\"round\":2}", Encoding.UTF8, "text/plain"));

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        Assert.Equal(before, root.Snapshot());
    }

    private static Task<(HttpStatusCode Code, string Body, bool NoStore)> PostRework(HttpClient client, string note)
    {
        return Post(client, JsonSerializer.Serialize(new { id = Id, action = "rework", round = 2, note }));
    }

    private static async Task<(HttpStatusCode Code, string Body, bool NoStore)> Post(HttpClient client, string json)
    {
        using HttpResponseMessage response = await client.PostAsync(
            JobActEndpoint.Route, new StringContent(json, Encoding.UTF8, "application/json"));
        return (response.StatusCode, await response.Content.ReadAsStringAsync(),
            response.Headers.CacheControl?.NoStore == true);
    }

    /// <summary>The snapshot without the job folder's own entry (its mtime changes when a file is added) and the
    /// request file.</summary>
    [Fact]
    public async Task Approve_UuidSuffixedIdWritesOnlyIntoItsOwnFolder()
    {
        using TempFactoryRoot root = new();
        string suffixed = "405-auto-pause." + JobSamples.Uuid;
        string legacy = root.AddJob("405-auto-pause", State("needs-human", 2));
        string folder = root.AddJob(suffixed, State("needs-human", 2));
        string file = Path.Combine(folder, "human.json");
        IReadOnlyList<string> before = WithoutJobFolder(root.Snapshot(), folder, file);
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();

        (HttpStatusCode code, _, _) = await Post(client, "{\"id\":\"" + suffixed + "\",\"action\":\"approve\",\"round\":2}");

        Assert.Equal(HttpStatusCode.OK, code);
        Assert.Equal(Encoding.UTF8.GetBytes(ApproveFile), File.ReadAllBytes(file));
        Assert.Equal(before, WithoutJobFolder(root.Snapshot(), folder, file));
        Assert.Equal(["human.json", "state.json"], Directory.GetFiles(folder).Select(Path.GetFileName).Order(StringComparer.Ordinal));
        Assert.Equal(["state.json"], Directory.GetFiles(legacy).Select(Path.GetFileName));
    }

    private static List<string> WithoutJobFolder(IReadOnlyList<string> snapshot, string folder, string file)
    {
        return snapshot
            .Where(entry => !entry.StartsWith(folder + "|", StringComparison.Ordinal)
                && !entry.StartsWith(file + "|", StringComparison.Ordinal))
            .ToList();
    }
}
