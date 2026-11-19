namespace ControlRoom.Server.Tests;

/// <summary>The <c>version</c> command, with <see cref="StringWriter"/>s for stdout and stderr.</summary>
public sealed class VersionCommandTests
{
    [Fact]
    public void Version_PrintsTheVersionAndExitsZeroWithoutTouchingTheTokenFile()
    {
        using TempTokenFolder tokens = new();
        StringWriter output = new();
        StringWriter error = new();

        Assert.True(VersionCommand.IsCommand(["version"]));
        int code = VersionCommand.Run(["version"], output, error);

        Assert.Equal(0, code);
        Assert.Equal("ControlRoom dev\n", output.ToString());
        Assert.Equal("", error.ToString());
        Assert.Empty(Directory.EnumerateFileSystemEntries(tokens.Path));
    }

    [Fact]
    public void Version_IsOnlyTheExactFirstArgument()
    {
        Assert.False(VersionCommand.IsCommand([]));
        Assert.False(VersionCommand.IsCommand(["Version"]));
        Assert.False(VersionCommand.IsCommand(["--version"]));
        Assert.False(VersionCommand.IsCommand(["token", "version"]));
    }

    [Fact]
    public void Version_WithExtraArguments_PrintsUsageExitsOneAndCreatesNoTokenFile()
    {
        using TempTokenFolder tokens = new();
        StringWriter output = new();
        StringWriter error = new();

        int code = VersionCommand.Run(["version", "--TokenFile", tokens.TokenFile], output, error);

        Assert.Equal(1, code);
        Assert.Equal("", output.ToString());
        Assert.Equal(VersionCommand.UsageText + Environment.NewLine, error.ToString());
        Assert.Empty(Directory.EnumerateFileSystemEntries(tokens.Path));
    }
}
