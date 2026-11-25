using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;

namespace ControlRoom.Server.Tests;

/// <summary>Other machines' item claims in <c>GET /api/backlogs</c> and the <c>claims</c> fingerprint in
/// <c>GET /api/changes</c>. Every claim a test expects is in its local bare origin before the client is created; then
/// the test moves <see cref="ServerFactory.Time"/> 60 s and awaits <see cref="ClaimWatcher.Scan"/> (scans run one at a
/// time, so the host's start scan is over and this one reads origin again). No test waits on a delay.</summary>
public sealed class ClaimsApiTests
{
    private const string Backlog =
        "# Alpha\n"
        + "- [ ] [#3](backlog/003-a.md) Three\n"
        + "- [?] [#4](backlog/004-b.md) Four\n"
        + "- [ ] [#5](backlog/005-c.md) Five\n"
        + "- [x] [#6](backlog/006-d.md) Six\n";

    [Fact]
    public async Task Backlogs_ListTheClaimsOfNumberedItemsLowestFirst()
    {
        using TempFactoryRoot root = new();
        (_, string remote) = ClaimFixture.Project(root, "Alpha", Backlog);
        ClaimFixture.Push(remote, "5", ClaimFixture.Message("5-five", "studio", "Alpha", "2026-03-01T08:05:00Z"));
        ClaimFixture.Push(remote, "3", ClaimFixture.Message("3-three", "mac-mini", "Alpha", "2026-03-01T08:00:00Z"));
        using ServerFactory factory = new(root.Path);

        JsonElement alpha = Project(await Scanned(factory), "Alpha");

        JsonElement[] claims = [.. alpha.GetProperty("claims").EnumerateArray()];
        Assert.Equal([3, 5], claims.Select(claim => claim.GetProperty("item").GetInt32()));
        Assert.Equal(
            "{\"item\":3,\"job\":\"3-three\",\"machine\":\"mac-mini\",\"claimed\":\"2026-03-01T08:00:00Z\"}",
            claims[0].GetRawText());
        Assert.Equal("studio", claims[1].GetProperty("machine").GetString());
        Assert.False(alpha.GetProperty("claimsStale").GetBoolean());
        Assert.Equal(factory.Time.Now, alpha.GetProperty("claimsChecked").GetDateTimeOffset());
    }

    [Fact]
    public async Task Backlogs_CleanOrDropBadFieldsAndKeepALargeClaimWithUnknownFields()
    {
        using TempFactoryRoot root = new();
        (_, string remote) = ClaimFixture.Project(root, "Alpha", Backlog);
        ClaimFixture.Push(remote, "3", "claim x\n\nJob: ../../etc\nMachine: ‮\nClaimed: yesterday\n");
        ClaimFixture.Push(
            remote, "4", "claim x\n\nJob: 4-four\nMachine: " + new string('m', 300) + "​\nClaimed: 2026-03-01T08:00:00+01:00\n");
        ClaimFixture.Push(
            remote, "5", ClaimFixture.Message("5-five", "mini", "Alpha", "2026-03-01T08:00:00Z") + new string('x', 5000));
        using ServerFactory factory = new(root.Path);

        JsonElement[] claims = [.. Project(await Scanned(factory), "Alpha").GetProperty("claims").EnumerateArray()];

        Assert.Equal("{\"item\":3,\"job\":null,\"machine\":null,\"claimed\":null}", claims[0].GetRawText());
        Assert.Equal("4-four", claims[1].GetProperty("job").GetString());
        Assert.Equal(new string('m', 120), claims[1].GetProperty("machine").GetString());
        Assert.Equal(JsonValueKind.Null, claims[1].GetProperty("claimed").ValueKind);
        Assert.Equal("{\"item\":5,\"job\":null,\"machine\":null,\"claimed\":null}", claims[2].GetRawText());
    }

