using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.Cryptography;

namespace ControlRoom.Server.Tests;

/// <summary><c>scripts/launch-agent.sh</c>, run with <c>/bin/sh</c> in its own temp <c>HOME</c> with fake
/// <c>launchctl</c>, <c>dotnet</c> and <c>curl</c> on <c>PATH</c>. No real launchctl, publish or network is used:
/// release tarballs are built with the real <c>/usr/bin/tar</c> in the test's own temp folder.</summary>
[UnsupportedOSPlatform("windows")]
public sealed class LaunchAgentScriptTests
{
    private const string Label = "local.controlroom.server";

    private const string UpdaterLabel = "local.controlroom.updater";

    private const string FixedPath =
        "/usr/local/share/dotnet:/opt/homebrew/bin:/usr/local/bin:/usr/bin:/bin:/usr/sbin:/sbin";

    private const string Usage =
        "usage: scripts/launch-agent.sh install [--release [vX.Y.Z]] [--tailnet-host <name>]|uninstall|status\n";

    private const string Releases = "https://github.com/nothisisnotdaniel/controlroom/releases";

    private const string LatestFailedText = "Can't find the latest release. Nothing was changed.\n";

    private const string DownloadFailedText = "Download failed. Nothing was changed.\n";

    private const string ReleaseBadText = "The release download doesn't check out. Nothing was changed.\n";

    private const string PublishFailedText = "dotnet publish failed. Nothing was changed.\n";

    private const string SymlinkText = "Refusing to write through a symlink: ";

    private const string PlutilPath = "/usr/bin/plutil";

    private const string TailnetHostText = "--tailnet-host must be a name like mac.tailnet.ts.net. Nothing was changed.\n";

    [Fact]
    public void Install_PublishesThenBootsOutReplacesWritesThePlistAndBootstraps()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using ScriptHome home = new();
        Directory.CreateDirectory(Path.Combine(home.Server, "old"));

        ScriptRun run = home.Run("install");

