using System.Text.RegularExpressions;

namespace ControlRoom.Server.Tests;

/// <summary>ControlRoom's scripts are RoomKit's scripts with ControlRoom's <c>roomkit:app</c> block.</summary>
public sealed partial class RoomKitScriptTests
{
    private const string Begin = "# roomkit:app-begin\n";

    private const string End = "# roomkit:app-end\n";

    [Fact]
    public void WindowsUpdaterUsesRoomKitsSemVerIdentifierRule()
    {
        string updater = Read(Path.Combine(RepoFolder(), "src", "RoomKit", "scripts", "updater.ps1"));
        Assert.Contains("$identifier = '" + SemVer.PrereleaseIdentifierPattern + "'", updater.Split('\n'));
    }

    [Fact]
    public void WindowsInstallerSharesTheUpdaterAndOnlyTheAppAssignmentsDiffer()
    {
        string scripts = Path.Combine(RepoFolder(), "scripts");
        string kit = Path.Combine(RepoFolder(), "src", "RoomKit", "scripts");
        Assert.Equal(Read(Path.Combine(kit, "launch-agent.ps1")), Read(Path.Combine(scripts, "launch-agent.ps1")));
        (string before, string block, string after) = Split(Read(Path.Combine(kit, "updater.ps1")));
        (string appBefore, string appBlock, string appAfter) = Split(Read(Path.Combine(scripts, "updater.ps1")));
        Assert.Equal(before, appBefore);
        Assert.Equal(after, appAfter);
        Assert.Equal(Names(block), Names(appBlock));
        Assert.NotEqual(block, appBlock);
        Assert.DoesNotContain("controlroom", block + after, StringComparison.OrdinalIgnoreCase);
        Assert.All(block.TrimEnd('\n').Split('\n'), line => Assert.Matches(@"^\$[A-Za-z]+ = ('[A-Za-z0-9./-]+'|[0-9]+)$", line));
    }

    [Theory]
    [InlineData("launch-agent.sh")]
    [InlineData("updater.sh")]
    public void OutsideTheBlock_TheScriptsAreByteIdentical(string name)
    {
        string app = Read(Path.Combine(RepoFolder(), "scripts", name));
        string kit = Read(Path.Combine(RepoFolder(), "src", "RoomKit", "scripts", name));
        (string appBefore, string appBlock, string appAfter) = Split(app);
        (string kitBefore, string kitBlock, string kitAfter) = Split(kit);

        Assert.Equal(kitBefore, appBefore);
        Assert.Equal(kitAfter, appAfter);
        Assert.NotEqual(kitBlock, appBlock);
        Assert.Equal(Names(kitBlock), Names(appBlock));
    }

    [Theory]
    [InlineData("launch-agent.sh")]
    [InlineData("updater.sh")]
    public void KitBlock_HoldsOnlyAssignmentsOfNeutralValues(string name)
    {
        string kit = Read(Path.Combine(RepoFolder(), "src", "RoomKit", "scripts", name));
        (_, string block, _) = Split(kit);
        string[] lines = block.TrimEnd('\n').Split('\n');

        Assert.NotEmpty(lines);
        Assert.All(lines, line => Assert.Matches(Assignment(), line));
        Assert.DoesNotContain("ControlRoom", kit, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("nothisisnotdaniel", kit, StringComparison.Ordinal);
        Assert.DoesNotContain("8100", kit, StringComparison.Ordinal);
        Assert.Equal(
            ["APP_NAME", "LABEL", "UPDATER_LABEL", "SUPPORT_FOLDER", "LOG_FOLDER", "REPO_SLUG", "RELEASES", "APP_PORT",
            "TARBALL_PREFIX", "SERVER_NAME", "SOLUTION_FILE"],
            Names(block));
    }

    [Theory]
    [InlineData("launch-agent.sh")]
    [InlineData("updater.sh")]
    public void KitScripts_KeepTheExecutableMode(string name)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        UnixFileMode app = File.GetUnixFileMode(Path.Combine(RepoFolder(), "scripts", name));
        UnixFileMode kit = File.GetUnixFileMode(Path.Combine(RepoFolder(), "src", "RoomKit", "scripts", name));
        Assert.Equal(app & UnixFileMode.UserExecute, kit & UnixFileMode.UserExecute);
    }

    private static (string Before, string Block, string After) Split(string script)
    {
        int begin = script.IndexOf(Begin, StringComparison.Ordinal);
        int end = script.IndexOf(End, StringComparison.Ordinal);
        Assert.True(begin >= 0 && end > begin, "missing roomkit:app block");
        Assert.Equal(begin, script.LastIndexOf(Begin, StringComparison.Ordinal));
        Assert.Equal(end, script.LastIndexOf(End, StringComparison.Ordinal));
        return (script[..(begin + Begin.Length)], script[(begin + Begin.Length)..end], script[end..]);
    }

    private static string[] Names(string block)
    {
        return [.. block.TrimEnd('\n').Split('\n').Select(line => line[..line.IndexOf('=', StringComparison.Ordinal)])];
    }

    private static string Read(string path)
    {
        return File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    private static string RepoFolder()
    {
        return Path.GetDirectoryName(Path.GetDirectoryName(AccessTokenTests.SourceFolder())!)!;
    }

    // NAME="value without $, quotes, backquotes or backslashes" or NAME=digits.
    [GeneratedRegex("\\A[A-Z_]+=(\"[^\"$`\\\\]*\"|[0-9]+)\\z")]
    private static partial Regex Assignment();
}
