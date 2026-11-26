using System.Diagnostics;

namespace ControlRoom.Server.Tests;

/// <summary><c>.github/scripts/ci_changes.py</c> (#471): runs its Python suite,
/// <c>.github/scripts/tests/test_ci_changes.py</c>, with <c>python3 -I -m unittest discover</c> from the repo folder.
/// Each Python test builds its own git repo in its own temp folder.</summary>
public sealed class CiChangesScriptTests
{
    private static readonly TimeSpan SuiteTimeout = TimeSpan.FromSeconds(120);

    [Fact]
    public async Task PythonSuite_Passes()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        ProcessStartInfo start = new("python3")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = RepoFolder(),
        };
        foreach (string argument in new[] { "-I", "-m", "unittest", "discover", "-s", ".github/scripts/tests" })
        {
            start.ArgumentList.Add(argument);
        }

        // The suite's git calls must work on their own temp repos, never on a repo a hook or caller points git at.
        foreach (string name in new[] { "GIT_DIR", "GIT_WORK_TREE", "GIT_INDEX_FILE", "GIT_OBJECT_DIRECTORY" })
        {
            start.Environment.Remove(name);
        }

        using Process process = Process.Start(start)!;
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(SuiteTimeout))
        {
            process.Kill(entireProcessTree: true);
            Assert.Fail("the ci_changes.py suite did not exit within " + SuiteTimeout);
        }

        process.WaitForExit();
        string text = await output + await error;
        Assert.True(process.ExitCode == 0, "python3 -I -m unittest discover -s .github/scripts/tests exited "
            + process.ExitCode + ":\n" + text);
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
