using System.Runtime.Versioning;

namespace ControlRoom.Server.Tests;

/// <summary><c>.github/scripts/develop-version.sh</c>, run with <c>/bin/sh</c>, a run number and a tag list on
/// stdin.</summary>
[UnsupportedOSPlatform("windows")]
public sealed class DevelopVersionScriptTests
{
    [Theory]
    [InlineData("", "12", "0.0.1-develop.12\n")]
    [InlineData("v0.1.0\n", "12", "0.1.1-develop.12\n")]
    [InlineData("v0.1.0-beta\n", "3", "0.1.1-develop.3\n")]
    [InlineData("v0.1.0\nv1.2.3\nv1.10.0\nv1.9.9\n", "7", "1.10.1-develop.7\n")]
    [InlineData("v1.2.3\nv1.2.4-develop.5\nv9.9.9-develop.1\n", "6", "1.2.4-develop.6\n")]
    [InlineData("v1.2.3\nv2.0.0+build.1\nv02.0.0\nx3.0.0\nv3.0\nv4.0.0\r\n", "1", "1.2.4-develop.1\n")]
    [InlineData("v1.2.3\nv1234567890.0.0\nv1.1234567890.0\nv1.2.1234567890\n", "1", "1.2.4-develop.1\n")]
    [InlineData("v999999999.999999999.999999999\n", "1", "999999999.999999999.1000000000-develop.1\n")]
    [InlineData("v1.0.0\nv1.2.3", "100", "1.2.4-develop.100\n")]
    public void Tags_PrintTheNextPatchAsADevelopVersion(string tags, string run, string expected)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        Assert.Equal((0, expected), CiScript.Run("develop-version.sh", run, tags));
    }

    [Theory]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("012")]
    [InlineData("-1")]
    [InlineData("1a")]
    [InlineData("1.2")]
    [InlineData(" 1")]
    [InlineData("$(id)")]
    public void BadRunNumber_ExitsOneAndPrintsNothing(string run)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        Assert.Equal((1, ""), CiScript.Run("develop-version.sh", run, "v1.2.3\n"));
    }
}
