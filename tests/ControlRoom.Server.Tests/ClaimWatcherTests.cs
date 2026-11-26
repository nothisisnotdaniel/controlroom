namespace ControlRoom.Server.Tests;

/// <summary>The claim watcher built directly (never started, so its timer never runs): each test awaits
/// <see cref="ClaimWatcher.Scan"/> itself, moves the fake clock by hand and checks <see cref="ClaimWatcher.State"/>.
/// The only origin is a local bare repo or a missing folder.</summary>
public sealed class ClaimWatcherTests : IDisposable
{
    private const string Backlog = "- [ ] [#3](backlog/003-a.md) Three\n- [ ] [#5](backlog/005-b.md) Five\n";

    private readonly TempFactoryRoot root = new();

    private readonly FakeTime time = new();

    private readonly LogCapture logs = new();

    private readonly ClaimWatcher watcher;

    public ClaimWatcherTests()
    {
        watcher = new ClaimWatcher(new FactoryRoot(root.Path), time, logs.For<ClaimWatcher>());
    }

    public void Dispose()
    {
        watcher.Dispose();
        root.Dispose();
    }

    [Fact]
    public void NeverScanned_HasNoClaimsNoCheckedTimeAndIsNotStale()
    {
        ClaimFixture.Project(root, "Alpha", Backlog);

        ClaimState state = watcher.State("Alpha");

        Assert.Empty(state.Claims);
        Assert.Null(state.Checked);
        Assert.False(state.Stale);
        Assert.Equal(ClaimState.None, watcher.State("factory"));
    }

    [Fact]
    public async Task NoOrigin_HasNoClaimsAndIsNotStale()
    {
        string folder = root.AddProject("Alpha", Backlog);
        TempFactoryRoot.InitGit(folder, "develop");

        await watcher.Scan();

        ClaimState state = watcher.State("Alpha");
        Assert.Empty(state.Claims);
        Assert.Null(state.Checked);
        Assert.False(state.Stale);
    }

    [Fact]
    public async Task TwoScansWithinAMinute_ReadOriginOnce()
    {
        (_, string remote) = ClaimFixture.Project(root, "Alpha", Backlog);
        ClaimFixture.Push(remote, "3", ClaimFixture.Message("3-a", "mini", "Alpha", "2026-03-01T08:00:00Z"));

        await watcher.Scan();
        ClaimFixture.Push(remote, "5", ClaimFixture.Message("5-b", "mini", "Alpha", "2026-03-01T08:01:00Z"));
        time.Now += TimeSpan.FromSeconds(30);
        await watcher.Scan();

        Assert.Equal([3], watcher.State("Alpha").Claims.Select(claim => claim.Item));
        Assert.Equal(FakeTime.Start, watcher.State("Alpha").Checked);

        time.Now = FakeTime.Start + TimeSpan.FromSeconds(60);
        await watcher.Scan();

        ClaimState state = watcher.State("Alpha");
        Assert.Equal([3, 5], state.Claims.Select(claim => claim.Item));
        Assert.Equal(time.Now, state.Checked);
        Assert.Equal(new ItemClaim(5, "5-b", "mini", "2026-03-01T08:01:00Z"), state.Claims[1]);
    }

    [Fact]
    public async Task UnreachableOrigin_KeepsTheLastClaimsAsStaleThenDropsThemAfterTenMinutes()
    {
        (string folder, string remote) = ClaimFixture.Project(root, "Alpha", Backlog);
        ClaimFixture.Push(remote, "3", ClaimFixture.Message("3-a", "mini", "Alpha", "2026-03-01T08:00:00Z"));
        await watcher.Scan();
        TempFactoryRoot.Git(folder, "remote", "set-url", "origin", Path.Combine(root.Path, ".remotes", "missing.git"));

        time.Now += TimeSpan.FromSeconds(60);
        await watcher.Scan();

        ClaimState stale = watcher.State("Alpha");
        Assert.Equal([3], stale.Claims.Select(claim => claim.Item));
        Assert.Equal(FakeTime.Start, stale.Checked);
        Assert.True(stale.Stale);
        Assert.All(logs.Lines, line => Assert.DoesNotContain(root.Path, line, StringComparison.Ordinal));

        time.Now = FakeTime.Start + ClaimWatcher.MaxAge;
        Assert.Equal([3], watcher.State("Alpha").Claims.Select(claim => claim.Item));
        time.Now = FakeTime.Start + ClaimWatcher.MaxAge + TimeSpan.FromSeconds(1);
        await watcher.Scan();

        ClaimState old = watcher.State("Alpha");
        Assert.Empty(old.Claims);
        Assert.Null(old.Checked);
        Assert.True(old.Stale);

        TempFactoryRoot.Git(folder, "remote", "set-url", "origin", remote);
        time.Now += TimeSpan.FromSeconds(60);
        await watcher.Scan();

        ClaimState back = watcher.State("Alpha");
        Assert.Equal([3], back.Claims.Select(claim => claim.Item));
        Assert.Equal(time.Now, back.Checked);
        Assert.False(back.Stale);
    }