    [Fact]
    public async Task Backlogs_LeaveOutOddRefsItemsWithoutALineOwnJobsAndOtherProjectsClaims()
    {
        using TempFactoryRoot root = new();
        (_, string remote) = ClaimFixture.Project(root, "Alpha", Backlog);
        ClaimFixture.Project(root, "Beta", "- [ ] [#3](backlog/003-a.md) Beta three\n- [ ] [#6](backlog/006-x.md) Six\n");
        string message = ClaimFixture.Message("9-other", "mini", "Alpha", "2026-03-01T08:00:00Z");
        ClaimFixture.Push(remote, "abc", message);
        ClaimFixture.Push(remote, "1/x", message);
        ClaimFixture.Push(remote, "7", message);
        ClaimFixture.Push(remote, "3", message);
        ClaimFixture.Push(remote, "6", ClaimFixture.Message("6-mine", "here", "Alpha", "2026-03-01T08:00:00Z"));
        root.AddJob("6-mine", "{\"stage\":\"build\",\"project\":\"Alpha\"}");
        using ServerFactory factory = new(root.Path);

        JsonDocument backlogs = await Scanned(factory);

        Assert.Equal(
            [3], Project(backlogs, "Alpha").GetProperty("claims").EnumerateArray().Select(claim => claim.GetProperty("item").GetInt32()));
        JsonElement beta = Project(backlogs, "Beta");
        Assert.Empty(beta.GetProperty("claims").EnumerateArray());
        Assert.Equal(factory.Time.Now, beta.GetProperty("claimsChecked").GetDateTimeOffset());
    }

    [Fact]
    public async Task Backlogs_WithoutOriginHaveNoClaimsAndTheEndpointNeverReadsOrigin()
    {
        using TempFactoryRoot root = new();
        string plain = root.AddProject("Plain", Backlog);
        TempFactoryRoot.InitGit(plain, "develop");
        (_, string remote) = ClaimFixture.Project(root, "Alpha", Backlog);
        ClaimFixture.Push(remote, "3", ClaimFixture.Message("3-three", "mini", "Alpha", "2026-03-01T08:00:00Z"));
        using ServerFactory factory = new(root.Path);

        JsonElement first = Project(await Scanned(factory), "Plain");
        ClaimFixture.Push(remote, "5", ClaimFixture.Message("5-five", "mini", "Alpha", "2026-03-01T08:00:00Z"));
        using JsonDocument again = await Backlogs(factory);

        Assert.Empty(first.GetProperty("claims").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, first.GetProperty("claimsChecked").ValueKind);
        Assert.False(first.GetProperty("claimsStale").GetBoolean());
        Assert.Equal(
            [3], Project(again, "Alpha").GetProperty("claims").EnumerateArray().Select(claim => claim.GetProperty("item").GetInt32()));
    }

    [Fact]
    public async Task FactoryEntry_TakesItsClaimsFromTheRootsOrigin()
    {
        using TempFactoryRoot root = new();
        string factoryFolder = Path.Combine(root.Path, "factory");
        Directory.CreateDirectory(factoryFolder);
        File.WriteAllText(
            Path.Combine(factoryFolder, "backlog.md"), "- [ ] [#2](backlog/002-a.md) Two\n- [ ] [#5](backlog/005-b.md) Five\n");
        TempFactoryRoot.InitGit(root.Path, "main");
        TempFactoryRoot.Git(root.Path, "branch", "develop");
        string develop = Path.Combine(root.Path, ".develop");
        TempFactoryRoot.Git(root.Path, "worktree", "add", "-q", develop, "develop");
        string rootRemote = root.AddBareRemote(root.Path);
        (_, string alphaRemote) = ClaimFixture.Project(root, "Alpha", Backlog);
        ClaimFixture.Push(rootRemote, "2", ClaimFixture.Message("2-two", "studio", "factory", "2026-03-01T08:00:00Z"));
        ClaimFixture.Push(rootRemote, "4", ClaimFixture.Message("4-four", "studio", "factory", "2026-03-01T08:00:00Z"));
        ClaimFixture.Push(alphaRemote, "5", ClaimFixture.Message("5-five", "mini", "Alpha", "2026-03-01T08:00:00Z"));
        using ServerFactory factory = new(root.Path) { FactoryDevelopDir = develop };

        JsonDocument backlogs = await Scanned(factory);

        JsonElement entry = Project(backlogs, "factory");
        Assert.True(entry.GetProperty("readable").GetBoolean());
        Assert.Equal([2], entry.GetProperty("claims").EnumerateArray().Select(claim => claim.GetProperty("item").GetInt32()));
        Assert.Equal(
            [5], Project(backlogs, "Alpha").GetProperty("claims").EnumerateArray().Select(claim => claim.GetProperty("item").GetInt32()));
    }

