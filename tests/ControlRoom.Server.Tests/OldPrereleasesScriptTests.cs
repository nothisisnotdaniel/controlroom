using System.Runtime.Versioning;

namespace ControlRoom.Server.Tests;

/// <summary><c>.github/scripts/old-prereleases.sh</c>, run with <c>/bin/sh</c>, a release version and a tag list
/// on stdin.</summary>
[UnsupportedOSPlatform("windows")]
public sealed class OldPrereleasesScriptTests
{
    private const string Tags =
        "v1.2.4-develop.9\nv1.2.3-develop.5\nv1.2.3\nv0.9.0-develop.1\nv1.3.0-develop.2\nv1.2.3-rc.1\n"
        + "v1.2.3-develop.05\nv1.2.3-develop.0\nv1.2.3-develop.1+x\nv01.2.3-develop.1\n v1.2.2-develop.1\n"
        + "v1.2.2-develop.1 \nv1.2.2-develop.1\r\nv2.0.0-develop.1\nv1.10.0-develop.1\nv1.2.0-develop.3";

    [Theory]
    [InlineData("1.2.3", "v1.2.3-develop.5\nv0.9.0-develop.1\nv1.2.0-develop.3\n")]
    [InlineData("1.2.4", "v1.2.4-develop.9\nv1.2.3-develop.5\nv0.9.0-develop.1\nv1.2.0-develop.3\n")]
    [InlineData("1.9.0", "v1.2.4-develop.9\nv1.2.3-develop.5\nv0.9.0-develop.1\nv1.3.0-develop.2\nv1.2.0-develop.3\n")]
    [InlineData("0.8.9", "")]
    [InlineData("2.0.0",
        "v1.2.4-develop.9\nv1.2.3-develop.5\nv0.9.0-develop.1\nv1.3.0-develop.2\nv2.0.0-develop.1\nv1.10.0-develop.1\n"
        + "v1.2.0-develop.3\n")]
    public void Release_PrintsOnlyDevelopTagsAtOrBelowItInInputOrder(string release, string expected)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        Assert.Equal((0, expected), CiScript.Run("old-prereleases.sh", release, Tags));
    }

    [Fact]
    public void EmptyList_PrintsNothing()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        Assert.Equal((0, ""), CiScript.Run("old-prereleases.sh", "1.2.3", ""));
    }

    [Theory]
    [InlineData("")]
    [InlineData("v1.2.3")]
    [InlineData("1.2")]
    [InlineData("1.2.3-rc.1")]
    [InlineData("1.2.3-develop.4")]
    [InlineData("1.2.3+b")]
    [InlineData("01.2.3")]
    [InlineData("1.2.3\n")]
    [InlineData("1.2.3\n1.2.4")]
    [InlineData("$(id)")]
    public void BadRelease_ExitsOneAndPrintsNothing(string release)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        Assert.Equal((1, ""), CiScript.Run("old-prereleases.sh", release, Tags));
    }
}
