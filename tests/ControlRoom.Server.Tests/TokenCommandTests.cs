using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Builder;

namespace ControlRoom.Server.Tests;

/// <summary>The <c>token</c> and <c>token --new</c> commands, with <see cref="StringWriter"/>s for stdout and
/// stderr.</summary>
public sealed class TokenCommandTests
{
    [Fact]
    public void Token_CreatesTheFileWhenMissingAndPrintsExactlyTheToken()
    {
        using TempTokenFolder tokens = new();
        StringWriter output = new();
        StringWriter error = new();

        int code = TokenCommand.Run(["token", "--TokenFile", tokens.TokenFile], output, error);

        Assert.Equal(0, code);
        string token = AccessToken.Read(tokens.TokenFile).Token!;
        Assert.Equal(token + "\n", output.ToString());
        Assert.Equal("", error.ToString());
    }

    [Fact]
    public void Token_PrintsTheStoredTokenAgain()
    {
        using TempTokenFolder tokens = new();
        string stored = AccessToken.Replace(tokens.TokenFile, buffer => buffer.Fill(1))!;
        StringWriter output = new();

        int code = TokenCommand.Run(["token", "--TokenFile", tokens.TokenFile], output, TextWriter.Null);

        Assert.Equal(0, code);
        Assert.Equal(stored + "\n", output.ToString());
    }

