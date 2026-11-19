using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace ControlRoom.Server.Tests;

/// <summary><c>scripts/updater.sh</c>, run with <c>/bin/sh</c> in its own temp <c>HOME</c> and factory root. Fakes
/// on <c>PATH</c>: <c>launchctl</c> (records calls; <c>bootstrap</c> marks the server as loaded), <c>sleep</c> (records
/// each call and returns at once: the fake clock), <c>mv</c> (fails for a chosen source, else the real one) and
/// <c>curl</c> (the latest-release redirect, the release files, and <c>/health</c> answering with the <c>health</c>
/// file of the server folder in place while loaded). No test waits on time or opens a port.</summary>
[UnsupportedOSPlatform("windows")]
public sealed partial class UpdaterScriptTests
{
    private const string Releases = "https://github.com/nothisisnotdaniel/controlroom/releases";

    private const string HealthCall = "-q -fsS --max-time 2 --noproxy * http://127.0.0.1:8100/health";

    private const string Token = "TOKEN-VALUE-never-sent-0123456789abcdefghijkl";

    private const string ListUrl = "https://api.github.com/repos/nothisisnotdaniel/controlroom/releases?per_page=100";

    private const string ListCall = "--proto =https --tlsv1.2 -fsS --max-time 30 --max-filesize 1048576 " + ListUrl;

    private const string Develop = "{\"autoUpdate\":true,\"channel\":\"develop\"}\n";

    [Fact]
    public void SharedFunctions_AreTheSameAsInLaunchAgent()
    {
        string agent = File.ReadAllText(Path.Combine(UpdaterHome.RepoFolder(), "scripts", "launch-agent.sh"));
        string updater = File.ReadAllText(UpdaterHome.ScriptPath());
        foreach (string name in new[] { "valid_tag", "latest_tag", "download", "check_and_extract" })
        {
            string expected = Function(agent, name);
            Assert.Equal(expected, Function(updater, name));
        }

        string code = string.Join('\n', updater.Split('\n').Where(line => !line.TrimStart().StartsWith('#')));
        Assert.DoesNotContain("access-token", code, StringComparison.Ordinal);
        Assert.DoesNotContain("Authorization", code, StringComparison.Ordinal);
        Assert.DoesNotContain("--header", code, StringComparison.Ordinal);
        Assert.DoesNotContain(" -H ", code, StringComparison.Ordinal);
    }

    [Fact]
    public void NewerRelease_IsSwappedInConfirmedThroughHealthAndRecorded()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using UpdaterHome home = new();
        home.AddRelease("1.3.0", "1.3.0");
        home.SetRedirect(Releases + "/tag/v1.3.0");

        string line = home.RunOnce();

