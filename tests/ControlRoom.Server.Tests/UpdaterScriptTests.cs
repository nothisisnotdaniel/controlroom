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
        Assert.Equal(["bootout " + home.Service, "bootstrap " + home.Domain + " " + home.ServerPlist], home.LaunchctlCalls());
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
    [InlineData("{\"autoUpdate\":false}\n", true)]
    [InlineData("{\"autoUpdate\": true}\n", true)]
    [InlineData("{\"autoUpdate\":true}\n\n", true)]
    [InlineData("{\"autoUpdate\":true}\n", false)]
    [InlineData(null, true)]
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

        private const string FakeLaunchctl =
            "#!/bin/sh\n"
            + "printf '%s\\n' \"$*\" >> \"$HOME/calls.log\"\n"
            + "case $1 in bootstrap) : > \"$HOME/loaded\" ;; bootout) rm -f \"$HOME/loaded\" ;; esac\n"
            + "exit 0\n";

        private const string FakeSleep =
            "#!/bin/sh\n"
            + "printf '%s\\n' \"$*\" >> \"$HOME/sleep.log\"\n"
            + "exit 0\n";

        /// <summary>Fails (exit 1) when the source ends with the text in <c>$HOME/mv-fail</c>, else runs the real
        /// <c>mv</c>.</summary>
        private const string FakeMv =
            "#!/bin/sh\n"
            + "if [ -f \"$HOME/mv-fail\" ]; then\n"
            + "  suffix=$(cat \"$HOME/mv-fail\")\n"
            + "  for a in \"$@\"; do case $a in *\"$suffix\") exit 1 ;; esac; break; done\n"
            + "  for a in \"$@\"; do case $a in -*) ;; *) case $a in *\"$suffix\") exit 1 ;; esac; break ;; esac; done\n"
            + "fi\n"
            + "exec /bin/mv \"$@\"\n";

        /// <summary>Logs its arguments as one line and its stdin; <c>/health</c> answers with the server folder's
        /// <c>health</c> file while loaded (else exit 7, like a refused connection); <c>-w</c> prints
        /// <c>$HOME/redirect</c>; anything else copies <c>$HOME/release/&lt;name&gt;</c> to <c>-o</c>.</summary>
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
            + "  health=\"$HOME/Library/Application Support/ControlRoom/server/health\"\n"
            + "  if [ -f \"$HOME/loaded\" ] && [ -f \"$health\" ]; then cat \"$health\"; exit 0; fi\n"
            + "  exit 7\n"
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
            ["calls.log", "curl.log", "curl-stdin.log", "sleep.log", "loaded", "redirect", "mv-fail"];

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

        public void FailMoveFrom(string suffix)
        {
            File.WriteAllText(System.IO.Path.Combine(Path, "mv-fail"), suffix);
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

        public IReadOnlyList<string> LaunchctlCalls()
        {
            return ReadLines("calls.log");
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
