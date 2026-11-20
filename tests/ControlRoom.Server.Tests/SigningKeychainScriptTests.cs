using System.Diagnostics;
using System.Runtime.Versioning;

namespace ControlRoom.Server.Tests;

/// <summary><c>.github/scripts/signing-keychain.sh</c>, run with <c>/bin/sh</c> and a fake <c>security</c> first on
/// <c>PATH</c> that logs its arguments, prints a canned output and exits with a set code.</summary>
[UnsupportedOSPlatform("windows")]
public sealed class SigningKeychainScriptTests
{
    private const string Sha1 = "abcdef0123456789abcdef0123456789abcdef01";
    private const string Login = "/a/login.keychain-db";
    private const string Spaced = "/b/with space.keychain-db";
    private const string Keychain = "/tmp/run/signing.keychain-db";

    [Fact]
    public void Add_PutsTheKeychainFirstAndKeepsTheExistingOnesWhole()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using Fake fake = new();
        fake.ListOutput = "    \"" + Login + "\"\n    \"" + Spaced + "\"\n";

        Result result = fake.Run("add", Keychain);

        Assert.Equal(0, result.Exit);
        Assert.Equal("", result.Stdout);
        Assert.Equal("", result.Stderr);
        Assert.Equal(
            [["list-keychains", "-d", "user"], ["list-keychains", "-d", "user", "-s", Keychain, Login, Spaced]],
            fake.Calls());
    }

    [Fact]
    public void Add_PassesAKeychainAlreadyOnTheListOnlyOnceAndFirst()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using Fake fake = new();
        fake.ListOutput = "    \"" + Login + "\"\n    \"" + Keychain + "\"\n";

        Result result = fake.Run("add", Keychain);

        Assert.Equal(0, result.Exit);
        Assert.Equal(["list-keychains", "-d", "user", "-s", Keychain, Login], fake.Calls()[1]);
    }

    [Fact]
    public void Remove_KeepsEveryOtherKeychain()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using Fake fake = new();
        fake.ListOutput = "    \"" + Keychain + "\"\n    \"" + Login + "\"\n";

        Result result = fake.Run("remove", Keychain);

        Assert.Equal(0, result.Exit);
        Assert.Equal("", result.Stdout + result.Stderr);
        Assert.Equal(
            [["list-keychains", "-d", "user"], ["list-keychains", "-d", "user", "-s", Login]],
            fake.Calls());
    }

    [Fact]
    public void Remove_LeavesAListWithoutTheKeychainUnchanged()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using Fake fake = new();
        fake.ListOutput = "    \"" + Login + "\"\n    \"" + Spaced + "\"\n";

        Result result = fake.Run("remove", Keychain);

        Assert.Equal(0, result.Exit);
        Assert.Equal(["list-keychains", "-d", "user", "-s", Login, Spaced], fake.Calls()[1]);
    }

    [Theory]
    [InlineData(Sha1, Sha1)]
    [InlineData("ABCDEF0123456789ABCDEF0123456789ABCDEF01", Sha1)]
    [InlineData(Sha1, "ABCDEF0123456789ABCDEF0123456789ABCDEF01")]
    public void Check_FindsThePinnedIdentityIgnoringCaseEvenWhenUntrusted(string listed, string pinned)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using Fake fake = new();
        fake.IdentityOutput = "Policy: Code Signing\n  Matching identities\n"
            + "  1) 1111111111111111111111111111111111111111 \"Other\"\n"
            + "  2) " + listed + " \"ControlRoom Release\" (CSSMERR_TP_NOT_TRUSTED)\n"
            + "     2 identities found\n\n  Valid identities only\n     0 valid identities found\n";

        Result result = fake.Run("check", Keychain, pinned);

        Assert.Equal(0, result.Exit);
        Assert.Equal("", result.Stdout + result.Stderr);
        Assert.Equal([["find-identity", "-p", "codesigning", Keychain]], fake.Calls());
    }

    [Theory]
    [InlineData("     0 identities found\n")]
    [InlineData("  1) 1111111111111111111111111111111111111111 \"Other\" (CSSMERR_TP_NOT_TRUSTED)\n     1 identities found\n")]
    [InlineData("  1) " + Sha1 + "00 \"Longer\"\n  2) 00" + Sha1 + " \"Longer\"\n     2 identities found\n")]
    public void Check_FailsWithoutThePinnedIdentityAndPrintsNoneOfTheOutput(string identities)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using Fake fake = new();
        fake.IdentityOutput = "Policy: Code Signing\n  Matching identities\n" + identities;

        Result result = fake.Run("check", Keychain, Sha1);

        Assert.Equal(1, result.Exit);
        Assert.DoesNotContain("identities", result.Stdout + result.Stderr, StringComparison.Ordinal);
        Assert.DoesNotContain("Policy", result.Stdout + result.Stderr, StringComparison.Ordinal);
        Assert.DoesNotContain("\"", result.Stdout + result.Stderr, StringComparison.Ordinal);
        Assert.DoesNotContain("1111", result.Stdout + result.Stderr, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("add")]
    [InlineData("remove")]
    public void ListFailure_ExitsWithOneLineAndSetsNothing(string command)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using Fake fake = new();
        fake.ListOutput = "    \"" + Login + "\"\n";
        fake.ExitCode = 1;

        Result result = fake.Run(command, Keychain);

        Assert.NotEqual(0, result.Exit);
        Assert.Equal("", result.Stdout);
        Assert.Single(result.Stderr.TrimEnd('\n').Split('\n'));
        Assert.DoesNotContain(Login, result.Stderr, StringComparison.Ordinal);
        Assert.Equal([["list-keychains", "-d", "user"]], fake.Calls());
    }

    [Fact]
    public void FindIdentityFailure_ExitsWithOneLine()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using Fake fake = new();
        fake.IdentityOutput = "  1) " + Sha1 + " \"ControlRoom Release\"\n";
        fake.ExitCode = 1;

        Result result = fake.Run("check", Keychain, Sha1);

        Assert.NotEqual(0, result.Exit);
        Assert.Equal("", result.Stdout);
        Assert.Single(result.Stderr.TrimEnd('\n').Split('\n'));
        Assert.DoesNotContain("ControlRoom Release", result.Stderr, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("add")]
    [InlineData("remove")]
    [InlineData("check " + Keychain)]
    [InlineData("check")]
    [InlineData("list " + Keychain)]
    [InlineData("add " + Keychain + " extra")]
    public void MissingSubcommandOrArgument_ExitsTwo(string line)
    {
        string[] arguments = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using Fake fake = new();

        Result result = fake.Run(arguments);

        Assert.Equal(2, result.Exit);
        Assert.Single(result.Stderr.TrimEnd('\n').Split('\n'));
        Assert.Empty(fake.Calls());
    }

    private sealed record Result(int Exit, string Stdout, string Stderr);

    /// <summary>A temp folder with the fake <c>security</c>, its canned outputs, exit code and argument log.</summary>
    private sealed class Fake : IDisposable
    {
        private readonly string folder;

        public Fake()
        {
            folder = Path.Combine(Path.GetTempPath(), "controlroom-keychain-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(folder, "bin"));
            string security = Path.Combine(folder, "bin", "security");
            File.WriteAllText(security,
                "#!/bin/sh\n"
                + "dir='" + folder + "'\n"
                + "for arg in \"$@\"; do printf '%s\\n' \"$arg\" >> \"$dir/log\"; done\n"
                + "printf '%s\\n' '--end--' >> \"$dir/log\"\n"
                + "case \"$1 $4\" in\n"
                + "  'list-keychains -s') ;;\n"
                + "  list-keychains*) cat \"$dir/list\" ;;\n"
                + "  find-identity*) cat \"$dir/identities\" ;;\n"
                + "esac\n"
                + "exit \"$(cat \"$dir/exit\")\"\n");
            File.SetUnixFileMode(security, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            ListOutput = "";
            IdentityOutput = "";
            ExitCode = 0;
        }

        public string ListOutput
        {
            set => File.WriteAllText(Path.Combine(folder, "list"), value);
        }

        public string IdentityOutput
        {
            set => File.WriteAllText(Path.Combine(folder, "identities"), value);
        }

        public int ExitCode
        {
            set => File.WriteAllText(Path.Combine(folder, "exit"), value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        public Result Run(params string[] arguments)
        {
            ProcessStartInfo start = new("/bin/sh")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            start.ArgumentList.Add(Path.Combine(RepoFolder(), ".github", "scripts", "signing-keychain.sh"));
            foreach (string argument in arguments)
            {
                start.ArgumentList.Add(argument);
            }

            start.Environment["PATH"] = Path.Combine(folder, "bin") + ":/usr/bin:/bin";
            using Process process = Process.Start(start)!;
            Task<string> stdout = process.StandardOutput.ReadToEndAsync();
            Task<string> stderr = process.StandardError.ReadToEndAsync();
            Assert.True(process.WaitForExit(TimeSpan.FromSeconds(30)), "signing-keychain.sh did not exit within 30 s");
            process.WaitForExit();
            return new Result(process.ExitCode, stdout.GetAwaiter().GetResult(), stderr.GetAwaiter().GetResult());
        }

        /// <summary>Each <c>security</c> call's arguments, in call order.</summary>
        public List<List<string>> Calls()
        {
            string log = Path.Combine(folder, "log");
            List<List<string>> calls = [];
            if (!File.Exists(log))
            {
                return calls;
            }

            List<string> current = [];
            foreach (string line in File.ReadAllLines(log))
            {
                if (line == "--end--")
                {
                    calls.Add(current);
                    current = [];
                }
                else
                {
                    current.Add(line);
                }
            }

            return calls;
        }

        public void Dispose()
        {
            Directory.Delete(folder, recursive: true);
        }

        private static string RepoFolder()
        {
            DirectoryInfo? current = new(AppContext.BaseDirectory);
            while (current is not null && !File.Exists(Path.Combine(current.FullName, "ControlRoom.slnx")))
            {
                current = current.Parent;
            }

            Assert.NotNull(current);
            return current.FullName;
        }
    }
}