        Assert.Equal(0, run.ExitCode);
        Assert.Equal(
            ["publish", Path.Combine(ScriptHome.RepoFolder(), "src", "ControlRoom.Server"), "-c", "Release", "-o",
                home.ServerNew],
            home.DotnetArguments());
        Assert.Equal(
            ["bootout gui/" + ScriptHome.UserId() + "/" + Label, "bootstrap gui/" + ScriptHome.UserId() + " " + home.Plist],
            home.LaunchctlCalls());
        Assert.Equal(
            ["bootout new=yes server=yes plist=no", "bootstrap new=no server=yes plist=yes"],
            home.LaunchctlStates());
        Assert.True(File.Exists(Path.Combine(home.Server, "ControlRoom.Server.dll")));
        Assert.False(Directory.Exists(Path.Combine(home.Server, "old")));
        Assert.False(Directory.Exists(home.ServerNew));
        Assert.Equal("Installed: " + home.Plist + "\nLog: " + home.Log + "\n", run.Output);
    }

    [Fact]
    public void Install_WritesAPlistThatLintsWithTheFixedKeysAndArguments()
    {
        if (OperatingSystem.IsWindows() || !File.Exists(PlutilPath))
        {
            return;
        }

        using ScriptHome home = new();

        Assert.Equal(0, home.Run("install").ExitCode);

        Assert.Equal(0, Plutil("-lint", home.Plist).ExitCode);
        Assert.Equal(Label, Extract(home.Plist, "Label"));
        Assert.Equal("true", Extract(home.Plist, "RunAtLoad"));
        Assert.Equal("true", Extract(home.Plist, "KeepAlive"));
        Assert.Equal("10", Extract(home.Plist, "ThrottleInterval"));
        Assert.Equal("4", Extract(home.Plist, "ProgramArguments"));
        Assert.Equal(Path.Combine(home.Path, "bin", "dotnet"), Extract(home.Plist, "ProgramArguments.0"));
        Assert.Equal(Path.Combine(home.Server, "ControlRoom.Server.dll"), Extract(home.Plist, "ProgramArguments.1"));
        Assert.Equal("--FactoryRoot", Extract(home.Plist, "ProgramArguments.2"));
        Assert.Equal(
            Directory.GetParent(ScriptHome.RepoFolder())!.FullName, Extract(home.Plist, "ProgramArguments.3"));
        Assert.Equal(home.Log, Extract(home.Plist, "StandardOutPath"));
        Assert.Equal(home.Log, Extract(home.Plist, "StandardErrorPath"));
        Assert.Equal(home.Server, Extract(home.Plist, "WorkingDirectory"));
        Assert.Equal(FixedPath, Extract(home.Plist, "EnvironmentVariables.PATH"));
    }

    [Fact]
    public void Install_WithATailnetHost_EndsTheProgramArgumentsWithIt()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using ScriptHome home = new();

        ScriptRun run = home.Run(ScriptHome.ScriptPath(), "install", "--tailnet-host", "mac.tail1234.ts.net");

        Assert.Equal(0, run.ExitCode);
        Assert.Equal(
            ["--FactoryRoot", Directory.GetParent(ScriptHome.RepoFolder())!.FullName, "--TailnetHost",
                "mac.tail1234.ts.net"],
            ProgramArguments(home.Plist)[2..]);
        if (File.Exists(PlutilPath))
        {
            Assert.Equal(0, Plutil("-lint", home.Plist).ExitCode);
            Assert.Equal("6", Extract(home.Plist, "ProgramArguments"));
            Assert.Equal("--TailnetHost", Extract(home.Plist, "ProgramArguments.4"));
            Assert.Equal("mac.tail1234.ts.net", Extract(home.Plist, "ProgramArguments.5"));
        }
    }

    [Fact]
    public void Install_WithoutATailnetHost_HasNoTailnetHostArgument()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using ScriptHome home = new();

        Assert.Equal(0, home.Run("install").ExitCode);

        Assert.Equal(4, ProgramArguments(home.Plist).Count);
        Assert.DoesNotContain("TailnetHost", File.ReadAllText(home.Plist), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("evil.example")]
    [InlineData("ts.net")]
    [InlineData("mac.ts.net")]
    [InlineData("mac.tail.ts.net:443")]
    [InlineData("*.tail.ts.net")]
    [InlineData("MAC.tail.ts.net")]
    [InlineData("mac..tail.ts.net")]
    [InlineData("-mac.tail.ts.net")]
    [InlineData("mac-.tail.ts.net")]
    [InlineData(".mac.tail.ts.net")]
    [InlineData("mac.tail.ts.net.")]
    [InlineData("mac.tail.ts.net</string>")]
    [InlineData("")]
    public void Install_WithABadTailnetHost_ExitsNonZeroAndChangesNothing(string name)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using ScriptHome home = new();
        IReadOnlyList<string> before = home.Snapshot();

        ScriptRun run = home.Run(ScriptHome.ScriptPath(), "install", "--tailnet-host", name);

        Assert.NotEqual(0, run.ExitCode);
        Assert.Equal(TailnetHostText, run.Error);
        Assert.Equal("", run.Output);
        Assert.False(File.Exists(home.Plist));
        Assert.Empty(home.LaunchctlCalls());
        Assert.Empty(home.DotnetArguments());
        Assert.Equal(before, home.Snapshot());
    }

    [Theory]
    [InlineData("install --tailnet-host")]
    [InlineData("install --other mac.tail1234.ts.net")]
    [InlineData("status --tailnet-host mac.tail1234.ts.net")]
    [InlineData("uninstall --tailnet-host mac.tail1234.ts.net")]
    [InlineData("install --tailnet-host mac.tail1234.ts.net extra")]
    public void BadTailnetHostArguments_PrintUsageExitTwoAndChangeNothing(string joined)
    {
        string[] arguments = joined.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using ScriptHome home = new();
        IReadOnlyList<string> before = home.Snapshot();

        ScriptRun run = home.Run(ScriptHome.ScriptPath(), arguments);

        Assert.Equal(2, run.ExitCode);
        Assert.Equal(Usage, run.Error);
        Assert.Empty(home.LaunchctlCalls());
        Assert.Empty(home.DotnetArguments());
        Assert.Equal(before, home.Snapshot());
    }

    [Fact]
    public void Install_SetsThePlistAndLogFolderModes()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using ScriptHome home = new();

        Assert.Equal(0, home.Run("install").ExitCode);

        Assert.Equal(
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead,
            File.GetUnixFileMode(home.Plist));
        Assert.Equal(
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute,
            File.GetUnixFileMode(Path.GetDirectoryName(home.Log)!));
    }

    [Fact]
    public void Install_EscapesAFactoryRootWithXmlCharacters()
    {
        if (OperatingSystem.IsWindows() || !File.Exists(PlutilPath))
        {
            return;
        }

        using ScriptHome home = new();
        string root = Path.Combine(home.Path, "fac&to<ry\"root");
        string scripts = Path.Combine(root, "ControlRoom", "scripts");
        Directory.CreateDirectory(scripts);
        File.WriteAllText(Path.Combine(root, "ControlRoom", "ControlRoom.slnx"), "");
        string script = Path.Combine(scripts, "launch-agent.sh");
        File.Copy(ScriptHome.ScriptPath(), script);

        ScriptRun run = home.Run(script, "install");

        Assert.Equal(0, run.ExitCode);
        Assert.Equal(0, Plutil("-lint", home.Plist).ExitCode);
        Assert.Equal(root, Extract(home.Plist, "ProgramArguments.3"));
        Assert.Equal(Path.Combine(root, "ControlRoom", "src", "ControlRoom.Server"), home.DotnetArguments()[1]);
    }

    [Fact]
    public void Install_TwiceKeepsOnePlistWithTheSameContentAndIgnoresANotLoadedBootout()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using ScriptHome home = new();
        home.SetExit("bootout", 3);

        Assert.Equal(0, home.Run("install").ExitCode);
        byte[] first = File.ReadAllBytes(home.Plist);
        Assert.Equal(0, home.Run("install").ExitCode);

        Assert.Equal(first, File.ReadAllBytes(home.Plist));
        Assert.Equal([home.Plist], Directory.GetFileSystemEntries(Path.GetDirectoryName(home.Plist)!));
        string service = "gui/" + ScriptHome.UserId() + "/" + Label;
        string bootstrap = "bootstrap gui/" + ScriptHome.UserId() + " " + home.Plist;
        Assert.Equal(["bootout " + service, bootstrap, "bootout " + service, bootstrap], home.LaunchctlCalls());
    }

    [Fact]
    public void Install_WhenPublishFails_ChangesNothingAndCallsNoLaunchctl()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using ScriptHome home = new();
        Assert.Equal(0, home.Run("install").ExitCode);
        home.FailPublish();
        IReadOnlyList<string> before = home.Snapshot();
        int calls = home.LaunchctlCalls().Count;

        ScriptRun run = home.Run("install");

        Assert.NotEqual(0, run.ExitCode);
        Assert.Equal(PublishFailedText, run.Error);
        Assert.Equal(calls, home.LaunchctlCalls().Count);
        Assert.False(Directory.Exists(home.ServerNew));
        Assert.Equal(before, home.Snapshot());
    }

    [Fact]
    public void Install_WhenPublishFailsOnAFreshHome_WritesNoPlist()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using ScriptHome home = new();
        home.FailPublish();

        ScriptRun run = home.Run("install");

        Assert.Equal(1, run.ExitCode);
        Assert.Equal(PublishFailedText, run.Error);
        Assert.Empty(home.LaunchctlCalls());
        Assert.False(File.Exists(home.Plist));
        Assert.False(Directory.Exists(home.ServerNew));
        Assert.False(Directory.Exists(home.Server));
    }

    [Fact]
    public void Install_RefusesASymlinkedPlist()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using ScriptHome home = new();
        string target = Path.Combine(home.Path, "elsewhere.plist");
        File.WriteAllText(target, "original");
        Directory.CreateDirectory(Path.GetDirectoryName(home.Plist)!);
        File.CreateSymbolicLink(home.Plist, target);

        ScriptRun run = home.Run("install");

        Assert.Equal(1, run.ExitCode);
        Assert.Equal(SymlinkText + home.Plist + "\n", run.Error);
        Assert.Equal("original", File.ReadAllText(target));
        Assert.Equal(target, new FileInfo(home.Plist).LinkTarget);
        Assert.Empty(home.LaunchctlCalls());
        Assert.Empty(home.DotnetArguments());
    }

    [Fact]
    public void Install_RefusesASymlinkedLaunchAgentsFolder()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using ScriptHome home = new();
        string target = Path.Combine(home.Path, "elsewhere");
        Directory.CreateDirectory(target);
        string agents = Path.GetDirectoryName(home.Plist)!;
        Directory.CreateDirectory(Path.GetDirectoryName(agents)!);
        Directory.CreateSymbolicLink(agents, target);

        ScriptRun run = home.Run("install");

        Assert.Equal(1, run.ExitCode);
        Assert.Equal(SymlinkText + agents + "\n", run.Error);
        Assert.Empty(Directory.GetFileSystemEntries(target));
        Assert.Empty(home.LaunchctlCalls());
        Assert.Empty(home.DotnetArguments());
    }

    [Fact]
    public void Install_RefusesASymlinkedLogFile()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using ScriptHome home = new();
        string target = Path.Combine(home.Path, "elsewhere.log");
        File.WriteAllText(target, "original");
        Directory.CreateDirectory(Path.GetDirectoryName(home.Log)!);
        File.CreateSymbolicLink(home.Log, target);

        ScriptRun run = home.Run("install");

        Assert.Equal(1, run.ExitCode);
        Assert.Equal(SymlinkText + home.Log + "\n", run.Error);
        Assert.Equal("original", File.ReadAllText(target));
        Assert.Empty(home.LaunchctlCalls());
        Assert.False(File.Exists(home.Plist));
    }

    [Fact]
    public void Install_WhenBootstrapFails_ExitsOneWithAFixedMessage()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using ScriptHome home = new();
        home.SetExit("bootstrap", 5);

        ScriptRun run = home.Run("install");

        Assert.Equal(1, run.ExitCode);
        Assert.Equal(
            "launchctl bootstrap failed. Check: launchctl print gui/" + ScriptHome.UserId() + "/" + Label + "\n",
            run.Error);
        Assert.Equal("", run.Output);
    }

    [Fact]
    public void Uninstall_RefusesASymlinkedLibraryFolder()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using ScriptHome home = new();
        string target = Path.Combine(home.Path, "elsewhere");
        string server = Path.Combine(target, "Application Support", "ControlRoom", "server");
        Directory.CreateDirectory(server);
        Directory.CreateSymbolicLink(Path.Combine(home.Path, "Library"), target);

        ScriptRun run = home.Run("uninstall");

        Assert.Equal(1, run.ExitCode);
        Assert.Equal(SymlinkText + Path.Combine(home.Path, "Library") + "\n", run.Error);
        Assert.True(Directory.Exists(server));
        Assert.Empty(home.LaunchctlCalls());
    }

    [Fact]
    public void Uninstall_BootsOutAndRemovesThePlistAndServerButKeepsTheLogAndToken()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using ScriptHome home = new();
        Assert.Equal(0, home.Run("install").ExitCode);
        File.WriteAllText(home.Log, "log line\n");
        home.PlantToken("token-value");

        ScriptRun run = home.Run("uninstall");

        Assert.Equal(0, run.ExitCode);
        Assert.Equal("bootout gui/" + ScriptHome.UserId() + "/" + Label, home.LaunchctlCalls()[^1]);
        Assert.False(File.Exists(home.Plist));
        Assert.False(Directory.Exists(home.Server));
        Assert.Equal("log line\n", File.ReadAllText(home.Log));
        Assert.Equal("token-value", File.ReadAllText(home.TokenFile));
    }

    [Fact]
    public void Uninstall_WhenNothingIsInstalled_ExitsZero()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using ScriptHome home = new();
        home.SetExit("bootout", 3);

        ScriptRun run = home.Run("uninstall");

        Assert.Equal(0, run.ExitCode);
        Assert.Equal(
            ["bootout gui/" + ScriptHome.UserId() + "/" + UpdaterLabel, "bootout gui/" + ScriptHome.UserId() + "/" + Label],
            home.LaunchctlCalls());
    }

    [Fact]
    public void Status_WhenNotInstalled_PrintsNoAndChangesNothing()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using ScriptHome home = new();
        home.SetExit("print", 113);
        IReadOnlyList<string> before = home.Snapshot();

        ScriptRun run = home.Run("status");

        Assert.Equal(0, run.ExitCode);
        Assert.Equal("installed: no\nrunning: no\nupdater: no\nlog: " + home.Log + "\n", run.Output);
        Assert.Equal(["print gui/" + ScriptHome.UserId() + "/" + Label], home.LaunchctlCalls());
        Assert.Equal(before, home.Snapshot());
    }

    [Fact]
    public void Status_WhenInstalledAndRunning_PrintsYesAndChangesNothing()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using ScriptHome home = new();
        Assert.Equal(0, home.Run("install").ExitCode);
        home.SetExit("print", 0);
        IReadOnlyList<string> before = home.Snapshot();

        ScriptRun run = home.Run("status");

        Assert.Equal(0, run.ExitCode);
        Assert.Equal("installed: yes\nrunning: yes\nupdater: no\nlog: " + home.Log + "\n", run.Output);
        Assert.Equal(before, home.Snapshot());
    }

    [Theory]
    [InlineData("")]
    [InlineData("bogus")]
    [InlineData("install extra")]
    public void BadArguments_PrintUsageExitTwoAndChangeNothing(string joined)
    {
        string[] arguments = joined.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using ScriptHome home = new();
        IReadOnlyList<string> before = home.Snapshot();

        ScriptRun run = home.Run(ScriptHome.ScriptPath(), arguments);

        Assert.Equal(2, run.ExitCode);
        Assert.Equal(Usage, run.Error);
        Assert.Equal("", run.Output);
        Assert.Empty(home.LaunchctlCalls());
        Assert.Empty(home.DotnetArguments());
        Assert.Equal(before, home.Snapshot());
    }

    [Fact]
    public void NoCommand_ShowsOrWritesTheToken()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using ScriptHome home = new();
        const string Token = "planted-token-0123456789abcdefghijklmnopqrstu";
        home.PlantToken(Token);
        File.SetUnixFileMode(home.TokenFile, UnixFileMode.None);
        List<string> outputs = [];

        foreach (string command in new[] { "install", "status", "uninstall", "install" })
        {
            ScriptRun run = home.Run(command);
            Assert.Equal(0, run.ExitCode);
            outputs.Add(run.Output);
            outputs.Add(run.Error);
        }

        File.SetUnixFileMode(home.TokenFile, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        Assert.All(outputs, text => Assert.DoesNotContain(Token, text, StringComparison.Ordinal));
        Assert.DoesNotContain(Token, home.Log, StringComparison.Ordinal);
        foreach (string file in Directory.EnumerateFiles(home.Path, "*", SearchOption.AllDirectories))
        {
            if (file != home.TokenFile)
            {
                Assert.DoesNotContain(Token, File.ReadAllText(file), StringComparison.Ordinal);
            }
        }

        Assert.DoesNotContain("access-token", File.ReadAllText(ScriptHome.ScriptPath()), StringComparison.Ordinal);
    }

    [Fact]
    public void InstallRelease_WithATag_DownloadsChecksExtractsAndRunsTheServerWithoutDotnet()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using ScriptHome home = new();
        home.AddRelease("1.2.3", ReleaseFault.None);
        Directory.CreateDirectory(Path.Combine(home.Server, "old"));

        ScriptRun run = home.Run(ScriptHome.ScriptPath(), "install", "--release", "v1.2.3");

        Assert.Equal(0, run.ExitCode);
        Assert.Equal(
            "Installed v1.2.3: " + home.Plist + "\nLog: " + home.Log + "\nUpdater: " + home.UpdaterPlist + "\n",
            run.Output);
        Assert.Empty(home.DotnetArguments());
        IReadOnlyList<string> curl = home.CurlCalls();
        Assert.Equal(2, curl.Count);
        AssertDownload(home, curl[0], "v1.2.3", "controlroom-1.2.3-osx-arm64.tar.gz");
        AssertDownload(home, curl[1], "v1.2.3", "SHA256SUMS");
        Assert.Equal(
            ["bootout new=yes server=yes plist=no", "bootout new=yes server=yes plist=no",
                "bootstrap new=no server=yes plist=yes", "bootstrap new=no server=yes plist=yes"],
            home.LaunchctlStates());
        Assert.True(File.Exists(Path.Combine(home.Server, "ControlRoom.Server")));
        Assert.True(File.Exists(Path.Combine(home.Server, "wwwroot", "index.html")));
        Assert.False(Directory.Exists(Path.Combine(home.Server, "old")));
        Assert.False(Directory.Exists(home.ServerNew));
        Assert.Empty(home.WorkFolders());
        Assert.Equal(
            [Path.Combine(home.Server, "ControlRoom.Server"), "--FactoryRoot",
                Directory.GetParent(ScriptHome.RepoFolder())!.FullName],
            ProgramArguments(home.Plist));
        Assert.Equal(
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead,
            File.GetUnixFileMode(home.Plist));
        if (File.Exists(PlutilPath))
        {
            Assert.Equal(0, Plutil("-lint", home.Plist).ExitCode);
            Assert.Equal("3", Extract(home.Plist, "ProgramArguments"));
            Assert.Equal(home.Server, Extract(home.Plist, "WorkingDirectory"));
        }
    }

    [Fact]
    public void InstallRelease_WithADevelopTag_DownloadsChecksAndRecordsThatTag()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using ScriptHome home = new();
        home.AddRelease("1.2.4-develop.7", ReleaseFault.None);

        ScriptRun run = home.Run(ScriptHome.ScriptPath(), "install", "--release", "v1.2.4-develop.7");

        Assert.Equal(0, run.ExitCode);
        Assert.Equal(
            "Installed v1.2.4-develop.7: " + home.Plist + "\nLog: " + home.Log + "\nUpdater: " + home.UpdaterPlist + "\n",
            run.Output);
        IReadOnlyList<string> curl = home.CurlCalls();
        Assert.Equal(2, curl.Count);
        AssertDownload(home, curl[0], "v1.2.4-develop.7", "controlroom-1.2.4-develop.7-osx-arm64.tar.gz");
        AssertDownload(home, curl[1], "v1.2.4-develop.7", "SHA256SUMS");
        Assert.True(File.Exists(Path.Combine(home.Server, "ControlRoom.Server")));
        Assert.Equal("v1.2.4-develop.7\n", File.ReadAllText(home.Marker));
        Assert.Empty(home.WorkFolders());
    }

    [Fact]
    public void InstallRelease_WithADevelopTagAndABadChecksum_RefusesItAndChangesNothing()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using ScriptHome home = new();
        Assert.Equal(0, home.Run("install").ExitCode);
        home.AddRelease("1.2.4-develop.7", ReleaseFault.BadChecksum);

        AssertReleaseRefused(home, ["install", "--release", "v1.2.4-develop.7"], ReleaseBadText);
    }

    [Fact]
    public void InstallRelease_WithoutATag_FindsTheLatestReleaseFromTheRedirect()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using ScriptHome home = new();
        home.AddRelease("2.0.0-rc.1", ReleaseFault.None);
        home.SetRedirect(Releases + "/tag/v2.0.0-rc.1");

        ScriptRun run = home.Run(ScriptHome.ScriptPath(), "install", "--release");

        Assert.Equal(0, run.ExitCode);
        Assert.Equal(
            "Installed v2.0.0-rc.1: " + home.Plist + "\nLog: " + home.Log + "\nUpdater: " + home.UpdaterPlist + "\n",
            run.Output);
        IReadOnlyList<string> curl = home.CurlCalls();
        Assert.Equal(3, curl.Count);
        Assert.Equal(
            "--proto =https --tlsv1.2 -fsS --max-time 30 -o /dev/null -w %{redirect_url} " + Releases + "/latest",
            curl[0]);
        AssertDownload(home, curl[1], "v2.0.0-rc.1", "controlroom-2.0.0-rc.1-osx-arm64.tar.gz");
        AssertDownload(home, curl[2], "v2.0.0-rc.1", "SHA256SUMS");
        Assert.Empty(home.DotnetArguments());
        Assert.Empty(home.WorkFolders());
    }

    [Theory]
    [InlineData("1.2.3-rc.1")]
    [InlineData("1.2.4-develop.7")]
    [InlineData("1.2.3-01a")]
    public void InstallRelease_WithAStrictSemVerPrerelease_ReachesTheDownload(string version)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using ScriptHome home = new();
        home.AddRelease(version, ReleaseFault.None);

        ScriptRun run = home.Run(ScriptHome.ScriptPath(), "install", "--release", "v" + version);

        Assert.Equal(0, run.ExitCode);
        IReadOnlyList<string> curl = home.CurlCalls();
        Assert.Equal(2, curl.Count);
        AssertDownload(home, curl[0], "v" + version, "controlroom-" + version + "-osx-arm64.tar.gz");
    }

    [Theory]
    [InlineData("install --release v1.2.3 --tailnet-host mac.tail1234.ts.net")]
    [InlineData("install --tailnet-host mac.tail1234.ts.net --release v1.2.3")]
    [InlineData("install --release --tailnet-host mac.tail1234.ts.net")]
    [InlineData("install --tailnet-host mac.tail1234.ts.net --release")]
    public void InstallRelease_WithATailnetHostInEitherOrder_EndsTheProgramArgumentsWithIt(string joined)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using ScriptHome home = new();
        home.AddRelease("1.2.3", ReleaseFault.None);
        home.SetRedirect(Releases + "/tag/v1.2.3");

        ScriptRun run = home.Run(ScriptHome.ScriptPath(), joined.Split(' '));

        Assert.Equal(0, run.ExitCode);
        Assert.Equal(
            [Path.Combine(home.Server, "ControlRoom.Server"), "--FactoryRoot",
                Directory.GetParent(ScriptHome.RepoFolder())!.FullName, "--TailnetHost", "mac.tail1234.ts.net"],
            ProgramArguments(home.Plist));
        Assert.Empty(home.DotnetArguments());
    }

    [Fact]
    public void Install_AfterARelease_WritesTheDotnetPlistAgain()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using ScriptHome home = new();
        home.AddRelease("1.2.3", ReleaseFault.None);
        Assert.Equal(0, home.Run(ScriptHome.ScriptPath(), "install", "--release", "v1.2.3").ExitCode);

        ScriptRun run = home.Run("install");

        Assert.Equal(0, run.ExitCode);
        Assert.Equal("Installed: " + home.Plist + "\nLog: " + home.Log + "\n", run.Output);
        Assert.Equal(
            [Path.Combine(home.Path, "bin", "dotnet"), Path.Combine(home.Server, "ControlRoom.Server.dll"),
                "--FactoryRoot", Directory.GetParent(ScriptHome.RepoFolder())!.FullName],
            ProgramArguments(home.Plist));
        Assert.False(File.Exists(Path.Combine(home.Server, "ControlRoom.Server")));
    }

    [Theory]
    [InlineData("1.2.3")]
    [InlineData("v1.2")]
    [InlineData("v1.2.3.4")]
    [InlineData("v01.2.3")]
    [InlineData("v1.02.3")]
    [InlineData("v1.2.3+build")]
    [InlineData("v1.2.3-develop.1+build")]
    [InlineData("v1.2.3-develop_1")]
    [InlineData("v1.2.3-develop.*")]
    [InlineData("1.2.3-develop.1")]
    [InlineData("v1.2.3-")]
    [InlineData("v1.2.3-01")]
    [InlineData("v1.2.3-rc.01")]
    [InlineData("v1.2.3/../x")]
    [InlineData("v1.2.3 ")]
    [InlineData("v1.2.3\nv1.2.4")]
    [InlineData("V1.2.3")]
    [InlineData("--release")]
    [InlineData("")]
    public void InstallRelease_WithABadTag_PrintsUsageExitsTwoAndDownloadsNothing(string tag)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using ScriptHome home = new();
        home.AddRelease("1.2.3", ReleaseFault.None);
        IReadOnlyList<string> before = home.Snapshot();

        ScriptRun run = home.Run(ScriptHome.ScriptPath(), "install", "--release", tag);

        Assert.Equal(2, run.ExitCode);
        Assert.Equal(Usage, run.Error);
        Assert.Equal("", run.Output);
        Assert.Empty(home.CurlCalls());
        Assert.Empty(home.LaunchctlCalls());
        Assert.Empty(home.DotnetArguments());
        Assert.Equal(before, home.Snapshot());
    }

    [Theory]
    [InlineData("install --release v1.2.3 extra")]
    [InlineData("install --release v1.2.3 v1.2.4")]
    [InlineData("install --release --release")]
    [InlineData("install --release v1.2.3 --release")]
    [InlineData("install --tailnet-host mac.tail1234.ts.net --tailnet-host mac.tail1234.ts.net")]
    [InlineData("install --tailnet-host mac.tail1234.ts.net --release v1.2.3 --tailnet-host mac.tail1234.ts.net")]
    [InlineData("install --release --tailnet-host")]
    [InlineData("install v1.2.3")]
    [InlineData("status --release")]
    [InlineData("uninstall --release v1.2.3")]
    public void BadReleaseArguments_PrintUsageExitTwoAndChangeNothing(string joined)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using ScriptHome home = new();
        IReadOnlyList<string> before = home.Snapshot();

        ScriptRun run = home.Run(ScriptHome.ScriptPath(), joined.Split(' '));

        Assert.Equal(2, run.ExitCode);
        Assert.Equal(Usage, run.Error);
        Assert.Empty(home.CurlCalls());
        Assert.Empty(home.LaunchctlCalls());
        Assert.Empty(home.DotnetArguments());
        Assert.Equal(before, home.Snapshot());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(Releases + "/tag/v1.2")]
    [InlineData(Releases + "/tag/v1.2.3\n")]
    [InlineData(Releases + "/tag/v1.2.3/")]
    [InlineData(Releases + "/tag/v1.2.3+abc")]
    [InlineData(Releases + "/tag/v1.2.3-01")]
    [InlineData(Releases + "/tag/v1.2.3/../../x")]
    [InlineData(Releases + "/tag/1.2.3")]
    [InlineData(Releases + "/tag/")]
    [InlineData(Releases)]
    [InlineData("http://github.com/nothisisnotdaniel/controlroom/releases/tag/v1.2.3")]
    [InlineData("https://evil.example/nothisisnotdaniel/controlroom/releases/tag/v1.2.3")]
    [InlineData("https://github.com/someone/controlroom/releases/tag/v1.2.3")]
    [InlineData(" " + Releases + "/tag/v1.2.3")]
    public void InstallRelease_WithABadLatestAnswer_FailsAndChangesNothing(string? answer)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using ScriptHome home = new();
        Assert.Equal(0, home.Run("install").ExitCode);
        home.AddRelease("1.2.3", ReleaseFault.None);
        if (answer is not null)
        {
            home.SetRedirect(answer);
        }

        AssertReleaseRefused(home, ["install", "--release"], LatestFailedText);
        Assert.Single(home.CurlCalls());
    }

    [Theory]
    [InlineData("controlroom-1.2.3-osx-arm64.tar.gz")]
    [InlineData("SHA256SUMS")]
    public void InstallRelease_WhenADownloadFails_FailsAndChangesNothing(string missing)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using ScriptHome home = new();
        Assert.Equal(0, home.Run("install").ExitCode);
        home.AddRelease("1.2.3", ReleaseFault.None);
        home.RemoveReleaseFile(missing);

        AssertReleaseRefused(home, ["install", "--release", "v1.2.3"], DownloadFailedText);
    }

    [Theory]
    [InlineData(ReleaseFault.BadChecksum)]
    [InlineData(ReleaseFault.NoChecksumLine)]
    [InlineData(ReleaseFault.TwoChecksumLines)]
    [InlineData(ReleaseFault.UpperCaseChecksum)]
    [InlineData(ReleaseFault.DotDotEntry)]
    [InlineData(ReleaseFault.DotEntry)]
    [InlineData(ReleaseFault.AbsoluteEntry)]
    [InlineData(ReleaseFault.OutsideEntry)]
    [InlineData(ReleaseFault.SecondTopFolder)]
    [InlineData(ReleaseFault.SymlinkEntry)]
    [InlineData(ReleaseFault.HardLinkEntry)]
    [InlineData(ReleaseFault.NotExecutable)]
    [InlineData(ReleaseFault.NoIndexHtml)]
    [InlineData(ReleaseFault.TooManyEntries)]
    [InlineData(ReleaseFault.TooMuchContent)]
    public void InstallRelease_WithABadDownload_RefusesItAndChangesNothing(ReleaseFault fault)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using ScriptHome home = new();
        Assert.Equal(0, home.Run("install").ExitCode);
        home.AddRelease("1.2.3", fault);

        AssertReleaseRefused(home, ["install", "--release", "v1.2.3"], ReleaseBadText);
        Assert.False(File.Exists(Path.Combine(home.Path, "Library", "evil")));
        Assert.False(File.Exists(Path.Combine(home.Support, "evil")));
    }

    [Fact]
    public void InstallRelease_WhenSha256SumsIsOverItsSizeCap_FailsTheDownloadAndChangesNothing()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using ScriptHome home = new();
        Assert.Equal(0, home.Run("install").ExitCode);
        home.AddRelease("1.2.3", ReleaseFault.LargeChecksumFile);

        AssertReleaseRefused(home, ["install", "--release", "v1.2.3"], DownloadFailedText);
        Assert.Equal(2, home.CurlCalls().Count);
    }

    [Fact]
    public void InstallRelease_OnAFreshHome_WhenTheDownloadIsBad_WritesNoPlist()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using ScriptHome home = new();
        home.AddRelease("1.2.3", ReleaseFault.BadChecksum);

        ScriptRun run = home.Run(ScriptHome.ScriptPath(), "install", "--release", "v1.2.3");

        Assert.Equal(1, run.ExitCode);
        Assert.Equal(ReleaseBadText, run.Error);
        Assert.Empty(home.LaunchctlCalls());
        Assert.False(File.Exists(home.Plist));
        Assert.False(Directory.Exists(home.Server));
        Assert.False(Directory.Exists(home.ServerNew));
        Assert.Empty(home.WorkFolders());
    }

    [Fact]
    public void InstallRelease_CopiesTheUpdaterWritesTheMarkerAndBootstrapsTheUpdaterPlist()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using ScriptHome home = new();
        home.AddRelease("1.2.3", ReleaseFault.None);

        ScriptRun run = home.Run(ScriptHome.ScriptPath(), "install", "--release", "v1.2.3");

        Assert.Equal(0, run.ExitCode);
        string user = ScriptHome.UserId();
        Assert.Equal(
            ["bootout gui/" + user + "/" + UpdaterLabel, "bootout gui/" + user + "/" + Label,
                "bootstrap gui/" + user + " " + home.Plist, "bootstrap gui/" + user + " " + home.UpdaterPlist],
            home.LaunchctlCalls());
        Assert.Equal(
            File.ReadAllText(System.IO.Path.Combine(ScriptHome.RepoFolder(), "scripts", "updater.sh")),
            File.ReadAllText(home.UpdaterScript));
        Assert.Equal(
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute,
            File.GetUnixFileMode(home.UpdaterScript));
        Assert.Equal("v1.2.3\n", File.ReadAllText(home.Marker));
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(home.Marker));
        Assert.Equal(
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead,
            File.GetUnixFileMode(home.UpdaterPlist));
        Assert.Equal(
            ["/bin/sh", home.UpdaterScript, "--factory-root", Directory.GetParent(ScriptHome.RepoFolder())!.FullName],
            ProgramArguments(home.UpdaterPlist));
        string plist = File.ReadAllText(home.UpdaterPlist);
        Assert.DoesNotContain("RunAtLoad", plist, StringComparison.Ordinal);
        Assert.DoesNotContain("KeepAlive", plist, StringComparison.Ordinal);
        Assert.Empty(Directory.GetFiles(home.Support, ".controlroom.*"));
        Assert.Empty(Directory.GetFiles(System.IO.Path.GetDirectoryName(home.UpdaterPlist)!, ".controlroom.*"));
        if (File.Exists(PlutilPath))
        {
            Assert.Equal(0, Plutil("-lint", home.UpdaterPlist).ExitCode);
            Assert.Equal(UpdaterLabel, Extract(home.UpdaterPlist, "Label"));
            Assert.Equal("21600", Extract(home.UpdaterPlist, "StartInterval"));
            Assert.Equal(FixedPath, Extract(home.UpdaterPlist, "EnvironmentVariables.PATH"));
            Assert.Equal(home.UpdaterLog, Extract(home.UpdaterPlist, "StandardOutPath"));
            Assert.Equal(home.UpdaterLog, Extract(home.UpdaterPlist, "StandardErrorPath"));
            Assert.Equal("4", Extract(home.UpdaterPlist, "ProgramArguments"));
        }
    }

    [Fact]
    public void Status_AfterARelease_ReportsTheUpdater()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using ScriptHome home = new();
        home.AddRelease("1.2.3", ReleaseFault.None);
        Assert.Equal(0, home.Run(ScriptHome.ScriptPath(), "install", "--release", "v1.2.3").ExitCode);
        home.SetExit("print", 0);

        ScriptRun run = home.Run("status");

        Assert.Equal(0, run.ExitCode);
        Assert.Equal("installed: yes\nrunning: yes\nupdater: yes\nlog: " + home.Log + "\n", run.Output);
    }

    [Fact]
    public void Install_AfterARelease_BootsOutAndRemovesTheUpdaterScriptAndMarker()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using ScriptHome home = new();
        home.AddRelease("1.2.3", ReleaseFault.None);
        Assert.Equal(0, home.Run(ScriptHome.ScriptPath(), "install", "--release", "v1.2.3").ExitCode);
        int before = home.LaunchctlCalls().Count;

        ScriptRun run = home.Run("install");

        Assert.Equal(0, run.ExitCode);
        string user = ScriptHome.UserId();
        Assert.Equal(
            ["bootout gui/" + user + "/" + UpdaterLabel, "bootout gui/" + user + "/" + Label,
                "bootstrap gui/" + user + " " + home.Plist],
            home.LaunchctlCalls().Skip(before));
        Assert.False(File.Exists(home.UpdaterPlist));
        Assert.False(File.Exists(home.UpdaterScript));
        Assert.False(File.Exists(home.Marker));
    }

    [Fact]
    public void Uninstall_AfterARelease_RemovesTheUpdaterMarkerAndPreviousServer()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using ScriptHome home = new();
        home.AddRelease("1.2.3", ReleaseFault.None);
        Assert.Equal(0, home.Run(ScriptHome.ScriptPath(), "install", "--release", "v1.2.3").ExitCode);
        Directory.CreateDirectory(home.Previous);
        File.WriteAllText(System.IO.Path.Combine(home.Previous, "ControlRoom.Server"), "old");

        ScriptRun run = home.Run("uninstall");

        Assert.Equal(0, run.ExitCode);
        string user = ScriptHome.UserId();
        Assert.Equal(
            ["bootout gui/" + user + "/" + UpdaterLabel, "bootout gui/" + user + "/" + Label],
            home.LaunchctlCalls().TakeLast(2));
        Assert.False(File.Exists(home.UpdaterPlist));
        Assert.False(File.Exists(home.UpdaterScript));
        Assert.False(File.Exists(home.Marker));
        Assert.False(Directory.Exists(home.Previous));
        Assert.False(Directory.Exists(home.Server));
    }

    [Fact]
    public void InstallRelease_RefusesASymlinkedUpdaterScript()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using ScriptHome home = new();
        home.AddRelease("1.2.3", ReleaseFault.None);
        Directory.CreateDirectory(home.Support);
        string target = System.IO.Path.Combine(home.Path, "elsewhere.sh");
        File.WriteAllText(target, "keep");
        File.CreateSymbolicLink(home.UpdaterScript, target);

        ScriptRun run = home.Run(ScriptHome.ScriptPath(), "install", "--release", "v1.2.3");

        Assert.Equal(1, run.ExitCode);
        Assert.Equal(SymlinkText + home.UpdaterScript + "\n", run.Error);
        Assert.Equal("keep", File.ReadAllText(target));
        Assert.Empty(home.LaunchctlCalls());
    }

    [Theory]
    [InlineData("install")]
    [InlineData("install --release v1.2.3")]
    [InlineData("uninstall")]
    public void InstallOrUninstall_WhileAFreshUpdateLockExists_StopsAndChangesNothing(string joined)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using ScriptHome home = new();
        home.AddRelease("1.2.3", ReleaseFault.None);
        Directory.CreateDirectory(home.Lock);
        IReadOnlyList<string> before = home.Snapshot();

        ScriptRun run = home.Run(ScriptHome.ScriptPath(), joined.Split(' '));

        Assert.Equal(1, run.ExitCode);
        Assert.Equal("An update is running. Try again in a few minutes.\n", run.Error);
        Assert.Equal("", run.Output);
        Assert.Empty(home.LaunchctlCalls());
        Assert.Empty(home.DotnetArguments());
        Assert.Empty(home.CurlCalls());
        Assert.Equal(before, home.Snapshot());
    }

    [Fact]
    public void Install_WithAnUpdateLockOlderThanAnHour_Installs()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using ScriptHome home = new();
        Directory.CreateDirectory(home.Lock);
        Directory.SetLastWriteTimeUtc(home.Lock, DateTime.UtcNow.AddHours(-2));

        ScriptRun run = home.Run("install");

        Assert.Equal(0, run.ExitCode);
        Assert.True(File.Exists(home.Plist));
    }

    /// <summary>Runs a release install that must fail with <paramref name="text"/>: exit 1, no output, no new
    /// launchctl call (so no <c>bootout</c>), no dotnet, the work folder gone and HOME as before.</summary>
    private static void AssertReleaseRefused(ScriptHome home, string[] arguments, string text)
    {
        IReadOnlyList<string> before = home.Snapshot();
        int calls = home.LaunchctlCalls().Count;
        int publishes = home.DotnetArguments().Count;

        ScriptRun run = home.Run(ScriptHome.ScriptPath(), arguments);

        Assert.Equal(1, run.ExitCode);
        Assert.Equal(text, run.Error);
        Assert.Equal("", run.Output);
        Assert.Equal(calls, home.LaunchctlCalls().Count);
        Assert.Equal(publishes, home.DotnetArguments().Count);
        Assert.Empty(home.WorkFolders());
        Assert.False(Directory.Exists(home.ServerNew));
        Assert.Equal(before, home.Snapshot());
    }

    /// <summary>One download call: fixed options, <c>-o</c> into a work folder under the support folder, and the
    /// fixed release URL.</summary>
    private static void AssertDownload(ScriptHome home, string call, string tag, string name)
    {
        string limit = name == "SHA256SUMS" ? "4096" : "209715200";
        string options = "--proto =https --proto-redir =https --tlsv1.2 -fsSL --max-time 300 --max-filesize " + limit
            + " -o ";
        Assert.StartsWith(options, call, StringComparison.Ordinal);
        string suffix = "/" + name + " " + Releases + "/download/" + tag + "/" + name;
        Assert.EndsWith(suffix, call, StringComparison.Ordinal);
        string folder = call[options.Length..^suffix.Length];
        Assert.Equal(home.Support, Path.GetDirectoryName(folder));
        Assert.StartsWith(".release.", Path.GetFileName(folder), StringComparison.Ordinal);
    }

    /// <summary>The <c>&lt;string&gt;</c> values of the plist's <c>ProgramArguments</c> array, read as text (no
    /// <c>plutil</c> needed).</summary>
    private static List<string> ProgramArguments(string plist)
    {
        string text = File.ReadAllText(plist);
        int start = text.IndexOf("<key>ProgramArguments</key>", StringComparison.Ordinal);
        int end = text.IndexOf("</array>", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        List<string> values = [];
        foreach (string line in text[start..end].Split('\n'))
        {
            string trimmed = line.Trim();
            if (trimmed.StartsWith("<string>", StringComparison.Ordinal))
            {
                values.Add(trimmed["<string>".Length..^"</string>".Length]);
            }
        }

        return values;
    }

    private static string Extract(string plist, string keyPath)
    {
        ScriptRun run = Plutil("-extract", keyPath, "raw", "-o", "-", plist);
        Assert.True(run.ExitCode == 0, "plutil -extract " + keyPath + ": " + run.Error);
        return run.Output.TrimEnd('\n');
    }

    private static ScriptRun Plutil(params string[] arguments)
    {
        return ScriptHome.RunProcess(PlutilPath, arguments, null);
    }

    private sealed record ScriptRun(int ExitCode, string Output, string Error);

    /// <summary>The one way a test release is broken.</summary>
    public enum ReleaseFault
    {
        None,
        BadChecksum,
        NoChecksumLine,
        TwoChecksumLines,
        UpperCaseChecksum,
        DotDotEntry,
        DotEntry,
        AbsoluteEntry,
        OutsideEntry,
        SecondTopFolder,
        SymlinkEntry,
        HardLinkEntry,
        NotExecutable,
        NoIndexHtml,
        TooManyEntries,
        TooMuchContent,
        LargeChecksumFile,
    }

    /// <summary>A temp folder that acts as <c>HOME</c>, with fake <c>launchctl</c> and <c>dotnet</c> in its
    /// <c>bin/</c>; deleted on dispose.</summary>
    private sealed class ScriptHome : IDisposable
    {
        /// <summary>The longest any one process run may take before the test fails.</summary>
        private static readonly TimeSpan ProcessTimeout = TimeSpan.FromSeconds(60);

        private const string FakeLaunchctl =
            "#!/bin/sh\n"
            + "printf '%s\\n' \"$*\" >> \"$HOME/calls.log\"\n"
            + "support=\"$HOME/Library/Application Support/ControlRoom\"\n"
            + "yn() { if [ -e \"$1\" ]; then printf yes; else printf no; fi; }\n"
            + "printf '%s new=%s server=%s plist=%s\\n' \"$1\" \"$(yn \"$support/server.new\")\" "
            + "\"$(yn \"$support/server\")\" \"$(yn \"$HOME/Library/LaunchAgents/local.controlroom.server.plist\")\" "
            + ">> \"$HOME/states.log\"\n"
            + "if [ -f \"$HOME/$1-exit\" ]; then exit \"$(cat \"$HOME/$1-exit\")\"; fi\n"
            + "exit 0\n";

        private const string FakeDotnet =
            "#!/bin/sh\n"
            + "for a in \"$@\"; do printf '%s\\n' \"$a\" >> \"$HOME/dotnet.log\"; done\n"
            + "out=\"\"\n"
            + "while [ $# -gt 0 ]; do if [ \"$1\" = \"-o\" ]; then out=$2; fi; shift; done\n"
            + "mkdir -p \"$out\" || exit 1\n"
            + "if [ -f \"$HOME/dotnet-fail\" ]; then printf partial > \"$out/partial\"; exit 1; fi\n"
            + "printf dll > \"$out/ControlRoom.Server.dll\"\n"
            + "exit 0\n";

        /// <summary>Logs its arguments as one line; for <c>-w</c> prints <c>$HOME/redirect</c> (exit 22 when
        /// missing), else copies <c>$HOME/release/&lt;last URL part&gt;</c> to <c>-o</c> (exit 22 when
        /// missing, exit 63 like curl when it is larger than <c>--max-filesize</c>).</summary>
        private const string FakeCurl =
            "#!/bin/sh\n"
            + "printf '%s\\n' \"$*\" >> \"$HOME/curl.log\"\n"
            + "out=\"\"; url=\"\"; w=no; max=\"\"\n"
            + "while [ $# -gt 0 ]; do\n"
            + "  case $1 in -o) out=$2; shift ;; -w) w=yes; shift ;; --max-filesize) max=$2; shift ;;"
            + " https://*) url=$1 ;; esac\n"
            + "  shift\n"
            + "done\n"
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

        private static readonly string[] HarnessFiles = ["calls.log", "states.log", "dotnet.log", "curl.log"];

        public ScriptHome()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), "controlroom-agent-" + Guid.NewGuid().ToString("N"));
            string bin = System.IO.Path.Combine(Path, "bin");
            Directory.CreateDirectory(bin);
            WriteExecutable(System.IO.Path.Combine(bin, "launchctl"), FakeLaunchctl);
            WriteExecutable(System.IO.Path.Combine(bin, "dotnet"), FakeDotnet);
            WriteExecutable(System.IO.Path.Combine(bin, "curl"), FakeCurl);
        }

        public string Path { get; }

        public string Plist => System.IO.Path.Combine(Path, "Library", "LaunchAgents", Label + ".plist");

        public string Log => System.IO.Path.Combine(Path, "Library", "Logs", "ControlRoom", "server.log");

        public string UpdaterPlist => System.IO.Path.Combine(Path, "Library", "LaunchAgents", UpdaterLabel + ".plist");

        public string UpdaterLog => System.IO.Path.Combine(Path, "Library", "Logs", "ControlRoom", "updater.log");

        public string UpdaterScript => System.IO.Path.Combine(Support, "updater.sh");

        public string Marker => System.IO.Path.Combine(Support, "installed-release");

        public string Previous => System.IO.Path.Combine(Support, "server.previous");

        public string Lock => System.IO.Path.Combine(Support, ".update.lock");

        public string Support => System.IO.Path.Combine(Path, "Library", "Application Support", "ControlRoom");

        public string Server => System.IO.Path.Combine(Support, "server");

        public string ServerNew => System.IO.Path.Combine(Support, "server.new");

        public string TokenFile => System.IO.Path.Combine(Support, "access-token");

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
            return System.IO.Path.Combine(RepoFolder(), "scripts", "launch-agent.sh");
        }

        public static string UserId()
        {
            ScriptRun run = RunProcess("/usr/bin/id", ["-u"], null);
            Assert.Equal(0, run.ExitCode);
            return run.Output.Trim();
        }

        public ScriptRun Run(string command)
        {
            return Run(ScriptPath(), command);
        }

        public ScriptRun Run(string script, params string[] arguments)
        {
            Dictionary<string, string> environment = new()
            {
                ["HOME"] = Path,
                ["PATH"] = System.IO.Path.Combine(Path, "bin") + ":/usr/bin:/bin",
            };
            return RunProcess("/bin/sh", [script, .. arguments], environment);
        }

        public static ScriptRun RunProcess(
            string program, IEnumerable<string> arguments, Dictionary<string, string>? environment)
        {
            ProcessStartInfo start = new(program)
            {
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

        public void SetExit(string command, int code)
        {
            File.WriteAllText(System.IO.Path.Combine(Path, command + "-exit"), code.ToString(
                System.Globalization.CultureInfo.InvariantCulture));
        }

        public void FailPublish()
        {
            File.WriteAllText(System.IO.Path.Combine(Path, "dotnet-fail"), "");
        }

        public void PlantToken(string token)
        {
            Directory.CreateDirectory(Support);
            File.WriteAllText(TokenFile, token);
        }

        public IReadOnlyList<string> LaunchctlCalls()
        {
            return ReadLines("calls.log");
        }

        public IReadOnlyList<string> LaunchctlStates()
        {
            return ReadLines("states.log");
        }

        public IReadOnlyList<string> DotnetArguments()
        {
            return ReadLines("dotnet.log");
        }

        public IReadOnlyList<string> CurlCalls()
        {
            return ReadLines("curl.log");
        }

        /// <summary>Writes what the fake <c>curl</c> prints for <c>-w '%{redirect_url}'</c>.</summary>
        public void SetRedirect(string answer)
        {
            File.WriteAllText(System.IO.Path.Combine(Path, "redirect"), answer);
        }

        /// <summary>Leftover release work folders (<c>.release.*</c>) in the support folder.</summary>
        public IReadOnlyList<string> WorkFolders()
        {
            return Directory.Exists(Support) ? [.. Directory.GetDirectories(Support, ".release.*")] : [];
        }

        /// <summary>Builds <c>release/controlroom-&lt;version&gt;-osx-arm64.tar.gz</c> and <c>release/SHA256SUMS</c>
        /// with the real <c>/usr/bin/tar</c>, broken in the one given way.</summary>
        public void AddRelease(string version, ReleaseFault fault)
        {
            string top = "controlroom-" + version + "-osx-arm64";
            string stage = System.IO.Path.Combine(Path, "stage-" + Guid.NewGuid().ToString("N"));
            string folder = System.IO.Path.Combine(stage, top);
            Directory.CreateDirectory(System.IO.Path.Combine(folder, "wwwroot"));
            string server = System.IO.Path.Combine(folder, "ControlRoom.Server");
            File.WriteAllText(server, "#!/bin/sh\nexit 0\n");
            File.SetUnixFileMode(server, fault == ReleaseFault.NotExecutable
                ? UnixFileMode.UserRead | UnixFileMode.UserWrite
                : UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            File.WriteAllText(System.IO.Path.Combine(folder, "ControlRoom.Server.dll"), "dll");
            if (fault == ReleaseFault.TooManyEntries)
            {
                string many = System.IO.Path.Combine(folder, "many");
                Directory.CreateDirectory(many);
                for (int index = 0; index < 10000; index++)
                {
                    File.WriteAllBytes(System.IO.Path.Combine(many, index.ToString(
                        System.Globalization.CultureInfo.InvariantCulture)), []);
                }
            }

            if (fault == ReleaseFault.TooMuchContent)
            {
                // Real zeros (no holes), just over the 512 MiB cap; gzip shrinks it to well under a MiB.
                using FileStream big = File.Create(System.IO.Path.Combine(folder, "big"));
                byte[] block = new byte[1 << 20];
                for (int index = 0; index < 513; index++)
                {
                    big.Write(block);
                }
            }

            if (fault != ReleaseFault.NoIndexHtml)
            {
                File.WriteAllText(System.IO.Path.Combine(folder, "wwwroot", "index.html"), "<!doctype html>");
            }

            List<string> arguments = [];
            List<string> entries = [top];
            File.WriteAllText(System.IO.Path.Combine(stage, "evil"), "evil");
            switch (fault)
            {
                case ReleaseFault.SymlinkEntry:
                    File.CreateSymbolicLink(System.IO.Path.Combine(folder, "wwwroot", "link"), "/etc/hosts");
                    break;
                case ReleaseFault.HardLinkEntry:
                    Assert.Equal(0, RunProcess(
                        "/bin/ln", [server, System.IO.Path.Combine(folder, "hard")], null).ExitCode);
                    break;
                case ReleaseFault.DotDotEntry:
                    arguments.AddRange(["-s", ",^evil$," + top + "/../evil,"]);
                    entries.Add("evil");
                    break;
                case ReleaseFault.DotEntry:
                    arguments.AddRange(["-s", ",^evil$," + top + "/./evil,"]);
                    entries.Add("evil");
                    break;
                case ReleaseFault.AbsoluteEntry:
                    arguments.AddRange(["-P", "-s", ",^evil$,/" + top + "/evil,"]);
                    entries.Add("evil");
                    break;
                case ReleaseFault.OutsideEntry:
                    arguments.AddRange(["-s", ",^evil$,other/evil,"]);
                    entries.Add("evil");
                    break;
                case ReleaseFault.SecondTopFolder:
                    arguments.AddRange(["-s", ",^evil$," + top + "-x/evil,"]);
                    entries.Add("evil");
                    break;
                default:
                    break;
            }

            string release = System.IO.Path.Combine(Path, "release");
            Directory.CreateDirectory(release);
            string tarball = System.IO.Path.Combine(release, top + ".tar.gz");
            Dictionary<string, string> environment = new() { ["COPYFILE_DISABLE"] = "1" };
            ScriptRun tar = RunProcess(
                "/usr/bin/tar", ["-czf", tarball, .. arguments, "-C", stage, .. entries], environment);
            Assert.True(tar.ExitCode == 0, "tar: " + tar.Error);
            Directory.Delete(stage, recursive: true);

            string hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(tarball))).ToLowerInvariant();
            string name = top + ".tar.gz";
            string sums = fault switch
            {
                ReleaseFault.BadChecksum => new string('0', 64) + "  " + name + "\n",
                ReleaseFault.NoChecksumLine => hash + "  other.tar.gz\n",
                ReleaseFault.TwoChecksumLines => hash + "  " + name + "\n" + hash + "  " + name + "\n",
                ReleaseFault.UpperCaseChecksum => hash.ToUpperInvariant() + "  " + name + "\n",
                ReleaseFault.LargeChecksumFile => new string('#', 4096) + "\n" + hash + "  " + name + "\n",
                _ => "1111111111111111111111111111111111111111111111111111111111111111  other.tar.gz\n"
                    + hash + "  " + name + "\n",
            };
            File.WriteAllText(System.IO.Path.Combine(release, "SHA256SUMS"), sums);
        }

        public void RemoveReleaseFile(string name)
        {
            File.Delete(System.IO.Path.Combine(Path, "release", name));
        }

        /// <summary>Every file and folder under HOME (apart from the fakes' logs) with its mode and content
        /// hash, sorted ordinally.</summary>
        public IReadOnlyList<string> Snapshot()
        {
            List<string> entries = [];
            foreach (string entry in Directory.EnumerateFileSystemEntries(Path, "*", SearchOption.AllDirectories))
            {
                string relative = System.IO.Path.GetRelativePath(Path, entry);
                if (HarnessFiles.Contains(relative))
                {
                    continue;
                }

                FileInfo info = new(entry);
                string hash = info.Exists && info.LinkTarget is null
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

            Directory.Delete(Path, recursive: true);
        }

        private List<string> ReadLines(string name)
        {
            string file = System.IO.Path.Combine(Path, name);
            return File.Exists(file) ? [.. File.ReadAllLines(file)] : [];
        }

        private static void WriteExecutable(string path, string content)
        {
            File.WriteAllText(path, content);
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }
}