    [Fact]
    public void TokenNew_WritesADifferentPrivateTokenAndPrintsIt()
    {
        using TempTokenFolder tokens = new();
        string old = AccessToken.Replace(tokens.TokenFile, buffer => buffer.Fill(1))!;
        StringWriter output = new();
        StringWriter error = new();

        int code = TokenCommand.Run(["token", "--new", "--TokenFile", tokens.TokenFile], output, error);

        Assert.Equal(0, code);
        string current = AccessToken.Read(tokens.TokenFile).Token!;
        Assert.NotEqual(old, current);
        Assert.Equal(current + "\n", output.ToString());
        Assert.Equal("", error.ToString());
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(AccessToken.FileMode, File.GetUnixFileMode(tokens.TokenFile));
        }
    }

    [Fact]
    public void Token_RefusesUnknownArguments()
    {
        using TempTokenFolder tokens = new();
        StringWriter output = new();
        StringWriter error = new();

        int code = TokenCommand.Run(["token", "--TokenFile", tokens.TokenFile, "--show"], output, error);

        Assert.Equal(1, code);
        Assert.Equal("", output.ToString());
        Assert.Equal(TokenCommand.UsageText + Environment.NewLine, error.ToString());
        Assert.False(File.Exists(tokens.TokenFile));
    }

    public static TheoryData<string> BadFiles()
    {
        TheoryData<string> data = ["symlink", "junk"];
        if (!OperatingSystem.IsWindows())
        {
            data.Add("fifo");
            data.Add("mode-0644");
            data.Add("locked");
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(BadFiles))]
    public async Task BadFile_StopsServerAndTokenAndTokenNewReplacesIt(string kind)
    {
        using TempTokenFolder tokens = new();
        using TempFactoryRoot root = new();
        string target = Path.Combine(tokens.Path, "target");
        File.WriteAllText(target, new string('A', 43) + "\n");
        MakeBadFile(root, tokens.TokenFile, target, kind);
        string expected = AccessToken.InvalidText + Environment.NewLine;
        Assert.Contains("token --new", AccessToken.InvalidText, StringComparison.Ordinal);

        StringWriter serverError = new();
        WebApplication? app = ServerApp.Create(
            ["--FactoryRoot", root.Path, "--TokenFile", tokens.TokenFile], serverError);
        Assert.Null(app);
        Assert.Equal(expected, serverError.ToString());

        StringWriter output = new();
        StringWriter error = new();
        Assert.Equal(1, TokenCommand.Run(["token", "--TokenFile", tokens.TokenFile], output, error));
        Assert.Equal("", output.ToString());
        Assert.Equal(expected, error.ToString());

        StringWriter newOutput = new();
        Assert.Equal(0, TokenCommand.Run(["token", "--new", "--TokenFile", tokens.TokenFile], newOutput,
            TextWriter.Null));
        TokenFileRead read = AccessToken.Read(tokens.TokenFile);
        Assert.Equal(TokenFileState.Valid, read.State);
        Assert.Equal(read.Token + "\n", newOutput.ToString());
        Assert.Null(new FileInfo(tokens.TokenFile).LinkTarget);
        Assert.Equal(new string('A', 43) + "\n", File.ReadAllText(target));
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(AccessToken.FileMode, File.GetUnixFileMode(tokens.TokenFile));
        }

        StringWriter startError = new();
        WebApplication? started = ServerApp.Create(
            ["--FactoryRoot", root.Path, "--TokenFile", tokens.TokenFile], startError);
        Assert.NotNull(started);
        await started.DisposeAsync();
        Assert.Equal("", startError.ToString());
    }

    [Fact]
    public async Task TokenNew_WhileRunningRefusesTheOldTokenAndAcceptsTheNewOne()
    {
        using TempFactoryRoot root = new();
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateClient();
        string old = factory.ReadToken();
        Assert.Equal(HttpStatusCode.OK, (await Send(client, "/api/board", old)).StatusCode);

        StringWriter output = new();
        Assert.Equal(0, TokenCommand.Run(["token", "--new", "--TokenFile", factory.TokenFile], output,
            TextWriter.Null));
        string current = output.ToString().TrimEnd('\n');

        Assert.NotEqual(old, current);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Send(client, "/api/board", old)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Send(client, "/api/board", current)).StatusCode);
    }

    [Fact]
    public async Task StartupOutput_NeverHoldsTheTokenAndTheTokenStaysOutOfTheRoot()
    {
        using TempFactoryRoot root = TempFactoryRoot.WithSamples();
        using TempTokenFolder tokens = new();
        IReadOnlyList<string> before = root.Snapshot();
        StringWriter error = new();

        WebApplication? app = ServerApp.Create(["--FactoryRoot", root.Path, "--TokenFile", tokens.TokenFile], error);

        Assert.NotNull(app);
        string token = AccessToken.Read(tokens.TokenFile).Token!;
        Assert.Equal(AccessToken.CreatedText + Environment.NewLine, error.ToString());
        Assert.DoesNotContain(token, error.ToString(), StringComparison.Ordinal);
        Assert.Equal(before, root.Snapshot());
        Assert.False(tokens.TokenFile.StartsWith(root.Path, StringComparison.Ordinal));
        await app.DisposeAsync();
    }

    [Fact]
    public void DefaultTokenFile_IsOutsideTheFactoryRootAndTheRepo()
    {
        string factoryRoot = FactoryRootLocator.FindDefault(AppContext.BaseDirectory)!;
        Assert.NotNull(factoryRoot);

        string path = AccessToken.ResolvePath(null);

        Assert.False(path.StartsWith(factoryRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal));
        Assert.EndsWith(Path.Combine("ControlRoom", "access-token"), path, StringComparison.Ordinal);
    }

    private static async Task<HttpResponseMessage> Send(HttpClient client, string path, string token)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.SendAsync(request);
    }

    private static void MakeBadFile(TempFactoryRoot root, string path, string target, string kind)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        switch (kind)
        {
            case "symlink":
                if (!OperatingSystem.IsWindows())
                {
                    File.SetUnixFileMode(target, AccessToken.FileMode);
                }

                File.CreateSymbolicLink(path, target);
                break;
            case "junk":
                File.WriteAllText(path, "not a token\n");
                SetPrivate(path);
                break;
            case "fifo":
                TempFactoryRoot.MakeFifo(path);
                SetPrivate(path);
                break;
            case "mode-0644":
                File.WriteAllText(path, new string('B', 43) + "\n");
                if (!OperatingSystem.IsWindows())
                {
                    File.SetUnixFileMode(path,
                        AccessToken.FileMode | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
                }

                break;
            default:
                File.WriteAllText(path, new string('B', 43) + "\n");
                SetPrivate(path);
                root.Lock(path);
                break;
        }
    }

    private static void SetPrivate(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, AccessToken.FileMode);
        }
    }
}
