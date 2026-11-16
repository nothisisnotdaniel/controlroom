using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.Cryptography;

namespace ControlRoom.Server.Tests;

/// <summary><c>scripts/launch-agent.sh</c>, run with <c>/bin/sh</c> in its own temp <c>HOME</c> with fake
/// <c>launchctl</c> and <c>dotnet</c> on <c>PATH</c>. No real launchctl, publish or network is used.</summary>
[UnsupportedOSPlatform("windows")]
public sealed class LaunchAgentScriptTests
{
    private const string Label = "local.controlroom.server";

    private const string FixedPath =
        "/usr/local/share/dotnet:/opt/homebrew/bin:/usr/local/bin:/usr/bin:/bin:/usr/sbin:/sbin";

    private const string Usage = "usage: scripts/launch-agent.sh install|uninstall|status\n";

    private const string PublishFailedText = "dotnet publish failed. Nothing was changed.\n";

    private const string SymlinkText = "Refusing to write through a symlink: ";

    private const string PlutilPath = "/usr/bin/plutil";

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
        Assert.Equal(["bootout gui/" + ScriptHome.UserId() + "/" + Label], home.LaunchctlCalls());
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
        Assert.Equal("installed: no\nrunning: no\nlog: " + home.Log + "\n", run.Output);
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
        Assert.Equal("installed: yes\nrunning: yes\nlog: " + home.Log + "\n", run.Output);
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

        private static readonly string[] HarnessFiles = ["calls.log", "states.log", "dotnet.log"];

        public ScriptHome()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), "controlroom-agent-" + Guid.NewGuid().ToString("N"));
            string bin = System.IO.Path.Combine(Path, "bin");
            Directory.CreateDirectory(bin);
            WriteExecutable(System.IO.Path.Combine(bin, "launchctl"), FakeLaunchctl);
            WriteExecutable(System.IO.Path.Combine(bin, "dotnet"), FakeDotnet);
        }

        public string Path { get; }

        public string Plist => System.IO.Path.Combine(Path, "Library", "LaunchAgents", Label + ".plist");

        public string Log => System.IO.Path.Combine(Path, "Library", "Logs", "ControlRoom", "server.log");

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
