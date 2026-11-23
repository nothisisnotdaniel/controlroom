using System.Diagnostics;
using System.Globalization;

namespace ControlRoom.Server.Tests;

/// <summary>Runs the two PowerShell scripts against a private release repository, clock and scheduler fixture.</summary>
internal static class WindowsReleaseFixture
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(3);

    public static void Run(string fixture, string scripts, string scenario, FakeTime clock)
    {
        string shell = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
        ProcessStartInfo start = new(shell)
        {
            WorkingDirectory = fixture,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };
        string runner = Path.Combine(RepoCopy.RepoFolder(), "tests", "ControlRoom.Server.Tests", "WindowsReleaseFixture.ps1");
        foreach (string argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "RemoteSigned",
                     "-File", runner, "-Fixture", fixture, "-Scripts", scripts, "-Scenario", scenario, "-Epoch",
                     clock.GetUtcNow().ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture) })
        {
            start.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(start)!;
        Task<string> error = process.StandardError.ReadToEndAsync();
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        if (!process.WaitForExit(Timeout))
        {
            process.Kill(entireProcessTree: true);
            Assert.Fail(scenario + " exceeded " + Timeout);
        }

        Assert.True(process.ExitCode == 0, scenario + Environment.NewLine + output.Result + error.Result);
    }
}
