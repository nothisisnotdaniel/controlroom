using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace ControlRoom.Server.Tests;

public sealed class WindowsReleaseTests
{
    [Theory]
    [InlineData("lifecycle", null, null)]
    [InlineData("interval", null, null)]
    [InlineData("defer", null, null)]
    [InlineData("concurrent-install", "1.3.0", null)]
    [InlineData("stable", "1.3.0", null)]
    [InlineData("develop", "1.4.0-develop.12", null)]
    [InlineData("archive", "1.3.0", "check")]
    [InlineData("entry-acl", "1.3.0", "swap")]
    [InlineData("entry-link", "1.3.0", "swap")]
    [InlineData("entry-directory", "1.3.0", "swap")]
    [InlineData("entry-size", "1.3.0", "swap")]
    [InlineData("wrong-listener", "1.3.0", "no_answer")]
    [InlineData("health-array", "1.3.0", "no_answer")]
    [InlineData("health-list", "1.3.0", "no_answer")]
    [InlineData("health-bytes", "1.3.0", "no_answer")]
    [InlineData("release-bytes", null, null)]
    [InlineData("release-pages", null, null)]
    [InlineData("job-scan", null, null)]
    [InlineData("linked-stamp", null, null)]
    [InlineData("linked-jobs", null, null)]
    [InlineData("result-retry", "1.3.0", null)]
    [InlineData("result-before-marker", "1.3.0", null)]
    [InlineData("invalid-pending", null, null)]
    [InlineData("failed-result-retry", "1.3.0", "check")]
    [InlineData("uninstall-pending", null, null)]
    [InlineData("pending-active", "1.3.0", "no_answer")]
    [InlineData("interrupted-swap", "1.3.0", "swap")]
    [InlineData("interrupted-candidate", "1.3.0", "swap")]
    [InlineData("interrupted-rollback", "1.3.0", "no_answer")]
    [InlineData("pending-install-digest", null, null)]
    [InlineData("restore-result-retry", "1.3.0", "rollback_failed")]
    [InlineData("rollback", "1.3.0", "rollback_failed")]
    [InlineData("start", "1.3.0", "bootstrap_failed")]
    [InlineData("swap", "1.3.0", "swap")]
    public async Task Scripts_ManageOnlyTheFixtureAndReportConfirmedOutcomes(string scenario, string? version, string? reason)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using TempTokenFolder fixture = new();
        FakeTime clock = new();
        WindowsReleaseFixture.Run(fixture.Path, Path.Combine(RepoCopy.RepoFolder(), "scripts"), scenario, clock);
        if (scenario == "lifecycle")
        {
            string[] registered = File.ReadAllLines(Path.Combine(fixture.Path, "server-root.txt"));
            await using WebApplication? server = ServerApp.Create(
                ["--FactoryRootBase64", registered[0], "--TokenFile", Path.Combine(fixture.Path, "probe", "access-token")],
                TextWriter.Null);
            Assert.NotNull(server);
            Assert.Equal(registered[1], server.Services.GetRequiredService<FactoryRoot>().Path);
        }
        UpdateResult? result = new UpdateResultReader(Path.Combine(fixture.Path, "ControlRoom"), UnixOwner.CurrentUser()).Read();
        if (version is null)
        {
            Assert.Null(result);
            return;
        }

        Assert.NotNull(result);
        Assert.Equal(clock.GetUtcNow().ToString("yyyyMMddTHHmmssZ", System.Globalization.CultureInfo.InvariantCulture), result.Run);
        Assert.Equal(version, result.Version);
        Assert.Equal(reason, result.Reason);
        Assert.Equal(reason is null ? "1.2.0" : null, result.Previous);
        Assert.Equal(reason is null ? UpdateResult.Updated : UpdateResult.Failed, result.Result);
    }

    [Fact]
    public void Readme_VerifiesTheDownloadBeforeRunningTheExeAndBothTasks()
    {
        string readme = File.ReadAllText(Path.Combine(RepoCopy.RepoFolder(), "README.md"));
        int last = readme.IndexOf("## Windows", StringComparison.Ordinal);
        Assert.True(last >= 0);
        foreach (string part in new[] { "gh release download", "gh attestation verify", "Get-FileHash",
                     "Expand-Archive", @".\controlroom.exe version", @".\controlroom.exe --FactoryRoot",
                     @".\controlroom.exe token", @".\launch-agent.ps1 install", "Install updates automatically",
                     @".\launch-agent.ps1 status", @".\launch-agent.ps1 uninstall" })
        {
            last = readme.IndexOf(part, last + 1, StringComparison.Ordinal);
            Assert.True(last >= 0, part);
        }
    }
}