        Assert.Matches(LinePattern("updated v1.2.0 v1.3.0"), line);
        Assert.Equal(
            ["bootout " + home.Service, "print " + home.Service, "bootstrap " + home.Domain + " " + home.ServerPlist,
                "print " + home.Service],
            home.AllLaunchctlCalls());
        Assert.Equal(["-nP -iTCP:8100 -sTCP:LISTEN -t"], home.LsofCalls());
        Assert.Equal("{\"version\":\"1.3.0\"}", File.ReadAllText(Path.Combine(home.Server, "health")));
        Assert.Equal("{\"version\":\"1.2.0\"}", File.ReadAllText(Path.Combine(home.Previous, "health")));
        Assert.Equal("v1.3.0\n", File.ReadAllText(home.Marker));
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(home.Marker));
        AssertResult(home, "\"result\":\"updated\",\"version\":\"1.3.0\",\"previous\":\"1.2.0\"");
        IReadOnlyList<string> curl = home.CurlCalls();
        Assert.Equal(HealthCall, curl[^1]);
        Assert.Single(curl, call => call.Contains("/health", StringComparison.Ordinal));
        Assert.Empty(home.SleepCalls());
        AssertNoToken(home);
        Assert.Empty(home.Leftovers());
    }

    [Fact]
    public void WithAnUnreadableTokenFile_StillUpdates()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using UpdaterHome home = new();
        File.SetUnixFileMode(home.TokenFile, UnixFileMode.None);
        home.AddRelease("1.3.0", "1.3.0");
        home.SetRedirect(Releases + "/tag/v1.3.0");

        Assert.Matches(LinePattern("updated v1.2.0 v1.3.0"), home.RunOnce());
        AssertNoToken(home);
    }

    [Fact]
    public void InstalledPrerelease_IsReplacedByTheSameFinalVersion()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using UpdaterHome home = new("v1.3.0-rc.1");
        home.AddRelease("1.3.0", "1.3.0");
        home.SetRedirect(Releases + "/tag/v1.3.0");

        Assert.Matches(LinePattern("updated v1.3.0-rc.1 v1.3.0"), home.RunOnce());
        AssertResult(home, "\"result\":\"updated\",\"version\":\"1.3.0\",\"previous\":\"1.3.0-rc.1\"");
    }

    [Fact]
    public void NoAnswerFromTheNewVersion_RollsBackAfterFifteenPolls()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using UpdaterHome home = new();
        home.AddRelease("1.3.0", "9.9.9");
        home.SetRedirect(Releases + "/tag/v1.3.0");

        string line = home.RunOnce();

        Assert.Matches(LinePattern("rolled_back v1.2.0 v1.3.0"), line);
        Assert.Equal(
            ["bootout " + home.Service, "bootstrap " + home.Domain + " " + home.ServerPlist,
                "bootout " + home.Service, "bootstrap " + home.Domain + " " + home.ServerPlist],
            home.LaunchctlCalls());
        Assert.Equal("{\"version\":\"1.2.0\"}", File.ReadAllText(Path.Combine(home.Server, "health")));
        Assert.False(Directory.Exists(home.Previous));
        Assert.Equal("v1.2.0\n", File.ReadAllText(home.Marker));
        AssertResult(home, "\"result\":\"update_failed\",\"version\":\"1.3.0\",\"reason\":\"no_answer\"");
        Assert.Equal(16, home.CurlCalls().Count(call => call == HealthCall));
        Assert.Equal(14, home.SleepCalls().Count);
        Assert.All(home.SleepCalls(), call => Assert.Equal("2", call));
        AssertNoToken(home);
        Assert.Empty(home.Leftovers());
    }

    [Fact]
    public void NoAnswerFromTheOldVersionAfterTheRollback_IsRollbackFailed()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using UpdaterHome home = new();
        File.Delete(Path.Combine(home.Server, "health"));
        home.AddRelease("1.3.0", null);
        home.SetRedirect(Releases + "/tag/v1.3.0");

        Assert.Matches(LinePattern("rollback_failed v1.2.0 v1.3.0"), home.RunOnce());
        AssertResult(home, "\"result\":\"update_failed\",\"version\":\"1.3.0\",\"reason\":\"rollback_failed\"");
        Assert.Equal(30, home.CurlCalls().Count(call => call == HealthCall));
        Assert.Equal(28, home.SleepCalls().Count);
        Assert.True(File.Exists(Path.Combine(home.Server, "ControlRoom.Server")));
        Assert.False(Directory.Exists(home.Previous));
    }

    [Fact]
    public void FailedRollbackRename_IsRollbackFailed()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using UpdaterHome home = new();
        home.AddRelease("1.3.0", "9.9.9");
        home.SetRedirect(Releases + "/tag/v1.3.0");
        home.FailMoveFrom("/server.previous");

        Assert.Matches(LinePattern("rollback_failed v1.2.0 v1.3.0"), home.RunOnce());
        AssertResult(home, "\"result\":\"update_failed\",\"version\":\"1.3.0\",\"reason\":\"rollback_failed\"");
        Assert.Equal("bootstrap " + home.Domain + " " + home.ServerPlist, home.LaunchctlCalls()[^1]);
        Assert.Equal(15, home.CurlCalls().Count(call => call == HealthCall));
    }

    [Fact]
    public void FailedRenameOfTheNewServer_PutsTheOldOneBackAsSwapFailed()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using UpdaterHome home = new();
        home.AddRelease("1.3.0", "1.3.0");
        home.SetRedirect(Releases + "/tag/v1.3.0");
        home.FailMoveFrom("/controlroom-1.3.0-osx-arm64");

        Assert.Matches(LinePattern("swap_failed v1.2.0 v1.3.0"), home.RunOnce());
        Assert.Equal(["bootout " + home.Service, "bootstrap " + home.Domain + " " + home.ServerPlist], home.LaunchctlCalls());
        Assert.Equal("{\"version\":\"1.2.0\"}", File.ReadAllText(Path.Combine(home.Server, "health")));
        Assert.False(Directory.Exists(home.Previous));
        Assert.Equal("v1.2.0\n", File.ReadAllText(home.Marker));
        AssertResult(home, "\"result\":\"update_failed\",\"version\":\"1.3.0\",\"reason\":\"swap\"");
        Assert.Empty(home.Leftovers());
    }

    [Fact]
    public void FailedRenameOfTheOldServer_LeavesItInPlaceAsSwapFailed()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using UpdaterHome home = new();
        home.AddRelease("1.3.0", "1.3.0");
        home.SetRedirect(Releases + "/tag/v1.3.0");
        home.FailMoveFrom("/ControlRoom/server");

        Assert.Matches(LinePattern("swap_failed v1.2.0 v1.3.0"), home.RunOnce());
        Assert.Equal("{\"version\":\"1.2.0\"}", File.ReadAllText(Path.Combine(home.Server, "health")));
        AssertResult(home, "\"result\":\"update_failed\",\"version\":\"1.3.0\",\"reason\":\"swap\"");
    }

    [Fact]
    public void FailedRenameAndNoOldAnswer_IsRollbackFailed()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using UpdaterHome home = new();
        File.Delete(Path.Combine(home.Server, "health"));
        home.AddRelease("1.3.0", "1.3.0");
        home.SetRedirect(Releases + "/tag/v1.3.0");
        home.FailMoveFrom("/controlroom-1.3.0-osx-arm64");

        Assert.Matches(LinePattern("rollback_failed v1.2.0 v1.3.0"), home.RunOnce());
        AssertResult(home, "\"result\":\"update_failed\",\"version\":\"1.3.0\",\"reason\":\"rollback_failed\"");
    }

    [Theory]
    [InlineData("other-listener")]
    [InlineData("two-listeners")]
    [InlineData("no-listener")]
    [InlineData("not-running")]
    public void HealthAnswerNotFromTheLaunchdServer_IsNeverCountedAsUpdated(string fault)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using UpdaterHome home = new();
        home.AddRelease("1.3.0", "1.3.0");
        home.SetRedirect(Releases + "/tag/v1.3.0");
        switch (fault)
        {
            case "other-listener":
                home.SetListeners("999\n");
                break;
            case "two-listeners":
                home.SetListeners("4242\n999\n");
                break;
            case "no-listener":
                home.SetListeners("");
                break;
            default:
                home.ServerNotRunning();
                break;
        }

        Assert.Matches(LinePattern("rollback_failed v1.2.0 v1.3.0"), home.RunOnce());
        Assert.Equal("v1.2.0\n", File.ReadAllText(home.Marker));
        AssertResult(home, "\"result\":\"update_failed\",\"version\":\"1.3.0\",\"reason\":\"rollback_failed\"");
        Assert.Equal(30, home.CurlCalls().Count(call => call == HealthCall));
        Assert.Equal(28, home.SleepCalls().Count);
        Assert.Equal(32, home.AllLaunchctlCalls().Count(call => call == "print " + home.Service));
    }

    [Fact]
    public void OnlyTheServerPidListening_IsConfirmed()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using UpdaterHome home = new();
        home.AddRelease("1.3.0", "1.3.0");
        home.SetRedirect(Releases + "/tag/v1.3.0");
        home.SetListeners("4242\n");

        Assert.Matches(LinePattern("updated v1.2.0 v1.3.0"), home.RunOnce());
        Assert.Equal("v1.3.0\n", File.ReadAllText(home.Marker));
    }

    [Fact]
    public void FailedMarkerWrite_PutsTheOldVersionBackAsSwapFailed()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using UpdaterHome home = new();
        home.AddRelease("1.3.0", "1.3.0");
        home.SetRedirect(Releases + "/tag/v1.3.0");
        home.FailMoveTo("/installed-release");

        Assert.Matches(LinePattern("swap_failed v1.2.0 v1.3.0"), home.RunOnce());
        Assert.Equal(
            ["bootout " + home.Service, "bootstrap " + home.Domain + " " + home.ServerPlist,
                "bootout " + home.Service, "bootstrap " + home.Domain + " " + home.ServerPlist],
            home.LaunchctlCalls());
        Assert.Equal("{\"version\":\"1.2.0\"}", File.ReadAllText(Path.Combine(home.Server, "health")));
        Assert.False(Directory.Exists(home.Previous));
        Assert.Equal("v1.2.0\n", File.ReadAllText(home.Marker));
        AssertResult(home, "\"result\":\"update_failed\",\"version\":\"1.3.0\",\"reason\":\"swap\"");
        Assert.Equal(2, home.CurlCalls().Count(call => call == HealthCall));
        Assert.Empty(home.Leftovers());
    }

    [Fact]
    public void FailedMarkerWriteAndNoOldAnswer_IsRollbackFailed()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using UpdaterHome home = new();
        File.Delete(Path.Combine(home.Server, "health"));
        home.AddRelease("1.3.0", "1.3.0");
        home.SetRedirect(Releases + "/tag/v1.3.0");
        home.FailMoveTo("/installed-release");

        Assert.Matches(LinePattern("rollback_failed v1.2.0 v1.3.0"), home.RunOnce());
        Assert.Equal("v1.2.0\n", File.ReadAllText(home.Marker));
        AssertResult(home, "\"result\":\"update_failed\",\"version\":\"1.3.0\",\"reason\":\"rollback_failed\"");
    }

    [Theory]
    [InlineData(65536, false)]
    [InlineData(65537, true)]
    public void StateFileOverTheCap_CountsAsARunningJob(int size, bool running)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using UpdaterHome home = new();
        string content = "{\"stage\": \"done\"}\n";
        home.AddRawJob("1-big", content + new string(' ', size - content.Length));
        home.SetRedirect(Releases + "/tag/v1.2.0");

        if (running)
        {
            AssertNothingDone(home, "job_running", " v1.2.0");
        }
        else
        {
            Assert.Matches(LinePattern("current v1.2.0 v1.2.0"), home.RunOnce());
        }
    }

    [Fact]
    public void UnreadableStateFile_CountsAsARunningJob()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using UpdaterHome home = new();
        home.AddJob("1-done", "done");
        string state = Path.Combine(home.Root, "factory", "jobs", "1-done", "state.json");
        File.SetUnixFileMode(state, UnixFileMode.None);

        AssertNothingDone(home, "job_running", " v1.2.0");
        File.SetUnixFileMode(state, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    [Theory]
    [InlineData("{\"autoUpdate\":false}\n", true)]
    [InlineData("{\"autoUpdate\": true}\n", true)]
    [InlineData("{\"autoUpdate\":true}\n\n", true)]
    [InlineData("{\"autoUpdate\":true}\n", false)]
    [InlineData(null, true)]
    [InlineData("{\"autoUpdate\":false,\"channel\":\"develop\"}\n", true)]
    [InlineData("{\"autoUpdate\":false,\"channel\":\"stable\"}", true)]
    [InlineData("{\"autoUpdate\":true,\"channel\":\"develop\"}\n\n", true)]
    [InlineData("{\"autoUpdate\":true,\"channel\":\"develop\"}\r\n", true)]
    [InlineData("{\"autoUpdate\":true,\"channel\":\"beta\"}\n", true)]
    [InlineData("{\"autoUpdate\":true,\"channel\":\"Develop\"}\n", true)]
    [InlineData("{\"channel\":\"develop\",\"autoUpdate\":true}\n", true)]
    [InlineData("{\"autoUpdate\":true,\"channel\":\"develop\",\"x\":1}\n", true)]
    [InlineData("{\"autoUpdate\":true,\"channel\":\"develop\"}\n", false)]
    public void SettingNotExactlyOn_IsOffAndChangesNothing(string? settings, bool safeMode)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using UpdaterHome home = new();
        home.SetSettings(settings);
        if (!safeMode)
        {
            File.SetUnixFileMode(home.Settings, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead);
        }

        AssertNothingDone(home, "off", "");
    }

    [Fact]
    public void SymlinkedSetting_IsOff()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using UpdaterHome home = new();
        string target = Path.Combine(home.Path, "elsewhere.json");
        File.WriteAllText(target, "{\"autoUpdate\":true}\n");
        File.SetUnixFileMode(target, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        File.Delete(home.Settings);
        File.CreateSymbolicLink(home.Settings, target);

        AssertNothingDone(home, "off", "");
    }

    [Fact]
    public void LooseSupportFolder_IsOff()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using UpdaterHome home = new();
        File.SetUnixFileMode(home.Support, AccessToken.FolderMode | UnixFileMode.GroupRead | UnixFileMode.GroupExecute);

        AssertNothingDone(home, "off", "");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("1.2.0\n")]
    [InlineData("v1.2.0+build\n")]
    [InlineData("v01.2.0\n")]
    public void MissingOrInvalidMarker_IsNotRelease(string? marker)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using UpdaterHome home = new();
        home.SetMarker(marker);

        AssertNothingDone(home, "not_release", "");
    }

    [Fact]
    public void FreshLock_IsBusyAndKeepsTheLock()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using UpdaterHome home = new();
        Directory.CreateDirectory(home.Lock);

        AssertNothingDone(home, "busy", " v1.2.0");
        Assert.True(Directory.Exists(home.Lock));
    }

    [Fact]
    public void LockOlderThanAnHour_IsRemovedAndTheRunGoesOn()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using UpdaterHome home = new();
        Directory.CreateDirectory(home.Lock);
        Directory.SetLastWriteTimeUtc(home.Lock, DateTime.UtcNow.AddHours(-2));
        home.SetRedirect(Releases + "/tag/v1.2.0");

        Assert.Matches(LinePattern("current v1.2.0 v1.2.0"), home.RunOnce());
        Assert.False(Directory.Exists(home.Lock));
    }

    [Theory]
    [InlineData("spike")]
    [InlineData("spec")]
    [InlineData("build")]
    [InlineData("review")]
    public void ActiveJob_IsJobRunning(string stage)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using UpdaterHome home = new();
        home.AddJob("1-done", "done");
        home.AddJob("2-active", stage);

        AssertNothingDone(home, "job_running", " v1.2.0");
        Assert.False(Directory.Exists(home.Lock));
    }

    [Fact]
    public void FinishedJobs_DoNotStopTheRun()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using UpdaterHome home = new();
        home.AddJob("1-done", "done");
        home.AddJob("2-approve", "approve");
        home.AddJob("3-human", "needs-human");
        home.SetRedirect(Releases + "/tag/v1.2.0");

        Assert.Matches(LinePattern("current v1.2.0 v1.2.0"), home.RunOnce());
    }

    [Theory]
    [InlineData("v1.2.0")]
    [InlineData("v1.1.9")]
    [InlineData("v0.9.99")]
    [InlineData("v1.3.0-rc.1")]
    public void LatestNotNewerOrAPrerelease_IsCurrentAndChangesNothing(string latest)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using UpdaterHome home = new();
        home.SetRedirect(Releases + "/tag/" + latest);

        AssertNothingDone(home, "current", " v1.2.0 " + latest, curlCalls: 1);
    }

    [Theory]
    [InlineData("check")]
    [InlineData("no_answer")]
    public void LatestThatFailedItsCheckOrStart_IsSkipped(string reason)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using UpdaterHome home = new();
        home.SetRedirect(Releases + "/tag/v1.3.0");
        home.SetResult("{\"run\":\"20261118T200000Z\",\"result\":\"update_failed\",\"version\":\"1.3.0\",\"reason\":\""
            + reason + "\"}\n");

        AssertNothingDone(home, "skipped_failed", " v1.2.0 v1.3.0", curlCalls: 1);
    }

    [Theory]
    [InlineData("{\"run\":\"20261118T200000Z\",\"result\":\"update_failed\",\"version\":\"1.3.0\",\"reason\":\"swap\"}\n")]
    [InlineData("{\"run\":\"20261118T200000Z\",\"result\":\"update_failed\",\"version\":\"1.2.9\",\"reason\":\"check\"}\n")]
    public void OtherFailedResults_DoNotSkip(string result)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using UpdaterHome home = new();
        home.AddRelease("1.3.0", "1.3.0");
        home.SetRedirect(Releases + "/tag/v1.3.0");
        home.SetResult(result);

        Assert.Matches(LinePattern("updated v1.2.0 v1.3.0"), home.RunOnce());
    }

    [Theory]
    [InlineData(null)]
    [InlineData(Releases + "/tag/1.3.0")]
    [InlineData("https://example.com/releases/tag/v1.3.0")]
    public void NoLatestRelease_IsLatestFailedWithNoResult(string? redirect)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using UpdaterHome home = new();
        if (redirect is not null)
        {
            home.SetRedirect(redirect);
        }

        AssertNothingDone(home, "latest_failed", " v1.2.0", curlCalls: 1);
    }

    [Fact]
    public void FailedDownload_IsDownloadFailedWithNoResult()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using UpdaterHome home = new();
        home.SetRedirect(Releases + "/tag/v1.3.0");

        AssertNothingDone(home, "download_failed", " v1.2.0 v1.3.0", curlCalls: 2);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailedCheck_IsCheckFailedWithACheckResultAndTheServerUntouched(bool symlinkEntry)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using UpdaterHome home = new();
        home.AddRelease("1.3.0", "1.3.0", badChecksum: !symlinkEntry, symlinkEntry: symlinkEntry);
        home.SetRedirect(Releases + "/tag/v1.3.0");
        IReadOnlyList<string> before = home.Snapshot("server");

        Assert.Matches(LinePattern("check_failed v1.2.0 v1.3.0"), home.RunOnce());

        Assert.Empty(home.LaunchctlCalls());
        Assert.Equal(before, home.Snapshot("server"));
        Assert.False(Directory.Exists(home.Previous));
        AssertResult(home, "\"result\":\"update_failed\",\"version\":\"1.3.0\",\"reason\":\"check\"");
        Assert.Empty(home.Leftovers());
    }

    [Theory]
    [InlineData("{\"autoUpdate\":true}")]
    [InlineData("{\"autoUpdate\":true,\"channel\":\"stable\"}")]
    [InlineData("{\"autoUpdate\":true,\"channel\":\"stable\"}\n")]
    public void StableChannel_IgnoresDevelopBuildsAndNeverAsksForTheList(string settings)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using UpdaterHome home = new();
        home.SetSettings(settings);
        home.SetList("[{\"tag_name\":\"v1.2.1-develop.9\"},{\"tag_name\":\"v1.2.0\"}]");
        home.AddRelease("1.2.1-develop.9", "1.2.1-develop.9");
        home.SetRedirect(Releases + "/tag/v1.2.0");

        AssertNothingDone(home, "current", " v1.2.0 v1.2.0", curlCalls: 1);
        Assert.DoesNotContain(home.CurlCalls(), call => call.Contains("api.github.com", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("v1.2.1-develop.3", "v1.2.1", "updated")]
    [InlineData("v1.2.1-develop.3", "v1.2.0", "current")]
    public void StableChannel_ComparesAnInstalledDevelopBuildBySemVer(string installed, string latest, string word)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using UpdaterHome home = new(installed);
        home.AddRelease(latest[1..], latest[1..]);
        home.SetRedirect(Releases + "/tag/" + latest);

        Assert.Matches(LinePattern(word + " " + installed + " " + latest), home.RunOnce());
    }

    [Fact]
    public void DevelopChannel_IgnoresATagWithALeadingZeroPrereleaseIdentifier()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using UpdaterHome home = new();
        home.SetSettings(Develop);
        home.SetList("[{\"tag_name\":\"v1.2.5-01\"},{\"tag_name\":\"v1.2.0\"}]");
        home.AddRelease("1.2.5-01", "1.2.5-01");
        home.SetRedirect(Releases + "/tag/v1.2.0");

        AssertNothingDone(home, "current", " v1.2.0 v1.2.0", curlCalls: 1);
    }

    [Fact]
    public void DevelopChannel_PicksDevelopTenOverNineAndInstallsIt()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using UpdaterHome home = new();
        home.SetSettings(Develop);
        home.SetList("[\n  {\n    \"tag_name\": \"v1.2.1-develop.9\",\n    \"body\": \"see \\\"tag_name\\\":\\\"v9.9.9\\\"\"\n  },\n"
            + "  {\"tag_name\":\"v1.2.1-develop.10\",\"prerelease\":true},{\"tag_name\":\"v1.2.0\"},"
            + "{\"tag_name\":\"v9.0.0-rc.1\"},{\"tag_name\":\"v9.0.0-develop.01\"},{\"tag_name\":\"v9.0.0+b\"},"
            + "{\"tag_name\":\"9.0.0\"},{\"tag_name\":\"v9.0.0-develop.1.1\"},{\"tag_name\":\"v9.0.0-develop.*\"}\n]\n");
        home.AddRelease("1.2.1-develop.10", "1.2.1-develop.10");

        string line = home.RunOnce();

        Assert.Matches(LinePattern("updated v1.2.0 v1.2.1-develop.10"), line);
        Assert.Equal("v1.2.1-develop.10\n", File.ReadAllText(home.Marker));
        AssertResult(home, "\"result\":\"updated\",\"version\":\"1.2.1-develop.10\",\"previous\":\"1.2.0\"");
        IReadOnlyList<string> curl = home.CurlCalls();
        Assert.Equal(ListCall, curl[0]);
        Assert.Single(curl, call => call.Contains("api.github.com", StringComparison.Ordinal));
        Assert.DoesNotContain(curl, call => call.Contains("-w ", StringComparison.Ordinal));
        Assert.DoesNotContain("-L", curl[0], StringComparison.Ordinal);
        Assert.Contains(curl, call => call.EndsWith(
            Releases + "/download/v1.2.1-develop.10/controlroom-1.2.1-develop.10-osx-arm64.tar.gz", StringComparison.Ordinal));
        Assert.Contains(curl, call => call.EndsWith(Releases + "/download/v1.2.1-develop.10/SHA256SUMS", StringComparison.Ordinal));
        AssertNoToken(home);
        Assert.Empty(home.Leftovers());
    }

    [Fact]
    public void DevelopChannel_MovesFromADevelopBuildToTheProperRelease()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using UpdaterHome home = new("v1.2.1-develop.10");
        home.SetSettings(Develop);
        home.SetList("[{\"tag_name\":\"v1.2.1-develop.10\"},{\"tag_name\":\"v1.2.1\"},{\"tag_name\":\"v1.2.0\"}]");
        home.AddRelease("1.2.1", "1.2.1");

        Assert.Matches(LinePattern("updated v1.2.1-develop.10 v1.2.1"), home.RunOnce());
        Assert.Equal("v1.2.1\n", File.ReadAllText(home.Marker));
    }

    [Theory]
    [InlineData("v1.2.1-develop.10", "[{\"tag_name\":\"v1.2.1-develop.9\"},{\"tag_name\":\"v1.2.0\"}]", "v1.2.1-develop.9")]
    [InlineData("v1.2.1-develop.10", "[{\"tag_name\":\"v1.2.1-develop.10\"}]", "v1.2.1-develop.10")]
    [InlineData("v1.2.1", "[{\"tag_name\":\"v1.2.1-develop.10\"}]", "v1.2.1-develop.10")]
    public void DevelopChannel_NothingNewer_IsCurrent(string installed, string list, string best)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using UpdaterHome home = new(installed);
        home.SetSettings(Develop);
        home.SetList(list);

        AssertNothingDone(home, "current", " " + installed + " " + best, curlCalls: 1);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("[]")]
    [InlineData("{\"message\":\"rate limited\"}")]
    [InlineData("[{\"tag_name\":\"v9.0.0-rc.1\"},{\"tag_name\":\"v9.0.0+b\"}]")]
    [InlineData("too-big")]
    public void DevelopChannel_NoListOrNoValidTag_IsLatestFailed(string? list)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using UpdaterHome home = new();
        home.SetSettings(Develop);
        if (list is not null)
        {
            home.SetList(list == "too-big" ? "[{\"tag_name\":\"v9.0.0\"}]".PadRight(1048577) : list);
        }

        AssertNothingDone(home, "latest_failed", " v1.2.0", curlCalls: 1);
        Assert.Equal(ListCall, Assert.Single(home.CurlCalls()));
    }

    [Fact]
    public void DevelopChannel_BadChecksum_IsCheckFailedAndTheServerUntouched()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using UpdaterHome home = new();
        home.SetSettings(Develop);
        home.SetList("[{\"tag_name\":\"v1.2.1-develop.4\"}]");
        home.AddRelease("1.2.1-develop.4", "1.2.1-develop.4", badChecksum: true);
        IReadOnlyList<string> before = home.Snapshot("server");

        Assert.Matches(LinePattern("check_failed v1.2.0 v1.2.1-develop.4"), home.RunOnce());

        Assert.Empty(home.LaunchctlCalls());
        Assert.Equal(before, home.Snapshot("server"));
        Assert.Equal("v1.2.0\n", File.ReadAllText(home.Marker));
        AssertResult(home, "\"result\":\"update_failed\",\"version\":\"1.2.1-develop.4\",\"reason\":\"check\"");
        Assert.Empty(home.Leftovers());
    }

    [Fact]
    public void DevelopChannel_NoAnswerFromTheNewBuild_RollsBack()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using UpdaterHome home = new();
        home.SetSettings(Develop);
        home.SetList("[{\"tag_name\":\"v1.2.1-develop.4\"}]");
        home.AddRelease("1.2.1-develop.4", "9.9.9");

        Assert.Matches(LinePattern("rolled_back v1.2.0 v1.2.1-develop.4"), home.RunOnce());

        Assert.Equal("{\"version\":\"1.2.0\"}", File.ReadAllText(Path.Combine(home.Server, "health")));
        Assert.Equal("v1.2.0\n", File.ReadAllText(home.Marker));
        AssertResult(home, "\"result\":\"update_failed\",\"version\":\"1.2.1-develop.4\",\"reason\":\"no_answer\"");
        AssertNoToken(home);
        Assert.Empty(home.Leftovers());
    }

    [Fact]
    public void DevelopChannel_BuildThatFailedItsCheck_IsSkipped()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using UpdaterHome home = new();
        home.SetSettings(Develop);
        home.SetList("[{\"tag_name\":\"v1.2.1-develop.4\"}]");
        home.SetResult("{\"run\":\"20261118T200000Z\",\"result\":\"update_failed\",\"version\":\"1.2.1-develop.4\","
            + "\"reason\":\"check\"}\n");

        AssertNothingDone(home, "skipped_failed", " v1.2.0 v1.2.1-develop.4", curlCalls: 1);
    }

    [Fact]
    public void LingeringServiceAndOneFailedBootstrap_AreWaitedForAndRetriedBeforeTheHealthCheck()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using UpdaterHome home = new();
        home.AddRelease("1.3.0", "1.3.0");
        home.SetRedirect(Releases + "/tag/v1.3.0");
        home.SetLinger(3);
        home.FailBootstraps(1);

        Assert.Matches(LinePattern("updated v1.2.0 v1.3.0"), home.RunOnce());

        AssertResult(home, "\"result\":\"updated\",\"version\":\"1.3.0\",\"previous\":\"1.2.0\"");
        string print = "launchctl print " + home.Service;
        string bootstrap = "launchctl bootstrap " + home.Domain + " " + home.ServerPlist;
        Assert.Equal(
            ["launchctl bootout " + home.Service, print, "sleep 0.5", print, "sleep 0.5", print, "sleep 0.5", print,
                bootstrap, "sleep 1", bootstrap, "health", print],
            home.Events());
        Assert.Equal(["0.5", "0.5", "0.5", "1"], home.SleepCalls());
        Assert.Equal("v1.3.0\n", File.ReadAllText(home.Marker));
    }

    [Fact]
    public void LingeringServiceWithAFirstTimeBootstrap_SleepsOnlyWhileItIsStillShown()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using UpdaterHome home = new();
        home.AddRelease("1.3.0", "1.3.0");
        home.SetRedirect(Releases + "/tag/v1.3.0");
        home.SetLinger(2);

        Assert.Matches(LinePattern("updated v1.2.0 v1.3.0"), home.RunOnce());

        Assert.Equal(["0.5", "0.5"], home.SleepCalls());
        Assert.Single(home.LaunchctlCalls(), call => call.StartsWith("bootstrap ", StringComparison.Ordinal));
    }

    [Fact]
    public void ServiceThatNeverGoes_IsWaitedForThirtyPollsThenBootstrappedAnyway()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using UpdaterHome home = new();
        home.AddRelease("1.3.0", "1.3.0");
        home.SetRedirect(Releases + "/tag/v1.3.0");
        home.SetLinger(1000);

        Assert.Matches(LinePattern("updated v1.2.0 v1.3.0"), home.RunOnce());

        Assert.Equal(Enumerable.Repeat("0.5", 30), home.SleepCalls());
        List<string> expected = ["launchctl bootout " + home.Service];
        for (int poll = 0; poll < 30; poll++)
        {
            expected.Add("launchctl print " + home.Service);
            expected.Add("sleep 0.5");
        }

        expected.Add("launchctl bootstrap " + home.Domain + " " + home.ServerPlist);
        Assert.Equal(expected, home.Events().Take(expected.Count));
    }

    [Fact]
    public void NewServerThatNeverBootstraps_IsBootstrapFailedWithoutAHealthPollForIt()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using UpdaterHome home = new();
        home.AddRelease("1.3.0", "1.3.0");
        home.SetRedirect(Releases + "/tag/v1.3.0");
        home.FailBootstraps(6);

        Assert.Matches(LinePattern("bootstrap_failed v1.2.0 v1.3.0"), home.RunOnce());

        AssertResult(home, "\"result\":\"update_failed\",\"version\":\"1.3.0\",\"reason\":\"bootstrap_failed\"");
        string bootstrap = "bootstrap " + home.Domain + " " + home.ServerPlist;
        Assert.Equal(
            ["bootout " + home.Service, .. Enumerable.Repeat(bootstrap, 6), "bootout " + home.Service, bootstrap],
            home.LaunchctlCalls());
        Assert.Equal(Enumerable.Repeat("1", 5), home.SleepCalls());
        List<string> events = [.. home.Events()];
        Assert.Single(events, call => call == "health");
        Assert.True(events.IndexOf("health") > events.LastIndexOf("launchctl bootout " + home.Service));
        Assert.Equal("{\"version\":\"1.2.0\"}", File.ReadAllText(Path.Combine(home.Server, "health")));
        Assert.False(Directory.Exists(home.Previous));
        Assert.Equal("v1.2.0\n", File.ReadAllText(home.Marker));
        Assert.Empty(home.Leftovers());
    }

    [Fact]
    public void BootstrapFailedRun_IsNotSkippedAndTheNextRunTriesTheSwapAgain()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using UpdaterHome home = new();
        home.AddRelease("1.3.0", "1.3.0");
        home.SetRedirect(Releases + "/tag/v1.3.0");
        home.FailBootstraps(6);
        Assert.Matches(LinePattern("bootstrap_failed v1.2.0 v1.3.0"), home.RunOnce());

        Assert.Matches(LinePattern("updated v1.2.0 v1.3.0"), home.RunOnce());

        AssertResult(home, "\"result\":\"updated\",\"version\":\"1.3.0\",\"previous\":\"1.2.0\"");
        Assert.Equal("v1.3.0\n", File.ReadAllText(home.Marker));
    }

    [Fact]
    public void OldServerThatNeverBootstrapsAfterABootstrapFailure_IsRollbackFailedWithoutHealthPolls()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using UpdaterHome home = new();
        home.AddRelease("1.3.0", "1.3.0");
        home.SetRedirect(Releases + "/tag/v1.3.0");
        home.FailBootstraps(12);

        Assert.Matches(LinePattern("rollback_failed v1.2.0 v1.3.0"), home.RunOnce());

        AssertResult(home, "\"result\":\"update_failed\",\"version\":\"1.3.0\",\"reason\":\"rollback_failed\"");
        Assert.Equal(12, home.LaunchctlCalls().Count(call => call.StartsWith("bootstrap ", StringComparison.Ordinal)));
        Assert.DoesNotContain(HealthCall, home.CurlCalls());
    }

    [Fact]
    public void OldServerThatNeverBootstrapsAfterNoAnswer_IsRollbackFailedWithoutPollingItsHealth()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using UpdaterHome home = new();
        home.AddRelease("1.3.0", "9.9.9");
        home.SetRedirect(Releases + "/tag/v1.3.0");
        home.FailBootstrapsPerBootout(0, 6);

        Assert.Matches(LinePattern("rollback_failed v1.2.0 v1.3.0"), home.RunOnce());

        AssertResult(home, "\"result\":\"update_failed\",\"version\":\"1.3.0\",\"reason\":\"rollback_failed\"");
        Assert.Equal(15, home.CurlCalls().Count(call => call == HealthCall));
        IReadOnlyList<string> afterRollback = [.. home.Events().Reverse().TakeWhile(call => call != "launchctl bootout " + home.Service)];
        Assert.Equal(6, afterRollback.Count(call => call.StartsWith("launchctl bootstrap ", StringComparison.Ordinal)));
        Assert.DoesNotContain("health", afterRollback);
    }

    [Fact]
    public void OldServerThatNeverBootstrapsAfterAFailedRename_IsRollbackFailedWithoutHealthPolls()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using UpdaterHome home = new();
        home.AddRelease("1.3.0", "1.3.0");
        home.SetRedirect(Releases + "/tag/v1.3.0");
        home.FailMoveFrom("/controlroom-1.3.0-osx-arm64");
        home.FailBootstraps(6);

        Assert.Matches(LinePattern("rollback_failed v1.2.0 v1.3.0"), home.RunOnce());

        AssertResult(home, "\"result\":\"update_failed\",\"version\":\"1.3.0\",\"reason\":\"rollback_failed\"");
        Assert.Equal(6, home.LaunchctlCalls().Count(call => call.StartsWith("bootstrap ", StringComparison.Ordinal)));
        Assert.DoesNotContain(HealthCall, home.CurlCalls());
        Assert.Equal("{\"version\":\"1.2.0\"}", File.ReadAllText(Path.Combine(home.Server, "health")));
    }

    [Fact]
    public void RollbackAfterNoAnswer_WaitsAfterEveryBootoutAndRetriesEveryBootstrap()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using UpdaterHome home = new();
        home.AddRelease("1.3.0", "9.9.9");
        home.SetRedirect(Releases + "/tag/v1.3.0");
        home.SetLinger(1);
        home.FailBootstrapsPerBootout(1, 1);

        Assert.Matches(LinePattern("rolled_back v1.2.0 v1.3.0"), home.RunOnce());

        AssertResult(home, "\"result\":\"update_failed\",\"version\":\"1.3.0\",\"reason\":\"no_answer\"");
        AssertWaitAndRetryAfterEveryBootout(home, bootouts: 2);
        Assert.Equal("{\"version\":\"1.2.0\"}", File.ReadAllText(Path.Combine(home.Server, "health")));
    }

    [Fact]
    public void SwapFailedAfterAFailedRename_WaitsAfterTheBootoutAndRetriesTheBootstrap()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using UpdaterHome home = new();
        home.AddRelease("1.3.0", "1.3.0");
        home.SetRedirect(Releases + "/tag/v1.3.0");
        home.FailMoveFrom("/controlroom-1.3.0-osx-arm64");
        home.SetLinger(1);
        home.FailBootstraps(1);

        Assert.Matches(LinePattern("swap_failed v1.2.0 v1.3.0"), home.RunOnce());

        AssertResult(home, "\"result\":\"update_failed\",\"version\":\"1.3.0\",\"reason\":\"swap\"");
        AssertWaitAndRetryAfterEveryBootout(home, bootouts: 1);
        Assert.Equal("{\"version\":\"1.2.0\"}", File.ReadAllText(Path.Combine(home.Server, "health")));
    }

    [Fact]
    public void SwapFailedAfterAFailedMarkerWrite_WaitsAfterEveryBootoutAndRetriesEveryBootstrap()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using UpdaterHome home = new();
        home.AddRelease("1.3.0", "1.3.0");
        home.SetRedirect(Releases + "/tag/v1.3.0");
        home.FailMoveTo("/installed-release");
        home.SetLinger(1);
        home.FailBootstrapsPerBootout(1, 1);

        Assert.Matches(LinePattern("swap_failed v1.2.0 v1.3.0"), home.RunOnce());

        AssertResult(home, "\"result\":\"update_failed\",\"version\":\"1.3.0\",\"reason\":\"swap\"");
        AssertWaitAndRetryAfterEveryBootout(home, bootouts: 2);
    }

    /// <summary>With one lingering <c>print</c> and one failing bootstrap per bootout: every bootout is followed by
    /// print, sleep 0.5, print (gone), then bootstrap, sleep 1, bootstrap.</summary>
    private static void AssertWaitAndRetryAfterEveryBootout(UpdaterHome home, int bootouts)
    {
        string bootout = "launchctl bootout " + home.Service;
        string print = "launchctl print " + home.Service;
        string bootstrap = "launchctl bootstrap " + home.Domain + " " + home.ServerPlist;
        IReadOnlyList<string> events = home.Events();
        List<int> starts = [.. Enumerable.Range(0, events.Count).Where(index => events[index] == bootout)];
        Assert.Equal(bootouts, starts.Count);
        foreach (int start in starts)
        {
            Assert.Equal(
                [bootout, print, "sleep 0.5", print, bootstrap, "sleep 1", bootstrap],
                events.Skip(start).Take(7));
        }
    }

    /// <summary>One run that must log <paramref name="word"/> (plus <paramref name="details"/>) and change nothing:
    /// no launchctl call, the given number of curl calls, no result, HOME as before apart from the fakes' logs.</summary>
    private static void AssertNothingDone(UpdaterHome home, string word, string details, int curlCalls = 0)
    {
        IReadOnlyList<string> before = home.Snapshot("");

        string line = home.RunOnce();

        Assert.Matches(LinePattern(word + details), line);
        Assert.Empty(home.LaunchctlCalls());
        Assert.Equal(curlCalls, home.CurlCalls().Count);
        Assert.Empty(home.SleepCalls());
        Assert.Equal(before, home.Snapshot(""));
    }

    private static void AssertResult(UpdaterHome home, string fields)
    {
        string text = File.ReadAllText(home.Result);
        Assert.Matches("^\\{\"run\":\"[0-9]{8}T[0-9]{6}Z\"," + Regex.Escape(fields) + "\\}\n\\z", text);
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(home.Result));
        Assert.NotNull(UpdateResult.Parse(File.ReadAllBytes(home.Result)));
    }

    private static void AssertNoToken(UpdaterHome home)
    {
        foreach (string call in home.CurlCalls())
        {
            Assert.DoesNotContain(Token, call, StringComparison.Ordinal);
            Assert.DoesNotContain("Authorization", call, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("access-token", call, StringComparison.Ordinal);
        }

        Assert.DoesNotContain(Token, home.CurlInput(), StringComparison.Ordinal);
    }

    private static string LinePattern(string rest)
    {
        return "^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}Z " + Regex.Escape(rest) + "\n\\z";
    }

    /// <summary>The text of <c>name() { … }</c> up to its closing brace at the start of a line.</summary>
    private static string Function(string script, string name)
    {
        Match match = Regex.Match(script.Replace("\r\n", "\n", StringComparison.Ordinal),
            "^" + Regex.Escape(name) + "\\(\\) \\{\n.*?^\\}\n", RegexOptions.Multiline | RegexOptions.Singleline);
        Assert.True(match.Success, name + " not found");
        return match.Value;
    }

    private sealed record ScriptRun(int ExitCode, string Output, string Error);

    /// <summary>A temp <c>HOME</c> with an installed release <c>v1.2.0</c> (server folder answering
    /// <c>{"version":"1.2.0"}</c>, server plist, marker, <c>autoUpdate</c> on, a token file), a factory root with an
    /// empty jobs folder and the fakes in <c>bin/</c>; deleted on dispose.</summary>
    private sealed class UpdaterHome : IDisposable
    {
        private static readonly TimeSpan ProcessTimeout = TimeSpan.FromSeconds(60);

        /// <summary>Logs its arguments (also to <c>order.log</c>). <c>bootout</c> unloads the server, copies
        /// <c>$HOME/linger</c> to <c>linger-left</c> and pops the first line of <c>bootstrap-fails-per-bootout</c> into
        /// <c>bootstrap-fails</c>. <c>bootstrap</c> fails (stderr, exit 5) while the <c>bootstrap-fails</c> counter is
        /// above 0 (counting it down), else loads the server. <c>print</c> shows the loaded server; after a bootout it
        /// still shows it while the <c>linger-left</c> counter is above 0 (counting it down), else exits 113.</summary>
        private const string FakeLaunchctl =
            "#!/bin/sh\n"
            + "printf '%s\\n' \"$*\" >> \"$HOME/calls.log\"\n"
            + "printf 'launchctl %s\\n' \"$*\" >> \"$HOME/order.log\"\n"
            + "count() { if [ -f \"$HOME/$1\" ]; then cat \"$HOME/$1\"; else echo 0; fi; }\n"
            + "case $1 in\n"
            + "  bootstrap)\n"
            + "    n=$(count bootstrap-fails)\n"
            + "    if [ \"$n\" -gt 0 ]; then\n"
            + "      echo $((n - 1)) > \"$HOME/bootstrap-fails\"\n"
            + "      echo 'Bootstrap failed: 5: Input/output error' >&2; exit 5\n"
            + "    fi\n"
            + "    : > \"$HOME/loaded\" ;;\n"
            + "  bootout)\n"
            + "    rm -f \"$HOME/loaded\"\n"
            + "    if [ -f \"$HOME/linger\" ]; then cat \"$HOME/linger\" > \"$HOME/linger-left\"; fi\n"
            + "    plan=\"$HOME/bootstrap-fails-per-bootout\"\n"
            + "    if [ -s \"$plan\" ]; then\n"
            + "      head -n 1 \"$plan\" > \"$HOME/bootstrap-fails\"\n"
            + "      tail -n +2 \"$plan\" > \"$plan.rest\"; cat \"$plan.rest\" > \"$plan\"; rm -f \"$plan.rest\"\n"
            + "    fi ;;\n"
            + "esac\n"
            + "if [ \"$1\" = print ]; then\n"
            + "  if [ -f \"$HOME/loaded\" ] && [ ! -f \"$HOME/not-running\" ]; then\n"
            + "    printf '\\tstate = running\\n\\tpid = 4242\\n\\tendpoints = {\\n\\t\\tpid = 7\\n\\t}\\n'; exit 0\n"
            + "  fi\n"
            + "  if [ -f \"$HOME/loaded\" ]; then printf '\\tstate = waiting\\n'; exit 0; fi\n"
            + "  n=$(count linger-left)\n"
            + "  if [ \"$n\" -gt 0 ]; then echo $((n - 1)) > \"$HOME/linger-left\"; printf '\\tstate = exiting\\n'; exit 0; fi\n"
            + "  exit 113\n"
            + "fi\n"
            + "exit 0\n";

        /// <summary>Logs its arguments; prints <c>$HOME/lsof-out</c> when set, else the fake server pid 4242 while
        /// loaded, else nothing with exit 1 (like the real <c>lsof</c>).</summary>
        private const string FakeLsof =
            "#!/bin/sh\n"
            + "printf '%s\\n' \"$*\" >> \"$HOME/lsof.log\"\n"
            + "if [ -f \"$HOME/lsof-out\" ]; then cat \"$HOME/lsof-out\"; exit 0; fi\n"
            + "if [ -f \"$HOME/loaded\" ]; then echo 4242; exit 0; fi\n"
            + "exit 1\n";

        private const string FakeSleep =
            "#!/bin/sh\n"
            + "printf '%s\\n' \"$*\" >> \"$HOME/sleep.log\"\n"
            + "printf 'sleep %s\\n' \"$*\" >> \"$HOME/order.log\"\n"
            + "exit 0\n";

        /// <summary>Fails (exit 1) when the source ends with the text in <c>$HOME/mv-fail</c> or the target with the
        /// text in <c>$HOME/mv-fail-to</c>, else runs the real <c>mv</c>.</summary>
        private const string FakeMv =
            "#!/bin/sh\n"
            + "if [ -f \"$HOME/mv-fail-to\" ]; then\n"
            + "  suffix=$(cat \"$HOME/mv-fail-to\")\n"
            + "  for a in \"$@\"; do last=$a; done\n"
            + "  case $last in *\"$suffix\") exit 1 ;; esac\n"
            + "fi\n"
            + "if [ -f \"$HOME/mv-fail\" ]; then\n"
            + "  suffix=$(cat \"$HOME/mv-fail\")\n"
            + "  for a in \"$@\"; do case $a in *\"$suffix\") exit 1 ;; esac; break; done\n"
            + "  for a in \"$@\"; do case $a in -*) ;; *) case $a in *\"$suffix\") exit 1 ;; esac; break ;; esac; done\n"
            + "fi\n"
            + "exec /bin/mv \"$@\"\n";

        /// <summary>Logs its arguments as one line and its stdin; <c>/health</c> answers with the server folder's
        /// <c>health</c> file while loaded (else exit 7, like a refused connection); <c>-w</c> prints
        /// <c>$HOME/redirect</c>; the release list URL prints <c>$HOME/list</c>; anything else copies <c>$HOME/release/&lt;name&gt;</c> to <c>-o</c>.</summary>
        private const string FakeCurl =
            "#!/bin/sh\n"
            + "printf '%s\\n' \"$*\" >> \"$HOME/curl.log\"\n"
            + "cat >> \"$HOME/curl-stdin.log\"\n"
            + "out=\"\"; url=\"\"; w=no; max=\"\"\n"
            + "while [ $# -gt 0 ]; do\n"
            + "  case $1 in -o) out=$2; shift ;; -w) w=yes; shift ;; --max-filesize) max=$2; shift ;;"
            + " http://* | https://*) url=$1 ;; esac\n"
            + "  shift\n"
            + "done\n"
            + "if [ \"$url\" = http://127.0.0.1:8100/health ]; then\n"
            + "  echo health >> \"$HOME/order.log\"\n"
            + "  health=\"$HOME/Library/Application Support/ControlRoom/server/health\"\n"
            + "  if [ -f \"$HOME/loaded\" ] && [ -f \"$health\" ]; then cat \"$health\"; exit 0; fi\n"
            + "  exit 7\n"
            + "fi\n"
            + "if [ \"$url\" = '" + ListUrl + "' ]; then\n"
            + "  if [ ! -f \"$HOME/list\" ]; then exit 22; fi\n"
            + "  size=$(wc -c < \"$HOME/list\")\n"
            + "  if [ -n \"$max\" ] && [ $((size + 0)) -gt \"$max\" ]; then exit 63; fi\n"
            + "  cat \"$HOME/list\"; exit 0\n"
            + "fi\n"
            + "if [ $w = yes ]; then\n"
            + "  if [ -f \"$HOME/redirect\" ]; then cat \"$HOME/redirect\"; exit 0; fi\n"
            + "  exit 22\n"
            + "fi\n"
            + "name=${url##*/}\n"
            + "if [ -f \"$HOME/release/$name\" ]; then\n"
            + "  size=$(wc -c < \"$HOME/release/$name\")\n"
            + "  if [ -n \"$max\" ] && [ $((size + 0)) -gt \"$max\" ]; then exit 63; fi\n"
            + "  cp \"$HOME/release/$name\" \"$out\"; exit 0\n"
            + "fi\n"
            + "exit 22\n";

        private static readonly string[] HarnessFiles =
            ["calls.log", "curl.log", "curl-stdin.log", "sleep.log", "lsof.log", "loaded", "redirect", "mv-fail",
                "mv-fail-to", "lsof-out", "not-running", "list", "order.log", "linger", "linger-left", "bootstrap-fails",
                "bootstrap-fails-per-bootout"];

        public UpdaterHome()
            : this("v1.2.0")
        {
        }

        public UpdaterHome(string installed)
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), "controlroom-updater-" + Guid.NewGuid().ToString("N"));
            string bin = System.IO.Path.Combine(Path, "bin");
            Directory.CreateDirectory(bin);
            WriteExecutable(System.IO.Path.Combine(bin, "launchctl"), FakeLaunchctl);
            WriteExecutable(System.IO.Path.Combine(bin, "curl"), FakeCurl);
            WriteExecutable(System.IO.Path.Combine(bin, "sleep"), FakeSleep);
            WriteExecutable(System.IO.Path.Combine(bin, "mv"), FakeMv);
            WriteExecutable(System.IO.Path.Combine(bin, "lsof"), FakeLsof);
            Directory.CreateDirectory(System.IO.Path.Combine(Root, "factory", "jobs"));

            Directory.CreateDirectory(Support);
            File.SetUnixFileMode(Support, AccessToken.FolderMode);
            Directory.CreateDirectory(Server);
            WriteExecutable(System.IO.Path.Combine(Server, "ControlRoom.Server"), "#!/bin/sh\nexit 0\n");
            File.WriteAllText(System.IO.Path.Combine(Server, "health"), "{\"version\":\"" + installed[1..] + "\"}");
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(ServerPlist)!);
            File.WriteAllText(ServerPlist, "<plist/>\n");
            SetMarker(installed + "\n");
            SetSettings("{\"autoUpdate\":true}\n");
            WritePrivate(TokenFile, Token);
            Domain = "gui/" + RunProcess("/usr/bin/id", ["-u"]).Output.Trim();
        }

        public string Path { get; }

        public string Root => System.IO.Path.Combine(Path, "root");

        public string Support => System.IO.Path.Combine(Path, "Library", "Application Support", "ControlRoom");

        public string Server => System.IO.Path.Combine(Support, "server");

        public string Previous => System.IO.Path.Combine(Support, "server.previous");

        public string Marker => System.IO.Path.Combine(Support, "installed-release");

        public string Settings => System.IO.Path.Combine(Support, "settings.json");

        public string Result => System.IO.Path.Combine(Support, "update-result.json");

        public string Lock => System.IO.Path.Combine(Support, ".update.lock");

        public string TokenFile => System.IO.Path.Combine(Support, "access-token");

        public string ServerPlist =>
            System.IO.Path.Combine(Path, "Library", "LaunchAgents", "local.controlroom.server.plist");

        public string Domain { get; }

        public string Service => Domain + "/local.controlroom.server";

        public static string RepoFolder()
        {
            DirectoryInfo? current = new(AppContext.BaseDirectory);
            while (current is not null && !File.Exists(System.IO.Path.Combine(current.FullName, "ControlRoom.slnx")))
            {
                current = current.Parent;
            }

            Assert.NotNull(current);
            return current.FullName;
        }

        public static string ScriptPath()
        {
            return System.IO.Path.Combine(RepoFolder(), "scripts", "updater.sh");
        }

        /// <summary>Runs the updater once; it must exit 0, write nothing to stderr and print one line, which is
        /// returned.</summary>
        public string RunOnce()
        {
            Dictionary<string, string> environment = new()
            {
                ["HOME"] = Path,
                ["PATH"] = System.IO.Path.Combine(Path, "bin") + ":/usr/bin:/bin:/usr/sbin:/sbin",
            };
            ScriptRun run = RunProcess("/bin/sh", [ScriptPath(), "--factory-root", Root], environment);
            Assert.Equal(0, run.ExitCode);
            Assert.Equal("", run.Error);
            Assert.Single(run.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries));
            return run.Output;
        }

        public void SetSettings(string? content)
        {
            if (content is null)
            {
                File.Delete(Settings);
                return;
            }

            WritePrivate(Settings, content);
        }

        public void SetMarker(string? content)
        {
            if (content is null)
            {
                File.Delete(Marker);
                return;
            }

            WritePrivate(Marker, content);
        }

        public void SetResult(string content)
        {
            WritePrivate(Result, content);
        }

        public void SetRedirect(string answer)
        {
            File.WriteAllText(System.IO.Path.Combine(Path, "redirect"), answer);
        }

        /// <summary>What the fake <c>curl</c> answers for the develop channel's release list.</summary>
        public void SetList(string json)
        {
            File.WriteAllText(System.IO.Path.Combine(Path, "list"), json);
        }

        public void FailMoveFrom(string suffix)
        {
            File.WriteAllText(System.IO.Path.Combine(Path, "mv-fail"), suffix);
        }

        public void FailMoveTo(string suffix)
        {
            File.WriteAllText(System.IO.Path.Combine(Path, "mv-fail-to"), suffix);
        }

        /// <summary>launchd reports the server loaded but not running (<c>state = waiting</c>).</summary>
        public void ServerNotRunning()
        {
            File.WriteAllText(System.IO.Path.Combine(Path, "not-running"), "");
        }

        /// <summary>After each bootout, <c>launchctl print</c> still shows the server for <paramref name="calls"/> more
        /// calls.</summary>
        public void SetLinger(int calls)
        {
            File.WriteAllText(System.IO.Path.Combine(Path, "linger"), calls.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\n");
        }

        /// <summary>The next <paramref name="count"/> bootstraps fail with exit 5.</summary>
        public void FailBootstraps(int count)
        {
            File.WriteAllText(System.IO.Path.Combine(Path, "bootstrap-fails"), count.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\n");
        }

        /// <summary>The n-th bootout sets the failing-bootstrap counter to the n-th number.</summary>
        public void FailBootstrapsPerBootout(params int[] counts)
        {
            File.WriteAllText(System.IO.Path.Combine(Path, "bootstrap-fails-per-bootout"),
                string.Concat(counts.Select(count => count.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\n")));
        }

        /// <summary>The launchctl, sleep and /health calls in the order they ran.</summary>
        public IReadOnlyList<string> Events()
        {
            return ReadLines("order.log");
        }

        /// <summary>What the fake <c>lsof</c> prints as the listening pids.</summary>
        public void SetListeners(string pids)
        {
            File.WriteAllText(System.IO.Path.Combine(Path, "lsof-out"), pids);
        }

        public void AddRawJob(string id, string content)
        {
            string folder = System.IO.Path.Combine(Root, "factory", "jobs", id);
            Directory.CreateDirectory(folder);
            File.WriteAllText(System.IO.Path.Combine(folder, "state.json"), content);
        }

        public void AddJob(string id, string stage)
        {
            string folder = System.IO.Path.Combine(Root, "factory", "jobs", id);
            Directory.CreateDirectory(folder);
            File.WriteAllText(System.IO.Path.Combine(folder, "state.json"),
                "{\n  \"id\": \"" + id + "\",\n  \"stage\": \"" + stage + "\",\n  \"round\": 1\n}\n");
        }

        /// <summary>Builds <c>release/controlroom-&lt;version&gt;-osx-arm64.tar.gz</c> (with a <c>health</c> file
        /// holding <paramref name="health"/> as the version, or none) and <c>release/SHA256SUMS</c> with the real
        /// <c>/usr/bin/tar</c>.</summary>
        public void AddRelease(string version, string? health, bool badChecksum = false, bool symlinkEntry = false)
        {
            string top = "controlroom-" + version + "-osx-arm64";
            string stage = System.IO.Path.Combine(Path, "stage-" + Guid.NewGuid().ToString("N"));
            string folder = System.IO.Path.Combine(stage, top);
            Directory.CreateDirectory(System.IO.Path.Combine(folder, "wwwroot"));
            WriteExecutable(System.IO.Path.Combine(folder, "ControlRoom.Server"), "#!/bin/sh\nexit 0\n");
            File.WriteAllText(System.IO.Path.Combine(folder, "wwwroot", "index.html"), "<!doctype html>");
            if (health is not null)
            {
                File.WriteAllText(System.IO.Path.Combine(folder, "health"), "{\"version\":\"" + health + "\"}");
            }

            if (symlinkEntry)
            {
                File.CreateSymbolicLink(System.IO.Path.Combine(folder, "wwwroot", "link"), "/etc/hosts");
            }

            string release = System.IO.Path.Combine(Path, "release");
            Directory.CreateDirectory(release);
            string tarball = System.IO.Path.Combine(release, top + ".tar.gz");
            ScriptRun tar = RunProcess(
                "/usr/bin/tar", ["-czf", tarball, "-C", stage, top], new() { ["COPYFILE_DISABLE"] = "1" });
            Assert.True(tar.ExitCode == 0, "tar: " + tar.Error);
            Directory.Delete(stage, recursive: true);
            string hash = badChecksum
                ? new string('0', 64)
                : Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(tarball)));
            File.WriteAllText(System.IO.Path.Combine(release, "SHA256SUMS"), hash + "  " + top + ".tar.gz\n");
        }

        /// <summary>The launchctl calls that change state (<c>print</c> only reads and is left out).</summary>
        public IReadOnlyList<string> LaunchctlCalls()
        {
            return [.. ReadLines("calls.log").Where(call => !call.StartsWith("print ", StringComparison.Ordinal))];
        }

        public IReadOnlyList<string> AllLaunchctlCalls()
        {
            return ReadLines("calls.log");
        }

        public IReadOnlyList<string> LsofCalls()
        {
            return ReadLines("lsof.log");
        }

        public IReadOnlyList<string> CurlCalls()
        {
            return ReadLines("curl.log");
        }

        public IReadOnlyList<string> SleepCalls()
        {
            return ReadLines("sleep.log");
        }

        public string CurlInput()
        {
            string file = System.IO.Path.Combine(Path, "curl-stdin.log");
            return File.Exists(file) ? File.ReadAllText(file) : "";
        }

        /// <summary>Work folders and temp files the updater left in the support folder.</summary>
        public IReadOnlyList<string> Leftovers()
        {
            return [.. Directory.GetFileSystemEntries(Support, ".*").Where(entry => !entry.EndsWith(".update.lock", StringComparison.Ordinal)),
                .. Directory.Exists(Lock) ? [Lock] : Array.Empty<string>()];
        }

        /// <summary>Every file and folder under HOME (or the one support sub folder), apart from the fakes' files,
        /// with its mode and content hash, sorted ordinally.</summary>
        public IReadOnlyList<string> Snapshot(string supportFolder)
        {
            string start = supportFolder.Length == 0 ? Path : System.IO.Path.Combine(Support, supportFolder);
            List<string> entries = [];
            foreach (string entry in Directory.EnumerateFileSystemEntries(start, "*", SearchOption.AllDirectories))
            {
                string relative = System.IO.Path.GetRelativePath(Path, entry);
                if (HarnessFiles.Contains(relative))
                {
                    continue;
                }

                FileInfo info = new(entry);
                string hash = info.Exists && info.LinkTarget is null && File.GetUnixFileMode(entry) != UnixFileMode.None
                    ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(entry)))
                    : info.LinkTarget ?? "";
                entries.Add(relative + " " + File.GetUnixFileMode(entry) + " " + hash);
            }

            entries.Sort(StringComparer.Ordinal);
            return entries;
        }

        public void Dispose()
        {
            if (File.Exists(TokenFile))
            {
                File.SetUnixFileMode(TokenFile, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }

            if (Directory.Exists(Support))
            {
                File.SetUnixFileMode(Support, AccessToken.FolderMode);
            }

            Directory.Delete(Path, recursive: true);
        }

        private static ScriptRun RunProcess(
            string program, IEnumerable<string> arguments, Dictionary<string, string>? environment = null)
        {
            ProcessStartInfo start = new(program)
            {
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            foreach (string argument in arguments)
            {
                start.ArgumentList.Add(argument);
            }

            if (environment is not null)
            {
                start.Environment.Clear();
                foreach (KeyValuePair<string, string> pair in environment)
                {
                    start.Environment[pair.Key] = pair.Value;
                }
            }

            using Process process = Process.Start(start)!;
            process.StandardInput.Close();
            Task<string> output = process.StandardOutput.ReadToEndAsync();
            Task<string> error = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(ProcessTimeout))
            {
                process.Kill(entireProcessTree: true);
                Assert.Fail(program + " did not exit within " + ProcessTimeout);
            }

            process.WaitForExit();
            return new ScriptRun(process.ExitCode, output.Result, error.Result);
        }

        private static void WritePrivate(string path, string content)
        {
            File.WriteAllText(path, content);
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        private static void WriteExecutable(string path, string content)
        {
            File.WriteAllText(path, content);
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        private List<string> ReadLines(string name)
        {
            string file = System.IO.Path.Combine(Path, name);
            return File.Exists(file) ? [.. File.ReadAllLines(file)] : [];
        }
    }
}
