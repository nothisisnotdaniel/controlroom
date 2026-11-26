using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace ControlRoom.Server.Tests;

public sealed class WindowsReleaseTests : IDisposable
{
    private readonly ITestOutputHelper output;
    private TempTokenFolder? fixture;
    private bool rootJunction;

    public WindowsReleaseTests(ITestOutputHelper output)
    {
        this.output = output;
    }

    [Theory]
    [InlineData("lifecycle", null, null)]
    [InlineData("live-server-install", null, null)]
    [InlineData("interval", null, null)]
    [InlineData("expect-line", null, null)]
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
    [InlineData("job-replace", null, null)]
    [InlineData("root-junction", null, null)]
    [InlineData("linked-stamp", null, null)]
    [InlineData("linked-jobs", null, null)]
    [InlineData("linked-job", null, null)]
    [InlineData("linked-state", null, null)]
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

        fixture = new TempTokenFolder();
        rootJunction = scenario == "root-junction";
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
            if (rootJunction)
            {
                Assert.False(File.Exists(Path.Combine(fixture.Path, "ControlRoom", "update-result.json")));
                output.WriteLine("Installer refusals, the physical-root updater and the absent result passed.");
            }
            return;
        }

        Assert.NotNull(result);
        Assert.Equal(clock.GetUtcNow().ToString("yyyyMMddTHHmmssZ", System.Globalization.CultureInfo.InvariantCulture), result.Run);
        Assert.Equal(version, result.Version);
        Assert.Equal(reason, result.Reason);
        Assert.Equal(reason is null ? "1.2.0" : null, result.Previous);
        Assert.Equal(reason is null ? UpdateResult.Updated : UpdateResult.Failed, result.Result);
    }

    // xUnit records disposal exceptions alongside any failure from the test body.
    public void Dispose()
    {
        if (fixture is null)
        {
            return;
        }

        if (rootJunction)
        {
            output.WriteLine(WindowsReleaseFixture.DetachRootJunction(fixture.Path));
        }
        fixture.Dispose();
        if (rootJunction)
        {
            output.WriteLine("Removed the private fixture tree.");
        }
    }

    [Theory]
    [InlineData("scripts")]
    [InlineData("roomkit-package")]
    public void Updater_DecidesTheFirstCheckMarkerWithTheNoFollowOpen(string folder)
    {
        string updater = File.ReadAllText(UpdaterIn(folder))
            .Replace("\r\n", "\n", StringComparison.Ordinal);
        int start = updater.IndexOf("function Test-UpdateDue(", StringComparison.Ordinal);
        Assert.True(start >= 0);
        int end = updater.IndexOf("\n}\n", start, StringComparison.Ordinal);
        Assert.True(end > start);
        string body = updater[start..end];
        Assert.DoesNotContain("[IO.File]::Exists", body, StringComparison.Ordinal);
        Assert.Contains("try { $marker = [RoomKit.UpdateEntry]::Open((Join-Path $Support 'first-check'), $false) }\n" +
            "    catch { return $true }\n    if ($marker) { $marker.Dispose(); return $true }", body, StringComparison.Ordinal);
        Assert.True(body.IndexOf("'first-check'", StringComparison.Ordinal) < body.IndexOf("'last-check'", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("scripts")]
    [InlineData("roomkit-package")]
    public void Updater_OpensFilesWithoutBackupSemanticsFirst(string folder)
    {
        string updater = File.ReadAllText(UpdaterIn(folder))
            .Replace("\r\n", "\n", StringComparison.Ordinal);
        int start = updater.IndexOf("private static SafeFileHandle Open(string path, bool directory, FileShare share) {",
            StringComparison.Ordinal);
        Assert.True(start >= 0);
        int end = updater.IndexOf("private static SafeFileHandle Checked(", start, StringComparison.Ordinal);
        Assert.True(end > start);
        string body = updater[start..end];
        int file = body.IndexOf("if (!directory) {", StringComparison.Ordinal);
        int plain = body.IndexOf("CreateFile(path, 0x80000000, share, IntPtr.Zero, 3, 0x00200000, IntPtr.Zero)",
            StringComparison.Ordinal);
        int fallback = body.IndexOf("if (fileError != 5) throw new Win32Exception(fileError);", StringComparison.Ordinal);
        int backup = body.IndexOf("CreateFile(path, 0x80000000, share, IntPtr.Zero, 3, 0x02200000, IntPtr.Zero)",
            StringComparison.Ordinal);
        Assert.True(file >= 0 && file < plain && plain < fallback && fallback < backup);
        Assert.Contains("if (fileError == 2 || fileError == 3) return null;", body, StringComparison.Ordinal);
        Assert.Equal(1, body.Split("0x02200000").Length - 1);
    }

    // ControlRoom's own scripts/ or, for "roomkit-package", the restored RoomKit package's scripts/.
    private static string UpdaterIn(string folder)
    {
        string scripts = folder == "roomkit-package"
            ? RoomKitPackage.Scripts()
            : Path.Combine([RepoCopy.RepoFolder(), .. folder.Split('/')]);
        return Path.Combine(scripts, "updater.ps1");
    }

    [Fact]
    public void Readme_VerifiesTheDownloadBeforeRunningTheExeAndBothTasks()
    {
        string readme = File.ReadAllText(Path.Combine(RepoCopy.RepoFolder(), "README.md"));
        int last = readme.IndexOf("## Windows", StringComparison.Ordinal);
        Assert.True(last >= 0);
        foreach (string part in new[] { "gh auth login", "gh release download", "gh attestation verify", "Get-FileHash",
                     "Expand-Archive", @".\controlroom.exe version", @".\controlroom.exe --FactoryRoot",
                     @".\controlroom.exe token", @".\launch-agent.ps1 install", "Install updates automatically",
                     "Set-ExecutionPolicy -Scope Process RemoteSigned",
                     @".\launch-agent.ps1 status", @".\launch-agent.ps1 uninstall" })
        {
            last = readme.IndexOf(part, last + 1, StringComparison.Ordinal);
            Assert.True(last >= 0, part);
        }
    }
}
