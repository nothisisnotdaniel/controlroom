using System.Diagnostics;
using System.Runtime.Versioning;

namespace ControlRoom.Server.Tests;

/// <summary><c>.github/scripts/release-version.sh</c>, run with <c>/bin/sh</c> and a fixed tag list on stdin.</summary>
[UnsupportedOSPlatform("windows")]
public sealed class ReleaseVersionScriptTests
{
    private const string Tags = "v0.1.0\nv1.2.3\nv1.3.0-rc.1\n";

    private static readonly TimeSpan ProcessTimeout = TimeSpan.FromSeconds(60);

    [Theory]
    [InlineData("release v1.2.4", "1.2.4")]
    [InlineData("release v1.3.0", "1.3.0")]
    [InlineData("release v1.3.0-rc.2", "1.3.0-rc.2")]
    [InlineData("release v0.0.0", "0.0.0")]
    [InlineData("release v10.20.30-alpha-1.beta", "10.20.30-alpha-1.beta")]
    [InlineData("release v1.2.3-0", "1.2.3-0")]
    [InlineData("release v1.2.3-rc.1", "1.2.3-rc.1")]
    [InlineData("release v1.2.3-0.3.7", "1.2.3-0.3.7")]
    [InlineData("release v1.2.3-x.7.z.92", "1.2.3-x.7.z.92")]
    [InlineData("release v1.2.3-alpha-1.beta", "1.2.3-alpha-1.beta")]
    [InlineData("release v1.2.3-01a", "1.2.3-01a")]
    [InlineData("release v1.2.3-0-1", "1.2.3-0-1")]
    public void GoodTitle_PrintsTheVersion(string title, string version)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        (int code, string output) = Run(title, Tags);

        Assert.Equal(0, code);
        Assert.Equal(version + "\n", output);
    }

    [Fact]
    public void NewTagsAndPrerelease_PrintTheVersion()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        Assert.Equal((0, "1.2.3\n"), Run("release v1.2.3", ""));
        Assert.Equal((0, "1.2.3-rc.1\n"), Run("release v1.2.3-rc.1", ""));
        Assert.Equal((0, "1.2.3-rc.1\n"), Run("release v1.2.3-rc.1", "v1.2.3-rc.0"));
    }

    [Theory]
    [InlineData("v1.2.4")]
    [InlineData("Release v1.2.4")]
    [InlineData("release 1.2.4")]
    [InlineData("release v1.2.4 now")]
    [InlineData(" release v1.2.4")]
    [InlineData("release v1.2.4\n")]
    [InlineData("release v1.2.4\r")]
    [InlineData("release v1.2.4\nrelease v1.2.5")]
    [InlineData("release v1.2.4+build.1")]
    [InlineData("release v1.2.4-develop.3")]
    [InlineData("release v01.2.4")]
    [InlineData("release v1.02.4")]
    [InlineData("release v1.2.04")]
    [InlineData("release v1.2")]
    [InlineData("release v1.2.4-")]
    [InlineData("release v1.2.4-rc..1")]
    [InlineData("release v1.2.3-01")]
    [InlineData("release v1.2.3-rc.01")]
    [InlineData("release v1.2.3-0.00")]
    [InlineData("release v1.2.3-1.2.03")]
    [InlineData("release v1.2.3")]
    [InlineData("release v1.3.0-rc.1")]
    [InlineData("release v$(id)")]
    [InlineData("")]
    public void BadTitleOrExistingTag_ExitsOneAndPrintsNothing(string title)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        (int code, string output) = Run(title, Tags);

        Assert.Equal(1, code);
        Assert.Equal("", output);
    }

    [Fact]
    public void ExistingTag_OnTheLastLineWithoutALineBreak_IsRefused()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        Assert.Equal((1, ""), Run("release v1.2.3", "v1.0.0\nv1.2.3"));
    }

    private static (int Code, string Output) Run(string title, string tags)
    {
        ProcessStartInfo start = new("/bin/sh")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add(Path.Combine(RepoFolder(), ".github", "scripts", "release-version.sh"));
        start.ArgumentList.Add(title);
        start.Environment.Clear();
        start.Environment["PATH"] = "/usr/bin:/bin";

        using Process process = Process.Start(start)!;
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        try
        {
            process.StandardInput.Write(tags);
            process.StandardInput.Close();
        }
        catch (IOException)
        {
            // The script may refuse the title and exit before it reads stdin (broken pipe).
        }
        if (!process.WaitForExit(ProcessTimeout))
        {
            process.Kill(entireProcessTree: true);
            Assert.Fail("release-version.sh did not exit within " + ProcessTimeout);
        }

        process.WaitForExit();
        _ = error.Result;
        return (process.ExitCode, output.Result);
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
