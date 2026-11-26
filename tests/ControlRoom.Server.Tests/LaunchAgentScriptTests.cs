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
        "usage: scripts/launch-agent.sh install [--release [vX.Y.Z]] [--tailnet-host <name> [--tailnet-port <port>]] "
        + "[--factory-root <path>] [--push-subject <subject>]|uninstall|status\n";

    private const string Releases = "https://github.com/nothisisnotdaniel/controlroom/releases";

    private const string ListUrl = "https://api.github.com/repos/nothisisnotdaniel/controlroom/releases?per_page=100";

    /// <summary>A release list whose newest develop-channel tag is <c>v0.2.0-develop.7</c> (newer than the latest
    /// final release <c>v0.1.0</c>; the release candidate is not a channel tag).</summary>
    private const string DevelopList =
        "[{\"tag_name\":\"v0.2.0-develop.3\"},{\"tag_name\":\"v0.3.0-rc.1\"},{\"tag_name\":\"v0.2.0-develop.7\"},"
        + "{\"tag_name\":\"v0.1.0\"}]\n";

    private const string LatestFailedText = "Can't find the latest release. Nothing was changed.\n";

    private const string DownloadFailedText = "Download failed. Nothing was changed.\n";

    private const string ReleaseBadText = "The release download doesn't check out. Nothing was changed.\n";

    private const string SigningCertBadText =
        "signing-cert.sha1 must hold one 40-character SHA-1 fingerprint. Nothing was changed.\n";

    private const string PublishFailedText = "dotnet publish failed. Nothing was changed.\n";

    private const string SymlinkText = "Refusing to write through a symlink: ";

    private const string PlutilPath = "/usr/bin/plutil";

    private const string TailnetHostText = "--tailnet-host must be a name like mac.tailnet.ts.net. Nothing was changed.\n";

    private const string TailnetPortText = "--tailnet-port must be a port from 1 to 65535. Nothing was changed.\n";

    private const string PushSubjectText =
        "--push-subject must be mailto:<address> or https://<host>. Nothing was changed.\n";

    private const string FactoryRootText = "--factory-root must be an absolute path to a folder. Nothing was changed.\n";

    /// <summary><c>install</c> from the repo and its refusals.</summary>
    public sealed class Install
    {
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
                ["bootout gui/" + ScriptHome.UserId() + "/" + Label, "print gui/" + ScriptHome.UserId() + "/" + Label,
                    "bootstrap gui/" + ScriptHome.UserId() + " " + home.Plist],
                home.LaunchctlCalls());
            Assert.Equal(
                ["bootout new=yes server=yes plist=no", "print new=yes server=yes plist=no",
                    "bootstrap new=no server=yes plist=yes"],
                home.LaunchctlStates());
            Assert.Empty(home.SleepCalls());
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
        public void Install_WithATailnetPort_AddsItAfterTheTailnetHost()
        {
            if (OperatingSystem.IsWindows())
            {
                return;
            }

            using ScriptHome home = new();

            ScriptRun run = home.Run(
                ScriptHome.ScriptPath(), "install", "--tailnet-port", "8443", "--tailnet-host", "mac.tail1234.ts.net");

            Assert.Equal(0, run.ExitCode);
            Assert.Equal(
                ["--FactoryRoot", Directory.GetParent(ScriptHome.RepoFolder())!.FullName, "--TailnetHost",
                    "mac.tail1234.ts.net", "--TailnetPort", "8443"],
                ProgramArguments(home.Plist)[2..]);
            if (File.Exists(PlutilPath))
            {
                Assert.Equal(0, Plutil("-lint", home.Plist).ExitCode);
            }
        }

        [Fact]
        public void Install_WithAFactoryRoot_UsesItForTheServer()
        {
            if (OperatingSystem.IsWindows())
            {
                return;
            }

            using ScriptHome home = new();
            string root = Path.Combine(home.Path, "Some Factory");
            Directory.CreateDirectory(root);

            ScriptRun run = home.Run(ScriptHome.ScriptPath(), "install", "--factory-root", root);

            Assert.Equal(0, run.ExitCode);
            Assert.Equal(["--FactoryRoot", root], ProgramArguments(home.Plist)[2..]);
        }

        [Theory]
        [InlineData("0")]
        [InlineData("65536")]
        [InlineData("08443")]
        [InlineData("+8443")]
        [InlineData("8443x")]
        [InlineData("123456")]
        [InlineData("")]
        public void Install_WithABadTailnetPort_ExitsNonZeroAndChangesNothing(string port)
        {
            if (OperatingSystem.IsWindows())
            {
                return;
            }

            using ScriptHome home = new();
            IReadOnlyList<string> before = home.Snapshot();

            ScriptRun run = home.Run(
                ScriptHome.ScriptPath(), "install", "--tailnet-host", "mac.tail1234.ts.net", "--tailnet-port", port);

            Assert.NotEqual(0, run.ExitCode);
            Assert.Equal(TailnetPortText, run.Error);
            Assert.False(File.Exists(home.Plist));
            Assert.Empty(home.LaunchctlCalls());
            Assert.Empty(home.DotnetArguments());
            Assert.Equal(before, home.Snapshot());
        }

        [Theory]
        [InlineData("mailto:me@example.com")]
        [InlineData("mailto:first.last+push@mail.example.org")]
        [InlineData("https://example.com/contact")]
        public void Install_WithAPushSubject_AddsItLast(string subject)
        {
            if (OperatingSystem.IsWindows())
            {
                return;
            }

            using ScriptHome home = new();

            ScriptRun run = home.Run(
                ScriptHome.ScriptPath(), "install", "--push-subject", subject, "--tailnet-host", "mac.tail1234.ts.net",
                "--tailnet-port", "8443");

            Assert.Equal(0, run.ExitCode);
            Assert.Equal(
                ["--FactoryRoot", Directory.GetParent(ScriptHome.RepoFolder())!.FullName, "--TailnetHost",
                    "mac.tail1234.ts.net", "--TailnetPort", "8443", "--PushSubject", subject],
                ProgramArguments(home.Plist)[2..]);
            if (File.Exists(PlutilPath))
            {
                Assert.Equal(0, Plutil("-lint", home.Plist).ExitCode);
            }
        }

        [Theory]
        [InlineData("mailto:controlroom@localhost")]
        [InlineData("mailto:me@mail.localhost")]
        [InlineData("mailto:me@example")]
        [InlineData("mailto:@example.com")]
        [InlineData("mailto:me@")]
        [InlineData("mailto:me@example..com")]
        [InlineData("mailto:me@example.com\n")]
        [InlineData("mailto:me <me@example.com>")]
        [InlineData("mailto:a\"b@example.com")]
        [InlineData("me@example.com")]
        [InlineData("http://example.com")]
        [InlineData("https://")]
        [InlineData("https://exa mple.com")]
        [InlineData("https://example.com/<x>")]
        [InlineData("")]
        public void Install_WithABadPushSubject_ExitsNonZeroAndChangesNothing(string subject)
        {
            if (OperatingSystem.IsWindows())
            {
                return;
            }

            using ScriptHome home = new();
            IReadOnlyList<string> before = home.Snapshot();

            ScriptRun run = home.Run(ScriptHome.ScriptPath(), "install", "--push-subject", subject);

            Assert.NotEqual(0, run.ExitCode);
            Assert.Equal(PushSubjectText, run.Error);
            Assert.False(File.Exists(home.Plist));
            Assert.Empty(home.LaunchctlCalls());
            Assert.Empty(home.DotnetArguments());
            Assert.Equal(before, home.Snapshot());
        }

        [Theory]
        [InlineData("relative/root")]
        [InlineData("/no/such/factory-root-folder")]
        [InlineData("")]
        public void Install_WithABadFactoryRoot_ExitsNonZeroAndChangesNothing(string root)
        {
            if (OperatingSystem.IsWindows())
            {
                return;
            }

            using ScriptHome home = new();
            IReadOnlyList<string> before = home.Snapshot();

            ScriptRun run = home.Run(ScriptHome.ScriptPath(), "install", "--factory-root", root);

            Assert.NotEqual(0, run.ExitCode);
            Assert.Equal(FactoryRootText, run.Error);
            Assert.False(File.Exists(home.Plist));
            Assert.Empty(home.LaunchctlCalls());
            Assert.Empty(home.DotnetArguments());
            Assert.Equal(before, home.Snapshot());
        }

        [Theory]
        [InlineData("install --tailnet-port 8443")]
        [InlineData("install --tailnet-port")]
        [InlineData("install --factory-root")]
        [InlineData("install --tailnet-host mac.tail1234.ts.net --tailnet-port 8443 --tailnet-port 8443")]
        [InlineData("install --factory-root /tmp --factory-root /tmp")]
        [InlineData("status --tailnet-port 8443")]
        [InlineData("uninstall --factory-root /tmp")]
        [InlineData("install --push-subject")]
        [InlineData("install --push-subject mailto:me@example.com --push-subject mailto:me@example.com")]
        [InlineData("status --push-subject mailto:me@example.com")]
        public void BadPortOrRootArguments_PrintUsageExitTwoAndChangeNothing(string joined)
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
            Assert.Equal(
                ["bootout " + service, "print " + service, bootstrap, "bootout " + service, "print " + service, bootstrap],
                home.LaunchctlCalls());
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
        public void Install_WithALingeringServiceAndOneFailedBootstrap_WaitsRetriesAndSucceeds()
        {
            if (OperatingSystem.IsWindows())
            {
                return;
            }

            using ScriptHome home = new();
            home.SetLinger(2);
            home.FailBootstraps(1);

            ScriptRun run = home.Run("install");

            Assert.Equal(0, run.ExitCode);
            Assert.Equal("Installed: " + home.Plist + "\nLog: " + home.Log + "\n", run.Output);
            string service = "gui/" + ScriptHome.UserId() + "/" + Label;
            string bootstrap = "bootstrap gui/" + ScriptHome.UserId() + " " + home.Plist;
            Assert.Equal(
                ["bootout " + service, "print " + service, "print " + service, "print " + service, bootstrap, bootstrap],
                home.LaunchctlCalls());
            Assert.Equal(["0.5", "0.5", "1"], home.SleepCalls());
        }

        [Fact]
        public void InstallRelease_WithALingeringServiceAndFailedBootstraps_WaitsAndRetriesForServerAndUpdater()
        {
            if (OperatingSystem.IsWindows())
            {
                return;
            }

            using ScriptHome home = new();
            home.AddRelease("1.2.3", ReleaseFault.None);
            home.SetLinger(1);
            home.FailBootstraps(1);

            ScriptRun run = home.Run(home.ReleaseScript, "install", "--release", "v1.2.3");

            Assert.Equal(0, run.ExitCode);
            string user = ScriptHome.UserId();
            string server = "bootstrap gui/" + user + " " + home.Plist;
            string updater = "bootstrap gui/" + user + " " + home.UpdaterPlist;
            Assert.Equal(
                ["bootout gui/" + user + "/" + UpdaterLabel, "print gui/" + user + "/" + UpdaterLabel,
                    "print gui/" + user + "/" + UpdaterLabel,
                    "bootout gui/" + user + "/" + Label, "print gui/" + user + "/" + Label, "print gui/" + user + "/" + Label,
                    server, server, updater],
                home.LaunchctlCalls());
            Assert.Equal(["0.5", "0.5", "1"], home.SleepCalls());
            Assert.Equal("v1.2.3\n", File.ReadAllText(home.Marker));
        }

        [Fact]
        public void InstallRelease_WhenTheUpdaterBootstrapFailsOnce_RetriesIt()
        {
            if (OperatingSystem.IsWindows())
            {
                return;
            }

            using ScriptHome home = new();
            home.AddRelease("1.2.3", ReleaseFault.None);
            Assert.Equal(0, home.Run(home.ReleaseScript, "install", "--release", "v1.2.3").ExitCode);
            int before = home.LaunchctlCalls().Count;
            home.SetLinger(1);
            home.FailBootstraps(2);

            ScriptRun run = home.Run(home.ReleaseScript, "install", "--release", "v1.2.3");

            Assert.Equal(0, run.ExitCode);
            string user = ScriptHome.UserId();
            string server = "bootstrap gui/" + user + " " + home.Plist;
            string updater = "bootstrap gui/" + user + " " + home.UpdaterPlist;
            Assert.Equal([server, server, server], home.LaunchctlCalls().Skip(before).Where(call => call == server));
            Assert.Equal([updater], home.LaunchctlCalls().Skip(before).Where(call => call == updater));
            Assert.Equal(["0.5", "0.5", "1", "1"], home.SleepCalls());
        }

        [Fact]
        public void Install_WhenEveryBootstrapFails_EndsWithTheFixedMessageAfterSixAttempts()
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
            Assert.Equal(6, home.LaunchctlCalls().Count(call => call.StartsWith("bootstrap ", StringComparison.Ordinal)));
            Assert.Equal(Enumerable.Repeat("1", 5), home.SleepCalls());
        }

        [Fact]
        public void InstallRelease_WhenEveryUpdaterBootstrapFails_EndsWithTheFixedMessageAfterSixAttempts()
        {
            if (OperatingSystem.IsWindows())
            {
                return;
            }

            using ScriptHome home = new();
            home.AddRelease("1.2.3", ReleaseFault.None);
            home.FailBootstrapsOf("/" + UpdaterLabel + ".plist");

            ScriptRun run = home.Run(home.ReleaseScript, "install", "--release", "v1.2.3");

            Assert.Equal(1, run.ExitCode);
            Assert.EndsWith(
                "launchctl bootstrap failed. Check: launchctl print gui/" + ScriptHome.UserId() + "/" + UpdaterLabel + "\n",
                run.Error, StringComparison.Ordinal);
            string user = ScriptHome.UserId();
            Assert.Equal(1, home.LaunchctlCalls().Count(call => call == "bootstrap gui/" + user + " " + home.Plist));
            Assert.Equal(6, home.LaunchctlCalls().Count(call => call == "bootstrap gui/" + user + " " + home.UpdaterPlist));
        }

        [Fact]
        public void InstallRelease_WhenEveryServerBootstrapFails_EndsWithTheFixedMessageAfterSixAttempts()
        {
            if (OperatingSystem.IsWindows())
            {
                return;
            }

            using ScriptHome home = new();
            home.AddRelease("1.2.3", ReleaseFault.None);
            home.SetExit("bootstrap", 5);

            ScriptRun run = home.Run(home.ReleaseScript, "install", "--release", "v1.2.3");

            Assert.Equal(1, run.ExitCode);
            Assert.Equal(
                "launchctl bootstrap failed. Check: launchctl print gui/" + ScriptHome.UserId() + "/" + Label + "\n",
                run.Error);
            Assert.Equal(6, home.LaunchctlCalls().Count(call => call.StartsWith("bootstrap ", StringComparison.Ordinal)));
        }
    }

    /// <summary><c>uninstall</c>, <c>status</c>, the token and the release download.</summary>
    public sealed class UninstallAndStatus
    {
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
            Assert.Equal(
                ["bootout gui/" + ScriptHome.UserId() + "/" + Label, "print gui/" + ScriptHome.UserId() + "/" + Label],
                home.LaunchctlCalls().TakeLast(2));
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
                ["bootout gui/" + ScriptHome.UserId() + "/" + UpdaterLabel, "print gui/" + ScriptHome.UserId() + "/" + UpdaterLabel,
                    "bootout gui/" + ScriptHome.UserId() + "/" + Label, "print gui/" + ScriptHome.UserId() + "/" + Label],
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

            ScriptRun run = home.Run(home.ReleaseScript, "install", "--release", "v1.2.3");

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
                ["bootout new=yes server=yes plist=no", "print new=yes server=yes plist=no",
                    "bootout new=yes server=yes plist=no", "print new=yes server=yes plist=no",
                    "bootstrap new=no server=yes plist=yes", "bootstrap new=no server=yes plist=yes"],
                home.LaunchctlStates());
            Assert.True(File.Exists(Path.Combine(home.Server, "ControlRoom.Server")));
            Assert.True(File.Exists(Path.Combine(home.Server, "wwwroot", "index.html")));
            Assert.False(Directory.Exists(Path.Combine(home.Server, "old")));
            Assert.False(Directory.Exists(home.ServerNew));
            Assert.Empty(home.WorkFolders());
            Assert.Equal(
                [Path.Combine(home.Server, "ControlRoom.Server"), "--FactoryRoot",
                    home.TreeRoot],
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

            ScriptRun run = home.Run(home.ReleaseScript, "install", "--release", "v1.2.4-develop.7");

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

            ScriptRun run = home.Run(home.ReleaseScript, "install", "--release");

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
        [InlineData("{\"autoUpdate\":true,\"channel\":\"develop\"}\n")]
        [InlineData("{\"autoUpdate\":false,\"channel\":\"develop\"}\n")]
        [InlineData("{\"autoUpdate\":true,\"channel\":\"develop\"}")]
        [InlineData("{\"autoUpdate\":true,\"channel\":\"develop\",\"updateInterval\":30}\n")]
        [InlineData("{\"autoUpdate\":false,\"channel\":\"develop\",\"updateInterval\":720}\n")]
        [InlineData("{\"autoUpdate\":true,\"channel\":\"develop\",\"updateInterval\":45}")]
        public void InstallRelease_WithoutATagOnTheDevelopChannel_InstallsTheNewestDevelopBuild(string settings)
        {
            if (OperatingSystem.IsWindows())
            {
                return;
            }

            using ScriptHome home = new();
            home.SetSettings(settings);
            home.AddRelease("0.2.0-develop.7", ReleaseFault.None);
            home.SetList(DevelopList);
            home.SetRedirect(Releases + "/tag/v0.1.0");

            ScriptRun run = home.Run(home.ReleaseScript, "install", "--release");

            Assert.Equal(0, run.ExitCode);
            Assert.Equal(
                "Installed v0.2.0-develop.7: " + home.Plist + "\nLog: " + home.Log + "\nUpdater: " + home.UpdaterPlist + "\n",
                run.Output);
            IReadOnlyList<string> curl = home.CurlCalls();
            Assert.Equal(3, curl.Count);
            Assert.Equal("--proto =https --tlsv1.2 -fsS --max-time 30 --max-filesize 1048576 " + ListUrl, curl[0]);
            AssertDownload(home, curl[1], "v0.2.0-develop.7", "controlroom-0.2.0-develop.7-osx-arm64.tar.gz");
            AssertDownload(home, curl[2], "v0.2.0-develop.7", "SHA256SUMS");
            Assert.Equal("v0.2.0-develop.7\n", File.ReadAllText(home.Marker));
            Assert.Equal(settings, File.ReadAllText(home.Settings));
            Assert.Empty(home.WorkFolders());
        }

        [Theory]
        [InlineData("missing")]
        [InlineData("stable")]
        [InlineData("short")]
        [InlineData("two-line-breaks")]
        [InlineData("crlf")]
        [InlineData("other-key")]
        [InlineData("over-cap")]
        [InlineData("symlink")]
        [InlineData("folder")]
        [InlineData("stable-interval")]
        [InlineData("interval-leading-zero")]
        [InlineData("interval-string")]
        [InlineData("interval-fifth-key")]
        [InlineData("interval-key-order")]
        public void InstallRelease_WithoutATagAndNoDevelopSetting_UsesTheLatestRedirect(string kind)
        {
            if (OperatingSystem.IsWindows())
            {
                return;
            }

            using ScriptHome home = new();
            const string DevelopSetting = "{\"autoUpdate\":true,\"channel\":\"develop\"}";
            switch (kind)
            {
                case "missing":
                    break;
                case "stable":
                    home.SetSettings("{\"autoUpdate\":true,\"channel\":\"stable\"}\n");
                    break;
                case "short":
                    home.SetSettings("{\"autoUpdate\":true}\n");
                    break;
                case "two-line-breaks":
                    home.SetSettings(DevelopSetting + "\n\n");
                    break;
                case "crlf":
                    home.SetSettings(DevelopSetting + "\r\n");
                    break;
                case "other-key":
                    home.SetSettings("{\"autoUpdate\":\"yes\",\"channel\":\"develop\"}\n");
                    break;
                case "over-cap":
                    home.SetSettings(DevelopSetting + new string('\n', 64 - DevelopSetting.Length + 1));
                    break;
                case "stable-interval":
                    home.SetSettings("{\"autoUpdate\":true,\"channel\":\"stable\",\"updateInterval\":60}\n");
                    break;
                case "interval-leading-zero":
                    home.SetSettings("{\"autoUpdate\":true,\"channel\":\"develop\",\"updateInterval\":060}\n");
                    break;
                case "interval-string":
                    home.SetSettings("{\"autoUpdate\":true,\"channel\":\"develop\",\"updateInterval\":\"60\"}\n");
                    break;
                case "interval-fifth-key":
                    home.SetSettings("{\"autoUpdate\":true,\"channel\":\"develop\",\"updateInterval\":60,\"x\":1}\n");
                    break;
                case "interval-key-order":
                    home.SetSettings("{\"autoUpdate\":true,\"updateInterval\":60,\"channel\":\"develop\"}\n");
                    break;
                case "symlink":
                    string target = System.IO.Path.Combine(home.Path, "develop.json");
                    File.WriteAllText(target, DevelopSetting + "\n");
                    home.SetSettings(null);
                    File.CreateSymbolicLink(home.Settings, target);
                    break;
                default:
                    home.SetSettings(null);
                    Directory.CreateDirectory(home.Settings);
                    break;
            }

            home.AddRelease("0.1.0", ReleaseFault.None);
            home.SetList(DevelopList);
            home.SetRedirect(Releases + "/tag/v0.1.0");

            ScriptRun run = home.Run(home.ReleaseScript, "install", "--release");

            Assert.Equal(0, run.ExitCode);
            IReadOnlyList<string> curl = home.CurlCalls();
            Assert.Equal(3, curl.Count);
            Assert.Equal(
                "--proto =https --tlsv1.2 -fsS --max-time 30 -o /dev/null -w %{redirect_url} " + Releases + "/latest",
                curl[0]);
            Assert.Equal("v0.1.0\n", File.ReadAllText(home.Marker));
        }

        [Fact]
        public void InstallRelease_WithATagOnTheDevelopChannel_InstallsThatTag()
        {
            if (OperatingSystem.IsWindows())
            {
                return;
            }

            using ScriptHome home = new();
            home.SetSettings("{\"autoUpdate\":true,\"channel\":\"develop\"}\n");
            home.AddRelease("0.1.0", ReleaseFault.None);
            home.SetList(DevelopList);

            ScriptRun run = home.Run(home.ReleaseScript, "install", "--release", "v0.1.0");

            Assert.Equal(0, run.ExitCode);
            IReadOnlyList<string> curl = home.CurlCalls();
            Assert.Equal(2, curl.Count);
            AssertDownload(home, curl[0], "v0.1.0", "controlroom-0.1.0-osx-arm64.tar.gz");
            Assert.Equal("v0.1.0\n", File.ReadAllText(home.Marker));
        }

        [Fact]
        public void InstallRelease_OnTheDevelopChannelWithNoListAnswer_IsLatestFailedAndChangesNothing()
        {
            if (OperatingSystem.IsWindows())
            {
                return;
            }

            using ScriptHome home = new();
            home.SetSettings("{\"autoUpdate\":true,\"channel\":\"develop\"}\n");
            home.AddRelease("0.1.0", ReleaseFault.None);
            home.SetRedirect(Releases + "/tag/v0.1.0");

            ScriptRun run = home.Run(home.ReleaseScript, "install", "--release");

            Assert.Equal(1, run.ExitCode);
            Assert.Equal(LatestFailedText, run.Error);
            Assert.Single(home.CurlCalls());
            Assert.Empty(home.LaunchctlCalls());
            Assert.False(File.Exists(home.Marker));
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

            ScriptRun run = home.Run(home.ReleaseScript, "install", "--release", "v" + version);

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

            ScriptRun run = home.Run(home.ReleaseScript, joined.Split(' '));

            Assert.Equal(0, run.ExitCode);
            Assert.Equal(
                [Path.Combine(home.Server, "ControlRoom.Server"), "--FactoryRoot",
                    home.TreeRoot, "--TailnetHost", "mac.tail1234.ts.net"],
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
            Assert.Equal(0, home.Run(home.ReleaseScript, "install", "--release", "v1.2.3").ExitCode);

            ScriptRun run = home.Run("install");

            Assert.Equal(0, run.ExitCode);
            Assert.Equal("Installed: " + home.Plist + "\nLog: " + home.Log + "\n", run.Output);
            Assert.Equal(
                [Path.Combine(home.Path, "bin", "dotnet"), Path.Combine(home.Server, "ControlRoom.Server.dll"),
                    "--FactoryRoot", Directory.GetParent(ScriptHome.RepoFolder())!.FullName],
                ProgramArguments(home.Plist));
            Assert.False(File.Exists(Path.Combine(home.Server, "ControlRoom.Server")));
        }
    }

    /// <summary>The checks of <c>install --release</c> and the updater it installs.</summary>
    public sealed class ReleaseChecks
    {
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

            ScriptRun run = home.Run(home.ReleaseScript, "install", "--release", tag);

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

            ScriptRun run = home.Run(home.ReleaseScript, joined.Split(' '));

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

            ScriptRun run = home.Run(home.ReleaseScript, "install", "--release", "v1.2.3");

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

            ScriptRun run = home.Run(home.ReleaseScript, "install", "--release", "v1.2.3");

            Assert.Equal(0, run.ExitCode);
            string user = ScriptHome.UserId();
            Assert.Equal(
                ["bootout gui/" + user + "/" + UpdaterLabel, "print gui/" + user + "/" + UpdaterLabel,
                    "bootout gui/" + user + "/" + Label, "print gui/" + user + "/" + Label,
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
                ["/bin/sh", home.UpdaterScript, "--factory-root", home.TreeRoot, "--start-delay", "60"],
                ProgramArguments(home.UpdaterPlist));
            string plist = File.ReadAllText(home.UpdaterPlist);
            Assert.Equal(ScriptFixtures.UpdaterPlist(home.UpdaterScript, home.TreeRoot, home.UpdaterLog), plist);
            Assert.Contains("<key>RunAtLoad</key>\n    <true/>\n", plist, StringComparison.Ordinal);
            Assert.DoesNotContain("KeepAlive", plist, StringComparison.Ordinal);
            Assert.Empty(Directory.GetFiles(home.Support, ".controlroom.*"));
            Assert.Empty(Directory.GetFiles(System.IO.Path.GetDirectoryName(home.UpdaterPlist)!, ".controlroom.*"));
            if (File.Exists(PlutilPath))
            {
                Assert.Equal(0, Plutil("-lint", home.UpdaterPlist).ExitCode);
                Assert.Equal(UpdaterLabel, Extract(home.UpdaterPlist, "Label"));
                Assert.Equal("900", Extract(home.UpdaterPlist, "StartInterval"));
                Assert.Equal("true", Extract(home.UpdaterPlist, "RunAtLoad"));
                Assert.Equal(FixedPath, Extract(home.UpdaterPlist, "EnvironmentVariables.PATH"));
                Assert.Equal(home.UpdaterLog, Extract(home.UpdaterPlist, "StandardOutPath"));
                Assert.Equal(home.UpdaterLog, Extract(home.UpdaterPlist, "StandardErrorPath"));
                Assert.Equal("6", Extract(home.UpdaterPlist, "ProgramArguments"));
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
            Assert.Equal(0, home.Run(home.ReleaseScript, "install", "--release", "v1.2.3").ExitCode);
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
            Assert.Equal(0, home.Run(home.ReleaseScript, "install", "--release", "v1.2.3").ExitCode);
            int before = home.LaunchctlCalls().Count;

            ScriptRun run = home.Run("install");

            Assert.Equal(0, run.ExitCode);
            string user = ScriptHome.UserId();
            Assert.Equal(
                ["bootout gui/" + user + "/" + UpdaterLabel, "print gui/" + user + "/" + UpdaterLabel,
                    "bootout gui/" + user + "/" + Label, "print gui/" + user + "/" + Label,
                    "bootstrap gui/" + user + " " + home.Plist],
                home.LaunchctlCalls().Skip(before));
            Assert.False(File.Exists(home.UpdaterPlist));
            Assert.False(File.Exists(home.UpdaterScript));
            Assert.False(File.Exists(home.Marker));
        }

        [Theory]
        [InlineData("install")]
        [InlineData("install --release v1.2.3")]
        [InlineData("uninstall")]
        public void InstallOrUninstall_RemovesTheLastCheckStamp(string joined)
        {
            if (OperatingSystem.IsWindows())
            {
                return;
            }

            using ScriptHome home = new();
            home.AddRelease("1.2.3", ReleaseFault.None);
            Assert.Equal(0, home.Run(home.ReleaseScript, "install", "--release", "v1.2.3").ExitCode);
            string stamp = System.IO.Path.Combine(home.Support, "last-check");
            File.WriteAllText(stamp, "1800000000\n");
            File.SetUnixFileMode(stamp, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            string[] arguments = joined.Split(' ');

            ScriptRun run = home.Run(home.ReleaseScript, arguments);

            Assert.Equal(0, run.ExitCode);
            Assert.False(File.Exists(stamp));
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
            Assert.Equal(0, home.Run(home.ReleaseScript, "install", "--release", "v1.2.3").ExitCode);
            Directory.CreateDirectory(home.Previous);
            File.WriteAllText(System.IO.Path.Combine(home.Previous, "ControlRoom.Server"), "old");

            ScriptRun run = home.Run("uninstall");

            Assert.Equal(0, run.ExitCode);
            string user = ScriptHome.UserId();
            Assert.Equal(
                ["bootout gui/" + user + "/" + UpdaterLabel, "print gui/" + user + "/" + UpdaterLabel,
                    "bootout gui/" + user + "/" + Label, "print gui/" + user + "/" + Label],
                home.LaunchctlCalls().TakeLast(4));
            Assert.False(File.Exists(home.UpdaterPlist));
            Assert.False(File.Exists(home.UpdaterScript));
            Assert.False(File.Exists(home.Marker));
            Assert.False(Directory.Exists(home.Previous));
            Assert.False(Directory.Exists(home.Server));
        }

        [Theory]
        [InlineData("uninstall")]
        [InlineData("install")]
        [InlineData("install --release v1.2.3")]
        public void InstallOrUninstall_RemovesTheUpdaterPreviousCopies(string joined)
        {
            if (OperatingSystem.IsWindows())
            {
                return;
            }

            using ScriptHome home = new();
            home.AddRelease("1.2.3", ReleaseFault.None);
            Assert.Equal(0, home.Run(home.ReleaseScript, "install", "--release", "v1.2.3").ExitCode);
            File.WriteAllText(home.UpdaterScriptPrevious, "#!/bin/sh\n# old updater\n");
            File.WriteAllText(home.UpdaterPlistPrevious, "<plist>old</plist>\n");

            ScriptRun run = home.Run(home.ReleaseScript, joined.Split(' '));

            Assert.Equal(0, run.ExitCode);
            Assert.False(File.Exists(home.UpdaterScriptPrevious));
            Assert.False(File.Exists(home.UpdaterPlistPrevious));
        }

        [Theory]
        [InlineData("uninstall", "script")]
        [InlineData("uninstall", "plist")]
        [InlineData("install", "script")]
        [InlineData("install --release v1.2.3", "plist")]
        public void InstallOrUninstall_RefusesASymlinkedUpdaterPreviousCopy(string joined, string which)
        {
            if (OperatingSystem.IsWindows())
            {
                return;
            }

            using ScriptHome home = new();
            home.AddRelease("1.2.3", ReleaseFault.None);
            Directory.CreateDirectory(home.Support);
            string target = System.IO.Path.Combine(home.Path, "elsewhere");
            File.WriteAllText(target, "keep");
            string link = which == "script" ? home.UpdaterScriptPrevious : home.UpdaterPlistPrevious;
            File.CreateSymbolicLink(link, target);

            ScriptRun run = home.Run(home.ReleaseScript, joined.Split(' '));

            Assert.Equal(1, run.ExitCode);
            Assert.Equal(SymlinkText + link + "\n", run.Error);
            Assert.Equal("keep", File.ReadAllText(target));
            Assert.Empty(home.LaunchctlCalls());
            Assert.NotNull(new FileInfo(link).LinkTarget);
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

            ScriptRun run = home.Run(home.ReleaseScript, "install", "--release", "v1.2.3");

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

            ScriptRun run = home.Run(home.ReleaseScript, joined.Split(' '));

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
    }

    /// <summary>The signature check of <c>install --release</c>.</summary>
    public sealed class Signing
    {
        [Fact]
        public void InstallRelease_WithSigningOff_RunsNoCodesignAndRemovesTheSupportCopy()
        {
            if (OperatingSystem.IsWindows())
            {
                return;
            }

            using ScriptHome home = new();
            home.AddRelease("1.2.3", ReleaseFault.None);
            Directory.CreateDirectory(home.Support);
            File.WriteAllText(home.SigningCopy, ScriptFixtures.Fingerprint + "\n");

            ScriptRun run = home.Run(home.ReleaseScript, "install", "--release", "v1.2.3");

            Assert.Equal(0, run.ExitCode);
            Assert.Equal("", run.Error);
            Assert.Empty(home.CodesignCalls());
            Assert.False(File.Exists(home.SigningCopy));
            Assert.Equal("v1.2.3\n", File.ReadAllText(home.Marker));
            Assert.True(File.Exists(Path.Combine(home.Server, "ControlRoom.Server")));
        }

        [Theory]
        [InlineData(ScriptFixtures.Fingerprint + "\n")]
        [InlineData(ScriptFixtures.Fingerprint)]
        [InlineData("0123456789ABCDEF0123456789abcdefABCDEF01\n")]
        public void InstallRelease_WithAValidFingerprint_VerifiesEveryTopLevelNativeFileAndWritesTheSupportCopy(
            string content)
        {
            if (OperatingSystem.IsWindows())
            {
                return;
            }

            using ScriptHome home = new();
            home.AddRelease("1.2.3", ReleaseFault.None);
            home.SetSigningFile(content);
            string fingerprint = content.TrimEnd('\n');

            ScriptRun run = home.Run(home.ReleaseScript, "install", "--release", "v1.2.3");

            Assert.Equal(0, run.ExitCode);
            Assert.Equal("", run.Error);
            IReadOnlyList<string[]> calls = home.CodesignCalls();
            string[] expected = ["ControlRoom.Server", "libclrjit.dylib", "libcoreclr.dylib", "createdump"];
            Assert.Equal(expected.Length, calls.Count);
            for (int index = 0; index < expected.Length; index++)
            {
                string requirement = index == 0
                    ? ScriptFixtures.ServerRequirement(fingerprint)
                    : ScriptFixtures.LibraryRequirement(fingerprint);
                Assert.Equal(4, calls[index].Length);
                Assert.Equal(["--verify", "--strict", requirement], calls[index][..3]);
                string file = calls[index][3];
                Assert.Equal(
                    Path.Combine("out", "controlroom-1.2.3-osx-arm64", expected[index]),
                    Path.GetRelativePath(Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(file)!)!)!, file));
                Assert.StartsWith(Path.Combine(home.Support, ".release."), file, StringComparison.Ordinal);
            }

            Assert.Equal(fingerprint + "\n", File.ReadAllText(home.SigningCopy));
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(home.SigningCopy));
            Assert.Empty(Directory.GetFiles(home.Support, ".controlroom.*"));
            Assert.Equal("v1.2.3\n", File.ReadAllText(home.Marker));
        }

        [Theory]
        [InlineData("0123456789abcdef0123456789abcdef0123456")]
        [InlineData("0123456789abcdef0123456789abcdef012345678")]
        [InlineData(ScriptFixtures.Fingerprint + "\n\n")]
        [InlineData(ScriptFixtures.Fingerprint + "\r\n")]
        [InlineData(ScriptFixtures.Fingerprint + " ")]
        [InlineData(ScriptFixtures.Fingerprint + "\nx")]
        [InlineData("0123456789abcdef0123456789abcdef0123456g")]
        [InlineData(" 0123456789abcdef0123456789abcdef0123456")]
        [InlineData("")]
        [InlineData("\n")]
        [InlineData("symlink")]
        [InlineData("folder")]
        public void InstallRelease_WithAnInvalidFingerprintFile_FailsBeforeAnyDownload(string content)
        {
            if (OperatingSystem.IsWindows())
            {
                return;
            }

            using ScriptHome home = new();
            home.AddRelease("1.2.3", ReleaseFault.None);
            if (content == "symlink")
            {
                string target = Path.Combine(home.Path, "real-fingerprint");
                File.WriteAllText(target, ScriptFixtures.Fingerprint + "\n");
                File.CreateSymbolicLink(home.SigningFile, target);
            }
            else if (content == "folder")
            {
                Directory.CreateDirectory(home.SigningFile);
            }
            else
            {
                home.SetSigningFile(content);
            }

            AssertReleaseRefused(home, ["install", "--release", "v1.2.3"], SigningCertBadText);
            Assert.Empty(home.CurlCalls());
            Assert.Empty(home.CodesignCalls());
        }

        [Theory]
        [InlineData("all")]
        [InlineData("ControlRoom.Server")]
        [InlineData("libcoreclr.dylib")]
        [InlineData("createdump")]
        public void InstallRelease_WhenTheSignatureCheckFails_ChangesNothing(string failing)
        {
            if (OperatingSystem.IsWindows())
            {
                return;
            }

            using ScriptHome home = new();
            home.AddRelease("1.2.3", ReleaseFault.None);
            Assert.Equal(0, home.Run(home.ReleaseScript, "install", "--release", "v1.2.3").ExitCode);
            home.AddRelease("1.2.4", ReleaseFault.None);
            home.SetSigningFile(ScriptFixtures.Fingerprint + "\n");
            if (failing == "all")
            {
                home.SetCodesignExit(1);
            }
            else
            {
                home.FailCodesignFor("/" + failing);
            }

            AssertReleaseRefused(home, ["install", "--release", "v1.2.4"], ReleaseBadText);
            Assert.NotEmpty(home.CodesignCalls());
            Assert.Equal("v1.2.3\n", File.ReadAllText(home.Marker));
            Assert.False(File.Exists(home.SigningCopy));
            Assert.True(File.Exists(home.UpdaterScript));
        }

        [Fact]
        public void InstallRelease_WithSigningOnAndNoCodesignOnPath_ChangesNothing()
        {
            if (OperatingSystem.IsWindows())
            {
                return;
            }

            using ScriptHome home = new();
            home.AddRelease("1.2.3", ReleaseFault.None);
            Assert.Equal(0, home.Run(home.ReleaseScript, "install", "--release", "v1.2.3").ExitCode);
            home.AddRelease("1.2.4", ReleaseFault.None);
            home.SetSigningFile(ScriptFixtures.Fingerprint + "\n");
            home.RemoveFakeCodesign();

            AssertReleaseRefused(home, ["install", "--release", "v1.2.4"], ReleaseBadText, withoutCodesign: true);
            Assert.Empty(home.CodesignCalls());
            Assert.Equal("v1.2.3\n", File.ReadAllText(home.Marker));
        }

        [Fact]
        public void InstallRelease_RefusesASymlinkedSupportFingerprintCopy()
        {
            if (OperatingSystem.IsWindows())
            {
                return;
            }

            using ScriptHome home = new();
            home.AddRelease("1.2.3", ReleaseFault.None);
            home.SetSigningFile(ScriptFixtures.Fingerprint + "\n");
            Directory.CreateDirectory(home.Support);
            string target = Path.Combine(home.Path, "elsewhere");
            File.WriteAllText(target, "keep");
            File.CreateSymbolicLink(home.SigningCopy, target);

            ScriptRun run = home.Run(home.ReleaseScript, "install", "--release", "v1.2.3");

            Assert.Equal(1, run.ExitCode);
            Assert.Equal(SymlinkText + home.SigningCopy + "\n", run.Error);
            Assert.Empty(home.CurlCalls());
            Assert.Empty(home.LaunchctlCalls());
            Assert.Equal("keep", File.ReadAllText(target));
        }
    }

    /// <summary>Runs a release install that must fail with <paramref name="text"/>: exit 1, no output, no new
    /// launchctl call (so no <c>bootout</c>), no dotnet, the work folder gone and HOME as before.</summary>
    private static void AssertReleaseRefused(
        ScriptHome home, string[] arguments, string text, bool withoutCodesign = false)
    {
        IReadOnlyList<string> before = home.Snapshot();
        int calls = home.LaunchctlCalls().Count;
        int publishes = home.DotnetArguments().Count;

        ScriptRun run = withoutCodesign
            ? home.RunWithoutCodesign(home.ReleaseScript, arguments)
            : home.Run(home.ReleaseScript, arguments);

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

        /// <summary>Logs its arguments and the folder state. <c>bootout</c> marks the service gone and copies
        /// <c>$HOME/linger</c> to <c>linger-left</c>; <c>bootstrap</c> fails (stderr, exit 5) while the
        /// <c>bootstrap-fails</c> counter is above 0 (counting it down) or always for a plist ending in
        /// <c>$HOME/bootstrap-fail-for</c>, else clears the mark; <c>$HOME/&lt;command&gt;-exit</c>
        /// overrides the exit code. <c>print</c> fails (exit 113) from a bootout until the next bootstrap, apart from
        /// <c>linger-left</c> more calls (counted down).</summary>
        private const string FakeLaunchctl =
            "#!/bin/sh\n"
            + "printf '%s\\n' \"$*\" >> \"$HOME/calls.log\"\n"
            + "support=\"$HOME/Library/Application Support/ControlRoom\"\n"
            + "yn() { if [ -e \"$1\" ]; then printf yes; else printf no; fi; }\n"
            + "printf '%s new=%s server=%s plist=%s\\n' \"$1\" \"$(yn \"$support/server.new\")\" "
            + "\"$(yn \"$support/server\")\" \"$(yn \"$HOME/Library/LaunchAgents/local.controlroom.server.plist\")\" "
            + ">> \"$HOME/states.log\"\n"
            + "count() { if [ -f \"$HOME/$1\" ]; then cat \"$HOME/$1\"; else echo 0; fi; }\n"
            + "case $1 in\n"
            + "  bootout)\n"
            + "    : > \"$HOME/gone\"\n"
            + "    if [ -f \"$HOME/linger\" ]; then cat \"$HOME/linger\" > \"$HOME/linger-left\"; fi ;;\n"
            + "  bootstrap)\n"
            + "    n=$(count bootstrap-fails)\n"
            + "    if [ \"$n\" -gt 0 ]; then\n"
            + "      echo $((n - 1)) > \"$HOME/bootstrap-fails\"\n"
            + "      echo 'Bootstrap failed: 5: Input/output error' >&2; exit 5\n"
            + "    fi\n"
            + "    if [ -f \"$HOME/bootstrap-fail-for\" ]; then\n"
            + "      case $3 in *\"$(cat \"$HOME/bootstrap-fail-for\")\") echo 'Bootstrap failed: 5: Input/output error' >&2; exit 5 ;; esac\n"
            + "    fi\n"
            + "    rm -f \"$HOME/gone\" ;;\n"
            + "esac\n"
            + "if [ -f \"$HOME/$1-exit\" ]; then exit \"$(cat \"$HOME/$1-exit\")\"; fi\n"
            + "if [ \"$1\" = print ] && [ -f \"$HOME/gone\" ]; then\n"
            + "  n=$(count linger-left)\n"
            + "  if [ \"$n\" -gt 0 ]; then echo $((n - 1)) > \"$HOME/linger-left\"; exit 0; fi\n"
            + "  exit 113\n"
            + "fi\n"
            + "exit 0\n";

        /// <summary>Logs its arguments and returns at once: the fake clock.</summary>
        private const string FakeSleep =
            "#!/bin/sh\n"
            + "printf '%s\\n' \"$*\" >> \"$HOME/sleep.log\"\n"
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

        /// <summary>Logs its arguments as one line; for the release list URL prints <c>$HOME/list</c> (exit 22 when
        /// missing); for <c>-w</c> prints <c>$HOME/redirect</c> (exit 22 when
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
            + "if [ \"$url\" = '" + ListUrl + "' ]; then\n"
            + "  if [ -f \"$HOME/list\" ]; then cat \"$HOME/list\"; exit 0; fi\n"
            + "  exit 22\n"
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
            ["calls.log", "states.log", "dotnet.log", "curl.log", "sleep.log", "gone", "linger", "linger-left",
                "bootstrap-fails", "bootstrap-fail-for", "codesign.log", "codesign-exit", "codesign-fail-for"];

        public ScriptHome()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), "controlroom-agent-" + Guid.NewGuid().ToString("N"));
            string bin = System.IO.Path.Combine(Path, "bin");
            Directory.CreateDirectory(bin);
            WriteExecutable(System.IO.Path.Combine(bin, "launchctl"), FakeLaunchctl);
            WriteExecutable(System.IO.Path.Combine(bin, "dotnet"), FakeDotnet);
            WriteExecutable(System.IO.Path.Combine(bin, "curl"), FakeCurl);
            WriteExecutable(System.IO.Path.Combine(bin, "sleep"), FakeSleep);
            WriteExecutable(System.IO.Path.Combine(bin, "codesign"), ScriptFixtures.FakeCodesign);
            ReleaseScript = ScriptFixtures.BuildReleaseTree(Path, RepoFolder());
        }

        /// <summary>The copy of <c>launch-agent.sh</c> in this home's own tree (<c>tree/ControlRoom/scripts/</c>),
        /// which every <c>install --release</c> test runs, so the real repo's <c>signing-cert.sha1</c> is never
        /// read.</summary>
        public string ReleaseScript { get; }

        /// <summary>The factory root the <see cref="ReleaseScript"/> copy sees.</summary>
        public string TreeRoot => System.IO.Path.Combine(Path, "tree");

        public string SigningFile => System.IO.Path.Combine(TreeRoot, "ControlRoom", "signing-cert.sha1");

        public string SigningCopy => System.IO.Path.Combine(Support, "signing-cert.sha1");

        /// <summary>Writes the tree's <c>signing-cert.sha1</c> (or removes it for <c>null</c>).</summary>
        public void SetSigningFile(string? content)
        {
            if (content is null)
            {
                File.Delete(SigningFile);
                return;
            }

            File.WriteAllText(SigningFile, content);
        }

        /// <summary>Every fake <c>codesign</c> call, its arguments split at tabs.</summary>
        public IReadOnlyList<string[]> CodesignCalls()
        {
            return [.. ReadLines("codesign.log").Select(line => line.Split('\t'))];
        }

        /// <summary>The fake <c>codesign</c> exits with <paramref name="code"/>.</summary>
        public void SetCodesignExit(int code)
        {
            File.WriteAllText(System.IO.Path.Combine(Path, "codesign-exit"),
                code.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\n");
        }

        /// <summary>The fake <c>codesign</c> fails for a file whose path ends with <paramref name="suffix"/>.</summary>
        public void FailCodesignFor(string suffix)
        {
            File.WriteAllText(System.IO.Path.Combine(Path, "codesign-fail-for"), suffix);
        }

        public string Path { get; }

        public string Plist => System.IO.Path.Combine(Path, "Library", "LaunchAgents", Label + ".plist");

        public string Log => System.IO.Path.Combine(Path, "Library", "Logs", "ControlRoom", "server.log");

        public string UpdaterPlist => System.IO.Path.Combine(Path, "Library", "LaunchAgents", UpdaterLabel + ".plist");

        public string UpdaterLog => System.IO.Path.Combine(Path, "Library", "Logs", "ControlRoom", "updater.log");

        public string UpdaterScript => System.IO.Path.Combine(Support, "updater.sh");

        public string Marker => System.IO.Path.Combine(Support, "installed-release");

        public string UpdaterScriptPrevious => System.IO.Path.Combine(Support, "updater.sh.previous");

        public string UpdaterPlistPrevious => System.IO.Path.Combine(Support, "updater.plist.previous");

        public string Settings => System.IO.Path.Combine(Support, "settings.json");

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
            return RunWithPath(System.IO.Path.Combine(Path, "bin") + ":/usr/bin:/bin", script, arguments);
        }

        /// <summary>Removes the fake <c>codesign</c> from <c>bin/</c>, for <see cref="RunWithoutCodesign"/>.</summary>
        public void RemoveFakeCodesign()
        {
            File.Delete(System.IO.Path.Combine(Path, "bin", "codesign"));
        }

        /// <summary>Runs <paramref name="script"/> with the fakes' <c>bin/</c> first, then only a folder of links
        /// to the system programs without <c>codesign</c>.</summary>
        public ScriptRun RunWithoutCodesign(string script, params string[] arguments)
        {
            // Outside HOME, so it never shows up in Snapshot(); removed on dispose.
            string system = Path + "-system-bin";
            ScriptFixtures.LinkSystemPrograms(system, "codesign");
            return RunWithPath(System.IO.Path.Combine(Path, "bin") + ":" + system, script, arguments);
        }

        private ScriptRun RunWithPath(string path, string script, string[] arguments)
        {
            Dictionary<string, string> environment = new()
            {
                ["HOME"] = Path,
                ["PATH"] = path,
                // Must never turn the check on or off: only signing-cert.sha1 does.
                ["SIGNING_CERT_SHA1"] = "ffffffffffffffffffffffffffffffffffffffff",
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

        /// <summary>After each bootout, <c>launchctl print</c> still shows the service for <paramref name="calls"/>
        /// more calls.</summary>
        public void SetLinger(int calls)
        {
            File.WriteAllText(System.IO.Path.Combine(Path, "linger"),
                calls.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\n");
        }

        /// <summary>The next <paramref name="count"/> bootstraps fail with exit 5.</summary>
        public void FailBootstraps(int count)
        {
            File.WriteAllText(System.IO.Path.Combine(Path, "bootstrap-fails"),
                count.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\n");
        }

        /// <summary>Every bootstrap of a plist whose path ends with <paramref name="suffix"/> fails with exit 5.</summary>
        public void FailBootstrapsOf(string suffix)
        {
            File.WriteAllText(System.IO.Path.Combine(Path, "bootstrap-fail-for"), suffix);
        }

        public IReadOnlyList<string> SleepCalls()
        {
            return ReadLines("sleep.log");
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

        /// <summary>Writes <c>settings.json</c> (<c>0600</c>, support folder <c>0700</c>), or removes it for
        /// <c>null</c>.</summary>
        public void SetSettings(string? content)
        {
            Directory.CreateDirectory(Support);
            File.SetUnixFileMode(Support, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            if (content is null)
            {
                File.Delete(Settings);
                return;
            }

            File.WriteAllText(Settings, content);
            File.SetUnixFileMode(Settings, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        /// <summary>Writes what the fake <c>curl</c> prints for the develop channel's release list.</summary>
        public void SetList(string json)
        {
            File.WriteAllText(System.IO.Path.Combine(Path, "list"), json);
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
            // Native files like a real publish: verify_signature checks the top-level ones, not wwwroot/nested.dylib.
            File.WriteAllText(System.IO.Path.Combine(folder, "libcoreclr.dylib"), "dylib");
            File.WriteAllText(System.IO.Path.Combine(folder, "libclrjit.dylib"), "dylib");
            File.WriteAllText(System.IO.Path.Combine(folder, "createdump"), "createdump");
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

            File.WriteAllText(System.IO.Path.Combine(folder, "wwwroot", "nested.dylib"), "not checked");

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
            if (Directory.Exists(Path + "-system-bin"))
            {
                Directory.Delete(Path + "-system-bin", recursive: true);
            }
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
