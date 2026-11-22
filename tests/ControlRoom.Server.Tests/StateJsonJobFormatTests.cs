namespace ControlRoom.Server.Tests;

/// <summary>ControlRoom's <see cref="IJobFormat"/>: today's stages, keys and paths, read through
/// <see cref="JobReader"/>.</summary>
public sealed class StateJsonJobFormatTests
{
    [Fact]
    public void Values_AreTodaysRules()
    {
        StateJsonJobFormat format = new();

        Assert.Equal(["spike", "spec", "build", "review", "approve"], format.ActiveStages);
        Assert.Equal(["needs-human"], format.BlockedStages);
        Assert.Equal("done", format.DoneStage);
        Assert.Equal(["spike", "spec", "build", "review", "approve", "done", "needs-human"], format.Stages);
        Assert.Equal(["security", "ux", "ui", "code"], format.ReviewKeys);
        Assert.Equal("factory/jobs", format.JobsFolderPath);
        Assert.Equal(["state.json", "human.json"], format.RecordFileNames);
        Assert.Same(JobReader.IdPattern(), format.IdPattern);
        Assert.IsType<StateJsonJobFormat>(ControlRoomApp.Jobs);
    }

    [Fact]
    public void ReadAll_MapsEveryJobReaderField()
    {
        using TempFactoryRoot root = new();
        string jobs = Path.Combine(root.Path, "factory", "jobs");
        Directory.CreateDirectory(Path.Combine(jobs, "007-board"));
        File.WriteAllText(Path.Combine(jobs, "007-board", "state.json"), JobSamples.FullState);
        Directory.CreateDirectory(Path.Combine(jobs, "008-next"));
        File.WriteAllText(Path.Combine(jobs, "008-next", "state.json"), JobSamples.State("Beta", "needs-human", 1));
        StateJsonJobFormat format = new();

        IReadOnlyList<JobSummary> expected = JobReader.ReadAll(root.Path);
        IReadOnlyList<RoomJob> jobsRead = format.ReadAll(root.Path);

        Assert.Equal(2, jobsRead.Count);
        Assert.Equal(expected.Select(StateJsonJobFormat.ToRoomJob), jobsRead);
        RoomJob board = jobsRead[0];
        Assert.Equal("007-board", board.Id);
        Assert.Equal("Alpha", board.Project);
        Assert.Equal("review", board.Stage);
        Assert.Equal("quality", board.Mode);
        Assert.Equal(2, board.Round);
        Assert.Equal(expected[0].Tokens, board.Tokens);
        Assert.Equal(expected[0].CiStatus, board.CiStatus);
        Assert.Equal(["007-board", "008-next"], format.JobFolders(root.Path).Select(folder => folder.Id));
        Assert.Equal(Path.Combine(jobs, "008-next"), format.JobFolders(root.Path)[1].Path);
    }
}
