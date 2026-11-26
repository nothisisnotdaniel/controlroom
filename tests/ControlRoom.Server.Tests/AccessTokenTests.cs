using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;

namespace ControlRoom.Server.Tests;

/// <summary>Checks <see cref="AccessToken"/> directly: format, modes, atomic replace, refusal cases and the
/// constant-time compare. Random bytes come from a fake source.</summary>
public sealed partial class AccessTokenTests
{
    private static RandomFill Fixed(byte value)
    {
        return buffer => buffer.Fill(value);
    }

    [Fact]
    public void Generate_IsBase64UrlWithoutPaddingOf32Bytes()
    {
        Assert.Equal(new string('A', 43), AccessToken.Generate(Fixed(0)));
        Assert.Equal(new string('_', 42) + "8", AccessToken.Generate(Fixed(0xFF)));
        Assert.Equal("-_v7-_v7-_v7-_v7-_v7-_v7-_v7-_v7-_v7-_v7-_s", AccessToken.Generate(Fixed(0xFB)));
        Assert.NotEqual(AccessToken.Generate(Fixed(1)), AccessToken.Generate(Fixed(2)));
        Assert.Matches(TokenPattern(), AccessToken.Generate(AccessToken.SystemRandom));
    }

    [Fact]
    public void Replace_CreatesA0600FileInA0700FolderAndReadKeepsIt()
    {
        using TempTokenFolder tokens = new();

        Assert.Equal(TokenFileState.Missing, AccessToken.Read(tokens.TokenFile).State);
        string? token = AccessToken.Replace(tokens.TokenFile, Fixed(7));

        Assert.NotNull(token);
        Assert.Matches(TokenPattern(), token);
        Assert.Equal(token + "\n", File.ReadAllText(tokens.TokenFile));
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(AccessToken.FileMode, File.GetUnixFileMode(tokens.TokenFile));
            Assert.Equal(AccessToken.FolderMode, File.GetUnixFileMode(Path.GetDirectoryName(tokens.TokenFile)!));
        }

