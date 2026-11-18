using System.Diagnostics;
using System.Runtime.Versioning;

namespace ControlRoom.Server.Tests;

/// <summary>Runs one <c>.github/scripts/*.sh</c> with <c>/bin/sh</c>, a clean environment, one argument and a
/// fixed stdin; returns the exit code and stdout.</summary>
[UnsupportedOSPlatform("windows")]
internal static class CiScript
{
    private static readonly TimeSpan ProcessTimeout = TimeSpan.FromSeconds(60);

    public static (int Code, string Output) Run(string script, string argument, string input)
    {
        ProcessStartInfo start = new("/bin/sh")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add(Path.Combine(RepoFolder(), ".github", "scripts", script));
        start.ArgumentList.Add(argument);
        start.Environment.Clear();
        start.Environment["PATH"] = "/usr/bin:/bin";

        using Process process = Process.Start(start)!;
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        try
        {
            process.StandardInput.Write(input);
            process.StandardInput.Close();
        }
        catch (IOException)
        {
            // The script may refuse its argument and exit before it reads stdin (broken pipe).
        }
        if (!process.WaitForExit(ProcessTimeout))
        {
            process.Kill(entireProcessTree: true);
            Assert.Fail(script + " did not exit within " + ProcessTimeout);
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
