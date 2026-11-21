using System.Text;

namespace ControlRoom.Server.Tests;

/// <summary><c>scripts/updater.sh</c> installing the release's own <c>updater.sh</c> and rewriting its plist with the
/// server swap, and putting both back on every rollback (same harness as the rest of
/// <see cref="UpdaterScriptTests"/>).</summary>
public sealed partial class UpdaterScriptTests
{
    [Fact]
    public void ReleaseUpdater_IsInstalledWithTheNewPlistAndTheOldOnesKeptAsPrevious()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using UpdaterHome home = new();
        home.SetSettings(Develop);
        home.SetList("[{\"tag_name\":\"v1.3.0\"}]\n");
        home.AddRelease("1.3.0", "1.3.0");
        byte[] serverPlist = File.ReadAllBytes(home.ServerPlist);
        byte[] settings = File.ReadAllBytes(home.Settings);

        Assert.Matches(LinePattern("updated v1.2.0 v1.3.0"), home.RunOnce());

        Assert.Equal(ReleaseUpdater, File.ReadAllText(home.UpdaterScript));
        Assert.Equal(ScriptMode, File.GetUnixFileMode(home.UpdaterScript));
        Assert.Equal(ScriptFixtures.UpdaterPlist(home.UpdaterScript, home.Root, home.UpdaterLog),
            File.ReadAllText(home.UpdaterPlist));
        Assert.Equal(PlistMode, File.GetUnixFileMode(home.UpdaterPlist));
        Assert.Equal(InstalledUpdater, File.ReadAllText(home.UpdaterScriptPrevious));
        Assert.Equal(ScriptMode, File.GetUnixFileMode(home.UpdaterScriptPrevious));
        Assert.Equal(InstalledUpdaterPlist, File.ReadAllText(home.UpdaterPlistPrevious));
        Assert.Equal(PlistMode, File.GetUnixFileMode(home.UpdaterPlistPrevious));
        // The previous plist stays in the support folder: launchd never sees a second updater plist.
        Assert.Equal(
            [home.ServerPlist, home.UpdaterPlist],
            Directory.GetFileSystemEntries(Path.GetDirectoryName(home.UpdaterPlist)!).Order(StringComparer.Ordinal));
        // Staged (script and plist) before the bootout; in place before the bootstrap.
        Assert.Equal(["2"], home.StagedAtBootout());
        Assert.Equal(["# release updater"], home.UpdaterAtBootstrap());
        // The server plist, settings.json and server.previous are as without the updater copy.
        Assert.Equal(serverPlist, File.ReadAllBytes(home.ServerPlist));
        Assert.Equal(settings, File.ReadAllBytes(home.Settings));
        Assert.Equal("{\"version\":\"1.2.0\"}", File.ReadAllText(Path.Combine(home.Previous, "health")));
        Assert.Equal("{\"version\":\"1.3.0\"}", File.ReadAllText(Path.Combine(home.Server, "health")));
        Assert.True(File.Exists(Path.Combine(home.Server, "updater.sh")));
        AssertResult(home, "\"result\":\"updated\",\"version\":\"1.3.0\",\"previous\":\"1.2.0\"");
        Assert.Empty(home.Leftovers());
    }

    [Fact]
    public void ReleaseUpdater_IsNeverRunAndTheUpdaterServiceNeverTouched()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using UpdaterHome home = new();
        string ran = Path.Combine(home.Path, "release-updater-ran");
        string script = "#!/bin/sh\n# marker updater\ntouch " + UpdaterHome.Quote(ran) + "\n";
        home.AddRelease("1.3.0", "1.3.0", updater: script);
        home.SetRedirect(Releases + "/tag/v1.3.0");

        Assert.Matches(LinePattern("updated v1.2.0 v1.3.0"), home.RunOnce());

        Assert.Equal(script, File.ReadAllText(home.UpdaterScript));
        Assert.False(File.Exists(ran));
        Assert.NotEmpty(home.AllLaunchctlCalls());
        Assert.All(home.AllLaunchctlCalls(),
            call => Assert.DoesNotContain("local.controlroom.updater", call, StringComparison.Ordinal));
    }

    [Fact]
    public void ReleaseUpdaterAtTheSizeCap_IsInstalled()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using UpdaterHome home = new();
        string script = Padded(262144);
        home.AddRelease("1.3.0", "1.3.0", updater: script);
        home.SetRedirect(Releases + "/tag/v1.3.0");

        Assert.Matches(LinePattern("updated v1.2.0 v1.3.0"), home.RunOnce());
        Assert.Equal(script, File.ReadAllText(home.UpdaterScript));
    }

    [Fact]
    public void ReleaseWithoutAnUpdater_KeepsTheOldOneAndStillUpdates()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using UpdaterHome home = new();
        home.AddRelease("1.3.0", "1.3.0", updater: null);
        home.SetRedirect(Releases + "/tag/v1.3.0");

        AssertUpdaterKept(home, "missing");
        Assert.Equal(["0"], home.StagedAtBootout());
    }

    [Theory]
    [InlineData("bash")]
    [InlineData("no-shebang")]
    [InlineData("crlf")]
    [InlineData("over-cap")]
    [InlineData("empty")]
    [InlineData("folder")]
    public void ReleaseUpdaterThatFailsTheCheck_IsKeptOutAsBadAndTheServerStillUpdates(string fault)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using UpdaterHome home = new();
        switch (fault)
        {
            case "bash":
                home.AddRelease("1.3.0", "1.3.0", updater: "#!/bin/bash\nexit 0\n");
                break;
            case "no-shebang":
                home.AddRelease("1.3.0", "1.3.0", updater: "exit 0\n");
                break;
            case "crlf":
                home.AddRelease("1.3.0", "1.3.0", updater: "#!/bin/sh\r\nexit 0\r\n");
                break;
            case "over-cap":
                home.AddRelease("1.3.0", "1.3.0", updater: Padded(262145));
                break;
            case "empty":
                home.AddRelease("1.3.0", "1.3.0", updater: "");
                break;
            default:
                home.AddRelease("1.3.0", "1.3.0", updaterKind: "folder");
                break;
        }

        home.SetRedirect(Releases + "/tag/v1.3.0");

        AssertUpdaterKept(home, "bad");
    }

    [Fact]
    public void FactoryRootWithALineBreak_KeepsTheReleaseUpdaterOutAsBad()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using UpdaterHome home = new();
        string root = Path.Combine(home.Path, "odd\nroot");
        Directory.CreateDirectory(Path.Combine(root, "factory", "jobs"));
        home.AddRelease("1.3.0", "1.3.0");
        home.SetRedirect(Releases + "/tag/v1.3.0");

        AssertUpdaterKept(home, "bad", ["--factory-root", root]);
    }

    [Fact]
    public void SymlinkedReleaseUpdater_FailsTheWholeCheckAndChangesNothing()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using UpdaterHome home = new();
        home.AddRelease("1.3.0", "1.3.0", updaterKind: "symlink");
        home.SetRedirect(Releases + "/tag/v1.3.0");

        Assert.Matches(LinePattern("check_failed v1.2.0 v1.3.0"), home.RunOnce());
        Assert.Empty(home.LaunchctlCalls());
        Assert.Equal(InstalledUpdater, File.ReadAllText(home.UpdaterScript));
        Assert.Equal(InstalledUpdaterPlist, File.ReadAllText(home.UpdaterPlist));
        Assert.Empty(home.Leftovers());
    }

    [Theory]
    [InlineData("/local.controlroom.updater.plist")]
    [InlineData("/ControlRoom/updater.sh")]
    [InlineData("/updater.plist.previous")]
    [InlineData("/updater.sh.previous")]
    public void FailedUpdaterWrite_PutsBackWhatWasRenamedAndStillUpdates(string target)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using UpdaterHome home = new();
        home.AddRelease("1.3.0", "1.3.0");
        home.SetRedirect(Releases + "/tag/v1.3.0");
        home.FailMoveTo(target);

        AssertUpdaterKept(home, "write_failed");
        Assert.Equal(["# installed updater"], home.UpdaterAtBootstrap());
    }

    [Fact]
    public void SymlinkedInstalledUpdater_IsWriteFailedAndLeftAsItIs()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using UpdaterHome home = new();
        string target = Path.Combine(home.Path, "elsewhere.sh");
        File.Move(home.UpdaterScript, target);
        File.CreateSymbolicLink(home.UpdaterScript, target);
        home.AddRelease("1.3.0", "1.3.0");
        home.SetRedirect(Releases + "/tag/v1.3.0");

        IReadOnlyList<string> lines = home.RunAll();

        Assert.Equal(2, lines.Count);
        Assert.Matches(LinePattern("updater_kept write_failed"), lines[0]);
        Assert.Matches(LinePattern("updated v1.2.0 v1.3.0"), lines[1]);
        Assert.Equal(target, new FileInfo(home.UpdaterScript).LinkTarget);
        Assert.Equal(InstalledUpdater, File.ReadAllText(target));
        Assert.False(File.Exists(home.UpdaterScriptPrevious));
        Assert.Empty(home.Leftovers());
    }

    [Theory]
    [InlineData("rolled_back")]
    [InlineData("bootstrap_failed")]
    [InlineData("swap_failed")]
    public void EveryRollback_PutsTheOldUpdaterScriptAndPlistBack(string word)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using UpdaterHome home = new();
        home.AddRelease("1.3.0", word == "rolled_back" ? "9.9.9" : "1.3.0");
        home.SetRedirect(Releases + "/tag/v1.3.0");
        if (word == "bootstrap_failed")
        {
            home.FailBootstraps(6);
        }
        else if (word == "swap_failed")
        {
            home.FailMoveTo("/installed-release");
        }

        IReadOnlyList<string> before = home.Snapshot("");

        Assert.Matches(LinePattern(word + " v1.2.0 v1.3.0"), home.RunOnce());

        Assert.Equal(InstalledUpdater, File.ReadAllText(home.UpdaterScript));
        Assert.Equal(ScriptMode, File.GetUnixFileMode(home.UpdaterScript));
        Assert.Equal(InstalledUpdaterPlist, File.ReadAllText(home.UpdaterPlist));
        Assert.Equal(PlistMode, File.GetUnixFileMode(home.UpdaterPlist));
        Assert.False(File.Exists(home.UpdaterScriptPrevious));
        Assert.False(File.Exists(home.UpdaterPlistPrevious));
        Assert.Equal("# release updater", home.UpdaterAtBootstrap()[0]);
        Assert.Equal("# installed updater", home.UpdaterAtBootstrap()[^1]);
        Assert.Equal(UpdaterFiles(before), UpdaterFiles(home.Snapshot("")));
        Assert.Empty(home.Leftovers());
    }

    [Fact]
    public void FailedUpdaterRestore_StillEndsWithTheRollbacksWord()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using UpdaterHome home = new();
        home.AddRelease("1.3.0", "9.9.9");
        home.SetRedirect(Releases + "/tag/v1.3.0");
        home.FailMoveFrom("/updater.sh.previous");

        Assert.Matches(LinePattern("rolled_back v1.2.0 v1.3.0"), home.RunOnce());

        Assert.Equal(InstalledUpdaterPlist, File.ReadAllText(home.UpdaterPlist));
        Assert.Equal(ReleaseUpdater, File.ReadAllText(home.UpdaterScript));
        Assert.Equal(InstalledUpdater, File.ReadAllText(home.UpdaterScriptPrevious));
        Assert.Equal("{\"version\":\"1.2.0\"}", File.ReadAllText(Path.Combine(home.Server, "health")));
        AssertResult(home, "\"result\":\"update_failed\",\"version\":\"1.3.0\",\"reason\":\"no_answer\"");
    }

    [Fact]
    public void FailedServerSwapRename_NeverTouchesTheUpdater()
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

        Assert.Equal(InstalledUpdater, File.ReadAllText(home.UpdaterScript));
        Assert.Equal(InstalledUpdaterPlist, File.ReadAllText(home.UpdaterPlist));
        Assert.False(File.Exists(home.UpdaterScriptPrevious));
        Assert.False(File.Exists(home.UpdaterPlistPrevious));
        Assert.Empty(home.Leftovers());
    }

    /// <summary>One updated run that logs <c>updater_kept &lt;why&gt;</c> just before the final line and leaves the
    /// installed updater script and plist byte for byte (and mode) as they were, with no <c>.previous</c> copy
    /// renamed in.</summary>
    private static void AssertUpdaterKept(UpdaterHome home, string why, string[]? arguments = null)
    {
        IReadOnlyList<string> before = home.Snapshot("");

        IReadOnlyList<string> lines = arguments is null ? home.RunAll() : home.RunAllWithArguments(arguments);

        Assert.Equal(2, lines.Count);
        Assert.Matches(LinePattern("updater_kept " + why), lines[0]);
        Assert.Matches(LinePattern("updated v1.2.0 v1.3.0"), lines[1]);
        Assert.Equal(InstalledUpdater, File.ReadAllText(home.UpdaterScript));
        Assert.Equal(ScriptMode, File.GetUnixFileMode(home.UpdaterScript));
        Assert.Equal(InstalledUpdaterPlist, File.ReadAllText(home.UpdaterPlist));
        Assert.Equal(PlistMode, File.GetUnixFileMode(home.UpdaterPlist));
        Assert.Equal(UpdaterFiles(before), UpdaterFiles(home.Snapshot("")));
        Assert.Equal("{\"version\":\"1.3.0\"}", File.ReadAllText(Path.Combine(home.Server, "health")));
        Assert.Equal("v1.3.0\n", File.ReadAllText(home.Marker));
        AssertResult(home, "\"result\":\"updated\",\"version\":\"1.3.0\",\"previous\":\"1.2.0\"");
        Assert.Empty(home.Leftovers());
    }

    /// <summary>The snapshot lines of the installed updater script and plist (path, mode, hash).</summary>
    private static List<string> UpdaterFiles(IReadOnlyList<string> snapshot)
    {
        string script = Path.Combine("Library", "Application Support", "ControlRoom", "updater.sh") + " ";
        string plist = Path.Combine("Library", "LaunchAgents", "local.controlroom.updater.plist") + " ";
        List<string> lines = [.. snapshot.Where(line =>
            line.StartsWith(script, StringComparison.Ordinal) || line.StartsWith(plist, StringComparison.Ordinal))];
        Assert.Equal(2, lines.Count);
        return lines;
    }

    /// <summary>A valid updater script (first line <c>#!/bin/sh</c>) of exactly <paramref name="size"/> bytes.</summary>
    private static string Padded(int size)
    {
        StringBuilder text = new("#!/bin/sh\n");
        while (text.Length < size - 1)
        {
            text.Append('#');
        }

        text.Append('\n');
        Assert.Equal(size, Encoding.UTF8.GetByteCount(text.ToString()));
        return text.ToString();
    }
}