        TokenFileRead read = AccessToken.Read(tokens.TokenFile);
        Assert.Equal(TokenFileState.Valid, read.State);
        Assert.Equal(token, read.Token);
        Assert.Single(Directory.GetFileSystemEntries(Path.GetDirectoryName(tokens.TokenFile)!));
    }

    [Fact]
    public async Task Server_CreatesTheTokenOnFirstStartAndKeepsItOnTheSecond()
    {
        using TempTokenFolder tokens = new();
        using TempFactoryRoot root = new();
        string[] args = ["--FactoryRoot", root.Path, "--TokenFile", tokens.TokenFile];
        StringWriter first = new();

        WebApplication? app = ServerApp.Create(args, first);

        Assert.NotNull(app);
        await app.DisposeAsync();
        string token = File.ReadAllText(tokens.TokenFile).TrimEnd('\n');
        Assert.Matches(TokenPattern(), token);
        Assert.Equal(AccessToken.CreatedText(tokens.TokenFile) + Environment.NewLine, first.ToString());
        Assert.DoesNotContain(token, first.ToString(), StringComparison.Ordinal);
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(AccessToken.FileMode, File.GetUnixFileMode(tokens.TokenFile));
            Assert.Equal(AccessToken.FolderMode, File.GetUnixFileMode(Path.GetDirectoryName(tokens.TokenFile)!));
        }

        StringWriter second = new();
        WebApplication? again = ServerApp.Create(args, second);
        Assert.NotNull(again);
        await again.DisposeAsync();
        Assert.Equal(token + "\n", File.ReadAllText(tokens.TokenFile));
        Assert.Equal("", second.ToString());
    }

    [Fact]
    public void Replace_WritesADifferentTokenAndKeepsTheMode()
    {
        using TempTokenFolder tokens = new();
        string? first = AccessToken.Replace(tokens.TokenFile, Fixed(1));
        string? second = AccessToken.Replace(tokens.TokenFile, Fixed(2));

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.NotEqual(first, second);
        Assert.Equal(second, AccessToken.Read(tokens.TokenFile).Token);
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(AccessToken.FileMode, File.GetUnixFileMode(tokens.TokenFile));
        }
    }

    [Fact]
    public void Read_RefusesASymlinkAndReplaceRemovesItWithoutTouchingItsTarget()
    {
        using TempTokenFolder tokens = new();
        string target = Path.Combine(tokens.Path, "target");
        string token = AccessToken.Generate(Fixed(3));
        TempTokenFolder.WritePrivateText(target, token + "\n");
        TempTokenFolder.CreatePrivateDirectory(Path.GetDirectoryName(tokens.TokenFile)!);
        File.CreateSymbolicLink(tokens.TokenFile, target);

        Assert.Equal(TokenFileState.Invalid, AccessToken.Read(tokens.TokenFile).State);

        string? replaced = AccessToken.Replace(tokens.TokenFile, Fixed(4));

        Assert.NotNull(replaced);
        Assert.Null(new FileInfo(tokens.TokenFile).LinkTarget);
        Assert.Equal(TokenFileState.Valid, AccessToken.Read(tokens.TokenFile).State);
        Assert.Equal(token + "\n", File.ReadAllText(target));
    }

    [Fact]
    public void Read_RefusesADanglingSymlink()
    {
        using TempTokenFolder tokens = new();
        TempTokenFolder.CreatePrivateDirectory(Path.GetDirectoryName(tokens.TokenFile)!);
        File.CreateSymbolicLink(tokens.TokenFile, Path.Combine(tokens.Path, "nowhere"));

        Assert.Equal(TokenFileState.Invalid, AccessToken.Read(tokens.TokenFile).State);
    }

    [Fact]
    public void Read_RefusesAFifoAndAFolder()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using TempTokenFolder tokens = new();
        TempTokenFolder.CreatePrivateDirectory(Path.GetDirectoryName(tokens.TokenFile)!);
        TempFactoryRoot.MakeFifo(tokens.TokenFile);
        File.SetUnixFileMode(tokens.TokenFile, AccessToken.FileMode);

        Assert.Equal(TokenFileState.Invalid, AccessToken.Read(tokens.TokenFile).State);

        string folder = Path.Combine(tokens.Path, "folder");
        Directory.CreateDirectory(folder);
        Assert.Equal(TokenFileState.Invalid, AccessToken.Read(folder).State);
    }

    [Theory]
    [InlineData(UnixFileMode.GroupRead)]
    [InlineData(UnixFileMode.OtherRead)]
    [InlineData(UnixFileMode.GroupRead | UnixFileMode.OtherRead)]
    [InlineData(UnixFileMode.GroupWrite)]
    public void Read_RefusesAFileOthersCanReach(UnixFileMode extra)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using TempTokenFolder tokens = new();
        AccessToken.Replace(tokens.TokenFile, Fixed(5));
        File.SetUnixFileMode(tokens.TokenFile, AccessToken.FileMode | extra);

        Assert.Equal(TokenFileState.Invalid, AccessToken.Read(tokens.TokenFile).State);

        Assert.NotNull(AccessToken.Replace(tokens.TokenFile, Fixed(6)));
        Assert.Equal(AccessToken.FileMode, File.GetUnixFileMode(tokens.TokenFile));
        Assert.Equal(TokenFileState.Valid, AccessToken.Read(tokens.TokenFile).State);
    }

    [Fact]
    public void Read_RefusesAnUnreadableFile()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using TempTokenFolder tokens = new();
        using TempFactoryRoot root = new();
        AccessToken.Replace(tokens.TokenFile, Fixed(5));
        root.Lock(tokens.TokenFile);

        Assert.Equal(TokenFileState.Invalid, AccessToken.Read(tokens.TokenFile).State);
    }

    [Theory]
    [InlineData("")]
    [InlineData("\n")]
    [InlineData("short\n")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=\n")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA+\n")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA \n")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA\n\n")]
    [InlineData(" AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA\n")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA\r\n")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA\n")]
    public void Read_RefusesJunkContent(string content)
    {
        using TempTokenFolder tokens = new();
        TempTokenFolder.WritePrivateText(tokens.TokenFile, content);

        Assert.Equal(TokenFileState.Invalid, AccessToken.Read(tokens.TokenFile).State);
    }

    [Fact]
    public void Read_RefusesAnOversizedFile()
    {
        using TempTokenFolder tokens = new();
        TempTokenFolder.WritePrivateText(tokens.TokenFile, new string('A', 4096));

        Assert.Equal(TokenFileState.Invalid, AccessToken.Read(tokens.TokenFile).State);
    }

    [Theory]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA\n")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    public void Read_AcceptsTheTokenWithOrWithoutOneNewline(string content)
    {
        using TempTokenFolder tokens = new();
        TempTokenFolder.WritePrivateText(tokens.TokenFile, content);

        Assert.Equal(new string('A', 43), AccessToken.Read(tokens.TokenFile).Token);
    }

    [Fact]
    public void Matches_RefusesADifferentLengthOrOneChangedCharacter()
    {
        string token = AccessToken.Generate(Fixed(9));

        Assert.True(AccessToken.Matches(token, token));
        Assert.True(AccessToken.Matches(token, new string(token.ToCharArray())));
        Assert.False(AccessToken.Matches(token, null));
        Assert.False(AccessToken.Matches(token, ""));
        Assert.False(AccessToken.Matches(token, token[..^1]));
        Assert.False(AccessToken.Matches(token, token + "A"));
        for (int index = 0; index < token.Length; index++)
        {
            char changed = token[index] == 'A' ? 'B' : 'A';
            string other = token[..index] + changed + token[(index + 1)..];
            Assert.False(AccessToken.Matches(token, other));
        }
    }

    [Fact]
    public void ResolvePath_DefaultsToLocalApplicationDataAndHonoursTheSetting()
    {
        string data = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify);

        Assert.Equal(Path.Combine(data, "ControlRoom", "access-token"), AccessToken.ResolvePath(null));
        Assert.Equal(Path.Combine(data, "ControlRoom", "access-token"), AccessToken.ResolvePath(" "));
        Assert.Equal(Path.GetFullPath("/x/y"), AccessToken.ResolvePath("/x/y"));
    }

    internal static string SourceFolder()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "ControlRoom.slnx")))
        {
            current = current.Parent;
        }

        Assert.NotNull(current);
        return Path.Combine(current.FullName, "src", "ControlRoom.Server");
    }

    [GeneratedRegex("^[A-Za-z0-9_-]{43}$")]
    private static partial Regex TokenPattern();
}
