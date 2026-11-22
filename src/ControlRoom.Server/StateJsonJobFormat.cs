using System.Text.RegularExpressions;

namespace ControlRoom.Server;

/// <summary>The factory's job format: one folder per job under <c>factory/jobs/</c>, read by
/// <see cref="JobReader"/> from its <c>state.json</c>, plus the <c>human.json</c> request of
/// <see cref="JobAction"/>.</summary>
internal sealed class StateJsonJobFormat : IJobFormat
{
    /// <summary>The stages of a running job.</summary>
    private static readonly string[] Active = ["spike", "spec", "build", "review", "approve"];

    /// <summary>The stages of a job that waits for the user.</summary>
    private static readonly string[] Blocked = ["needs-human"];

    /// <summary>The stage of a finished job.</summary>
    private const string Done = "done";

    /// <summary>Every stage.</summary>
    private static readonly string[] All = [.. Active, Done, .. Blocked];

    /// <summary>The files whose changes the change fingerprint watches.</summary>
    private static readonly string[] Records = [JobReader.StateFileName, JobAction.FileName];

    public string JobsFolderPath => "factory/jobs";

    public IReadOnlyList<string> RecordFileNames => Records;

    public IReadOnlyList<string> Stages => All;

    public IReadOnlyList<string> ActiveStages => Active;

    public IReadOnlyList<string> BlockedStages => Blocked;

    public string DoneStage => Done;

    public IReadOnlyList<string> ReviewKeys => JobReader.ReviewerKeys;

    public Regex IdPattern => JobReader.IdPattern();

    public IReadOnlyList<RoomJobFolder> JobFolders(string factoryRoot)
    {
        return [.. JobReader.ListFolders(factoryRoot).Select(folder => new RoomJobFolder(folder.Id, folder.Path))];
    }

    public IReadOnlyList<RoomJob> ReadAll(string factoryRoot)
    {
        return [.. JobReader.ReadAll(factoryRoot).Select(ToRoomJob)];
    }

    /// <summary>The fields RoomKit's views read.</summary>
    public static RoomJob ToRoomJob(JobSummary job)
    {
        ArgumentNullException.ThrowIfNull(job);
        return new RoomJob(
            job.Id,
            job.Project,
            job.Title,
            job.Stage,
            job.Mode,
            job.Path,
            job.Round,
            job.Decision,
            job.Note,
            job.CiStatus,
            job.Tokens,
            job.Updated);
    }
}
