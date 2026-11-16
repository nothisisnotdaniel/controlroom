using System.ComponentModel;
using System.Diagnostics;

namespace ControlRoom.Server;

/// <summary>The result of one git call: the exit code (-1 when git could not start or timed out) and its standard
/// output.</summary>
internal sealed record GitResult(int ExitCode, string Output)
{
    public bool Ok => ExitCode == 0;
}

/// <summary>Git calls in a project's own repo, each with a fixed argument list (never a shell), a timeout
/// (30 s, 60 s for push), no terminal prompt and no repo discovery above the project folder. Request text never
/// becomes an argument; the only variable arguments are the commit message (built from a number and an action)
/// and the file paths the server chose.</summary>
internal static class ProjectGit
{
    public const string Branch = "develop";

    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    public static readonly TimeSpan PushTimeout = TimeSpan.FromSeconds(60);

    // Variables that would point git at another repo than the project's own.
    private static readonly string[] RepoVariables =
    [
        "GIT_DIR", "GIT_WORK_TREE", "GIT_INDEX_FILE", "GIT_OBJECT_DIRECTORY", "GIT_ALTERNATE_OBJECT_DIRECTORIES",
        "GIT_COMMON_DIR", "GIT_NAMESPACE", "GIT_PREFIX",
    ];

    /// <summary>The current branch, or <c>null</c> when the project folder has no repo of its own or git
    /// fails.</summary>
    public static async Task<string?> CurrentBranch(string folder)
    {
        string dotGit = Path.Combine(folder, ".git");
        if (!Directory.Exists(dotGit) && !File.Exists(dotGit))
        {
            return null;
        }

        GitResult result = await Run(folder, ["branch", "--show-current"], Timeout);
        return result.Ok ? result.Output.Trim() : null;
    }

    /// <summary>True when <c>git status --porcelain -- backlog.md backlog</c> prints nothing (a failed call counts
    /// as not clean).</summary>
    public static async Task<bool> BacklogIsClean(string folder)
    {
        GitResult result = await Run(
            folder, ["status", "--porcelain", "--", BacklogReader.FileName, StoryReader.FolderName], Timeout);
        return result.Ok && result.Output.Trim().Length == 0;
    }

    /// <summary>Stages the files (new and deleted ones too) and commits exactly them.</summary>
    public static async Task<bool> Commit(string folder, string message, IReadOnlyList<string> files)
    {
        GitResult add = await Run(folder, ["add", "--", .. files], Timeout);
        if (!add.Ok)
        {
            return false;
        }

        GitResult commit = await Run(folder, ["commit", "-q", "-m", message, "--", .. files], Timeout);
        return commit.Ok;
    }

    /// <summary>Resets the index for the files to <c>HEAD</c> (after a failed commit).</summary>
    public static async Task Unstage(string folder, IReadOnlyList<string> files)
    {
        await Run(folder, ["reset", "-q", "--", .. files], Timeout);
    }

    public static async Task<bool> HasOrigin(string folder)
    {
        GitResult result = await Run(folder, ["remote"], Timeout);
        return result.Ok && result.Output.Split('\n').Any(line => line.Trim() == "origin");
    }

    public static async Task<bool> Push(string folder)
    {
        GitResult result = await Run(folder, ["push", "-q", "origin", Branch], PushTimeout);
        return result.Ok;
    }

    public static async Task<GitResult> Run(string folder, IReadOnlyList<string> arguments, TimeSpan timeout)
    {
        ProcessStartInfo start = new("git")
        {
            WorkingDirectory = folder,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        foreach (string name in RepoVariables)
        {
            start.Environment.Remove(name);
        }

        start.Environment["GIT_TERMINAL_PROMPT"] = "0";
        start.Environment["GIT_OPTIONAL_LOCKS"] = "0";
        start.Environment["GIT_CEILING_DIRECTORIES"] = Path.GetDirectoryName(Path.GetFullPath(folder)) ?? "";

        using Process process = new() { StartInfo = start };
        try
        {
            if (!process.Start())
            {
                return new GitResult(-1, "");
            }
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            return new GitResult(-1, "");
        }

        process.StandardInput.Close();
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        using CancellationTokenSource cancel = new(timeout);
        try
        {
            await process.WaitForExitAsync(cancel.Token);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (Exception exception) when (exception is InvalidOperationException or Win32Exception)
            {
                // Already gone.
            }

            return new GitResult(-1, "");
        }

        await error;
        return new GitResult(process.ExitCode, await output);
    }
}