    [Fact]
    public async Task ChangedClaim_IsReadAgainAndAGoneOneIsDropped()
    {
        (_, string remote) = ClaimFixture.Project(root, "Alpha", Backlog);
        ClaimFixture.Push(remote, "3", ClaimFixture.Message("3-a", "mini", "Alpha", "2026-03-01T08:00:00Z"));
        ClaimFixture.Push(remote, "5", ClaimFixture.Message("5-b", "mini", "Alpha", "2026-03-01T08:00:00Z"));
        await watcher.Scan();

        ClaimFixture.Push(remote, "3", ClaimFixture.Message("3-c", "studio", "Alpha", "2026-03-01T09:00:00Z"));
        TempFactoryRoot.Git(remote, "update-ref", "-d", "refs/factory/claims/5");
        time.Now += TimeSpan.FromSeconds(60);
        await watcher.Scan();

        Assert.Equal([new ItemClaim(3, "3-c", "studio", "2026-03-01T09:00:00Z")], watcher.State("Alpha").Claims);
    }

    [Fact]
    public async Task OddRefsAreLeftOutAndALargeMessageKeepsTheClaimWithUnknownFields()
    {
        (_, string remote) = ClaimFixture.Project(root, "Alpha", Backlog);
        string message = ClaimFixture.Message("3-a", "mini", "Alpha", "2026-03-01T08:00:00Z");
        ClaimFixture.Push(remote, "abc", message);
        ClaimFixture.Push(remote, "1/x", message);
        ClaimFixture.Push(remote, "1234567", message);
        ClaimFixture.Push(remote, "5", message + new string('x', ClaimWatcher.MaxMessageBytes) + "\n");

        await watcher.Scan();

        Assert.Equal([new ItemClaim(5, null, null, null)], watcher.State("Alpha").Claims);
    }

    [Fact]
    public async Task ATagOverALargeClaimCommit_IsNotPeeledAndTheOtherClaimsStayFresh()
    {
        (_, string remote) = ClaimFixture.Project(root, "Alpha", Backlog);
        string large = ClaimFixture.Push(
            remote, "big", ClaimFixture.Message("3-a", "mini", "Alpha", "2026-03-01T08:00:00Z")
            + new string('x', ClaimWatcher.MaxMessageBytes * 2) + "\n");
        TempFactoryRoot.Git(remote, "tag", "-a", "-m", "small", "wrapped", large);
        string tag = TempFactoryRoot.Git(remote, "rev-parse", "refs/tags/wrapped");
        TempFactoryRoot.Git(remote, "update-ref", "refs/factory/claims/3", tag);
        ClaimFixture.Push(remote, "5", ClaimFixture.Message("5-b", "mini", "Alpha", "2026-03-01T08:00:00Z"));

        await watcher.Scan();

        ClaimState state = watcher.State("Alpha");
        Assert.Equal(
            [new ItemClaim(3, null, null, null), new ItemClaim(5, "5-b", "mini", "2026-03-01T08:00:00Z")], state.Claims);
        Assert.False(state.Stale);
        Assert.Equal(time.Now, state.Checked);
    }

    [Fact]
    public async Task StartAsync_RunsTheTimerEveryMinuteAndStopWaitsForTheFirstScan()
    {
        ClaimFixture.Project(root, "Alpha", Backlog);

        await watcher.StartAsync(CancellationToken.None);
        await watcher.StopAsync(CancellationToken.None);

        Assert.Equal([(TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(60))], time.Timers);
        Assert.Equal(time.Now, watcher.State("Alpha").Checked);
        Assert.Empty(watcher.State("Alpha").Claims);
    }

    [Fact]
    public void Parse_KeepsTheFirstOfEachKeyCleanedAndChecked()
    {
        string message = "claim x\n\nJob: 7-seven\nJob: 8-eight\nMachine: ‮mac\u0007​-mini\nClaimed: 2026-03-01T08:00:00Z\n";

        Assert.Equal(new ItemClaim(7, "7-seven", "mac-mini", "2026-03-01T08:00:00Z"), ClaimWatcher.Parse(7, message));
        Assert.Equal(
            new ItemClaim(7, null, null, null),
            ClaimWatcher.Parse(7, "Job: ../etc\nMachine: \u0007\nClaimed: 2026-03-01 08:00:00\n"));
        Assert.Equal(
            new ItemClaim(7, null, null, null), ClaimWatcher.Parse(7, "Job:7-seven\nClaimed: 2026-03-01T08:00:00Z \u0000x\n"));
        Assert.Equal(ClaimWatcher.MaxValueLength, ClaimWatcher.Parse(7, "Machine: " + new string('m', 500)).Machine!.Length);
    }

    [Theory]
    [InlineData("refs/factory/claims/3", 3)]
    [InlineData("refs/factory/claims/003", 3)]
    [InlineData("refs/factory/claims/999999", 999999)]
    [InlineData("refs/factory/claims/1234567", null)]
    [InlineData("refs/factory/claims/abc", null)]
    [InlineData("refs/factory/claims/1/x", null)]
    [InlineData("refs/factory/claims/", null)]
    [InlineData("refs/factory/claims/٣", null)]
    [InlineData("refs/heads/3", null)]
    public void ItemOf_TakesOnlyOneToSixDigits(string name, int? item)
    {
        Assert.Equal(item, ClaimWatcher.ItemOf(name));
    }
}