    [Fact]
    public async Task Changes_ClaimsFingerprintFollowsTheClaimStateOnly()
    {
        using TempFactoryRoot root = new();
        (string folder, string remote) = ClaimFixture.Project(root, "Alpha", Backlog);
        ClaimFixture.Project(root, "Beta", Backlog);
        ClaimFixture.Push(remote, "3", ClaimFixture.Message("3-three", "mini", "Alpha", "2026-03-01T08:00:00Z"));
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateTokenClient();
        ClaimWatcher watcher = factory.Services.GetRequiredService<ClaimWatcher>();

        (string alpha, string beta) first = await Prints(client);
        factory.Time.Now += TimeSpan.FromSeconds(60);
        await watcher.Scan();
        (string alpha, string beta) scanned = await Prints(client);

        factory.Time.Now += TimeSpan.FromSeconds(60);
        await watcher.Scan();
        Assert.Equal(scanned, await Prints(client));

        ClaimFixture.Push(remote, "5", ClaimFixture.Message("5-five", "mini", "Alpha", "2026-03-01T08:00:00Z"));
        factory.Time.Now += TimeSpan.FromSeconds(60);
        await watcher.Scan();
        (string alpha, string beta) added = await Prints(client);
        Assert.NotEqual(scanned.alpha, added.alpha);
        Assert.Equal(scanned.beta, added.beta);

        TempFactoryRoot.Git(folder, "remote", "set-url", "origin", Path.Combine(root.Path, ".remotes", "missing.git"));
        factory.Time.Now += TimeSpan.FromSeconds(60);
        await watcher.Scan();
        (string alpha, string beta) stale = await Prints(client);
        Assert.NotEqual(added.alpha, stale.alpha);
        Assert.Equal(added.beta, stale.beta);
        Assert.Equal(first.beta, stale.beta);
    }

    /// <summary>Creates the client (the host's start scan begins), moves the clock 60 s, awaits a scan and returns
    /// <c>GET /api/backlogs</c>.</summary>
    private static async Task<JsonDocument> Scanned(ServerFactory factory)
    {
        using HttpClient client = factory.CreateTokenClient();
        factory.Time.Now += TimeSpan.FromSeconds(60);
        await factory.Services.GetRequiredService<ClaimWatcher>().Scan();
        return JsonDocument.Parse(await client.GetStringAsync("/api/backlogs"));
    }

    private static async Task<JsonDocument> Backlogs(ServerFactory factory)
    {
        using HttpClient client = factory.CreateTokenClient();
        return JsonDocument.Parse(await client.GetStringAsync("/api/backlogs"));
    }

    private static JsonElement Project(JsonDocument backlogs, string name)
    {
        return backlogs.RootElement.GetProperty("projects").EnumerateArray()
            .Single(project => project.GetProperty("name").GetString() == name);
    }

    private static async Task<(string Alpha, string Beta)> Prints(HttpClient client)
    {
        using JsonDocument changes = JsonDocument.Parse(await client.GetStringAsync("/api/changes"));
        JsonElement projects = changes.RootElement.GetProperty("projects");
        return (projects.GetProperty("Alpha").GetProperty("claims").GetString()!,
            projects.GetProperty("Beta").GetProperty("claims").GetString()!);
    }
}
