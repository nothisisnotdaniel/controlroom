namespace ControlRoom.Server.Tests;

/// <summary>Writes item claims straight into a local bare <c>origin</c> as job.py makes them: a parentless commit on
/// the empty tree whose message holds <c>Job:</c>, <c>Machine:</c>, <c>Project:</c> and <c>Claimed:</c>, under
/// <c>refs/factory/claims/&lt;name&gt;</c>. No transport and no network is used.</summary>
internal static class ClaimFixture
{
    /// <summary>Git's empty tree (SHA-1 repos).</summary>
    public const string EmptyTree = "4b825dc642cb6eb9a060e54bf8d69288fbee4904";

    /// <summary>job.py's claim message.</summary>
    public static string Message(string job, string machine, string project, string claimed)
    {
        return "claim " + job + "\n\nJob: " + job + "\nMachine: " + machine + "\nProject: " + project + "\nClaimed: "
            + claimed + "\n";
    }

    /// <summary>Points <c>refs/factory/claims/&lt;name&gt;</c> in the bare remote at a new claim commit with the
    /// message, and returns its sha.</summary>
    public static string Push(string remote, string name, string message)
    {
        TempFactoryRoot.Git(remote, "config", "user.name", "Other Machine");
        TempFactoryRoot.Git(remote, "config", "user.email", "other@example.invalid");
        string sha = TempFactoryRoot.Git(remote, "commit-tree", EmptyTree, "-m", message);
        TempFactoryRoot.Git(remote, "update-ref", "refs/factory/claims/" + name, sha);
        return sha;
    }

    /// <summary>A project folder with the backlog, its own git repo on <c>develop</c> and a local bare origin;
    /// returns (folder, remote).</summary>
    public static (string Folder, string Remote) Project(TempFactoryRoot root, string name, string backlog)
    {
        string folder = root.AddProject(name, backlog);
        TempFactoryRoot.InitGit(folder, "develop");
        return (folder, root.AddBareRemote(folder));
    }
}
