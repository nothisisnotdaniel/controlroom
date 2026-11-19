namespace ControlRoom.Server.Tests;

public sealed class JobReaderTests
{
    [Fact]
    public void Parse_MapsEveryKnownFieldFromTheRealShape()
    {
        JobSummary? job = JobReader.Parse("007-board", JobSamples.FullState);

        Assert.NotNull(job);
        Assert.Equal("007-board", job.Id);
        Assert.Equal("Alpha", job.Project);
        Assert.Equal("#2 Factory dashboard", job.Title);
        Assert.Equal("factory/007-board", job.Branch);
        Assert.Equal("review", job.Stage);
        Assert.Equal(2, job.Round);
        Assert.Equal("quality", job.Mode);
        Assert.Equal("full", job.Path);
        Assert.Equal("high", job.Risk);
        Assert.Equal(
            new Dictionary<string, string>
            {
                ["security"] = "PASS", ["ux"] = "pending", ["ui"] = "skipped", ["code"] = "CHANGES",
            },
            job.Reviews);
        IReadOnlyDictionary<string, string> past = Assert.Single(job.PastRounds);
        Assert.Equal("CHANGES", past["security"]);
        Assert.Equal("PASS", job.SpecReview);
        Assert.True(job.SpecRevised);
        Assert.Null(job.Decision);
        Assert.Equal("waiting on ci", job.Note);
        Assert.Equal("passed", job.CiStatus);
        Assert.Equal(1000 + 200000 + 3000 + 10 + 20 + 30, job.Tokens);
    }

    [Fact]
    public void Parse_MissingFieldsAreNullOrEmpty()
    {
        JobSummary? job = JobReader.Parse("1-a", "{}");

        Assert.NotNull(job);
        Assert.Equal("1-a", job.Id);
        Assert.Null(job.Project);
        Assert.Null(job.Title);
        Assert.Null(job.Branch);
        Assert.Null(job.Stage);
        Assert.Null(job.Round);
        Assert.Null(job.Mode);
        Assert.Null(job.Path);
        Assert.Null(job.Risk);
        Assert.Empty(job.Reviews);
        Assert.Empty(job.PastRounds);
        Assert.Null(job.SpecReview);
        Assert.Null(job.SpecRevised);
        Assert.Null(job.Decision);
        Assert.Null(job.Note);
        Assert.Null(job.CiStatus);
        Assert.Null(job.Tokens);
    }

    [Fact]
    public void Parse_WrongTypedFieldsAreNullOrEmpty()
    {
        JobSummary? job = JobReader.Parse("1-a", JobSamples.WrongTypes);

        Assert.NotNull(job);
        Assert.Equal(
            new JobSummary("1-a", null, null, null, null, null, null, null, null, job.Reviews, job.PastRounds, null,
                null, null, null, null, null),
            job);
        Assert.Empty(job.Reviews);
        Assert.Empty(job.PastRounds);
    }

    [Theory]
    [InlineData("{\"round\":-1}")]
    [InlineData("{\"round\":1.5}")]
    [InlineData("{\"round\":99999999999}")]
    public void Parse_RoundMustBeANonNegativeInt(string json)
    {
        Assert.Null(JobReader.Parse("1-a", json)!.Round);
    }

    [Fact]
    public void Parse_TokensSkipBadCounts()
    {
        JobSummary? job = JobReader.Parse(
            "1-a", "{\"tokens\":{\"a\":{\"input\":5,\"cacheRead\":\"7\",\"output\":-3},\"b\":{}}}");

        Assert.Equal(5, job!.Tokens);
    }

    [Theory]
    [InlineData("{\"tokens\":{}}")]
    [InlineData("{\"tokens\":{\"a\":{}}}")]
    [InlineData("{\"tokens\":{\"a\":\"x\",\"b\":{\"input\":-1,\"cacheRead\":\"7\",\"output\":1.5}}}")]
    [InlineData("{\"tokens\":{\"a\":{\"runs\":3,\"model\":\"opus\"}}}")]
    public void Parse_TokensAreUnknownWithoutAValidCount(string json)
    {
        Assert.Null(JobReader.Parse("1-a", json)!.Tokens);
    }

    [Fact]
    public void Parse_TokensOfOnlyZeroCountsAreZeroNotUnknown()
    {
        Assert.Equal(0, JobReader.Parse("1-a", "{\"tokens\":{\"a\":{\"input\":0}}}")!.Tokens);
    }

    [Fact]
    public void Parse_UpdatedParsesTheExactUtcForm()
    {
        JobSummary? job = JobReader.Parse("1-a", "{\"updated\":\"2026-11-19T23:59:58Z\"}");

        Assert.Equal(new DateTimeOffset(2026, 11, 19, 23, 59, 58, TimeSpan.Zero), job!.Updated);
        Assert.Equal(TimeSpan.Zero, job.Updated!.Value.Offset);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"updated\":null}")]
    [InlineData("{\"updated\":1763596800}")]
    [InlineData("{\"updated\":[\"2026-11-19T10:00:00Z\"]}")]
    [InlineData("{\"updated\":\"2026-11-19T10:00:00+01:00\"}")]
    [InlineData("{\"updated\":\"2026-11-19T10:00:00+00:00\"}")]
    [InlineData("{\"updated\":\"2026-11-19T10:00:00.123Z\"}")]
    [InlineData("{\"updated\":\"2026-11-19T10:00:00z\"}")]
    [InlineData("{\"updated\":\"2026-11-19 10:00:00Z\"}")]
    [InlineData("{\"updated\":\"2026-11-19T10:00:00\"}")]
    [InlineData("{\"updated\":\"2026-11-19\"}")]
    [InlineData("{\"updated\":\" 2026-11-19T10:00:00Z\"}")]
    [InlineData("{\"updated\":\"2026-11-19T10:00:00Z\\n\"}")]
    [InlineData("{\"updated\":\"2026-02-30T10:00:00Z\"}")]
    [InlineData("{\"updated\":\"2026-11-19T24:00:00Z\"}")]
    [InlineData("{\"updated\":\"２026-11-19T10:00:00Z\"}")]
    public void Parse_UpdatedIsNullForAnyOtherValue(string json)
    {
        Assert.Null(JobReader.Parse("1-a", json)!.Updated);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("[{\"project\":\"Alpha\"}]")]
    [InlineData("\"text\"")]
    [InlineData("42")]
    [InlineData("null")]
    public void Parse_RefusesWhatIsNotAJsonObject(string json)
    {
        Assert.Null(JobReader.Parse("1-a", json));
    }

    [Fact]
    public void ListFolders_KeepsValidNamesInOrdinalOrderAndSkipsSymlinks()
    {
        using TempFactoryRoot root = new();
        foreach (string id in new[]
                 {
                     "10-b", "2-a", "1-z", "123456-max", "1234567-long", "A-1", "1-Upper", "1_a", "-1", "1-",
                     "x-1", "1-a.b", ".1-a", "1-" + new string('a', 81),
                 })
        {
            root.AddJob(id, "{}");
        }

        string outside = Path.Combine(root.Path, ".outside");
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "state.json"), "{\"title\":\"secret\"}");
        Directory.CreateSymbolicLink(Path.Combine(root.JobsPath, "3-linked"), outside);
        File.WriteAllText(Path.Combine(root.JobsPath, "4-file"), "{}");

        Assert.Equal(
            ["1-z", "10-b", "123456-max", "2-a"],
            JobReader.ListFolders(root.Path).Select(folder => folder.Id));
    }

    [Fact]
    public void ReadAll_LeavesOutEveryRefusedStateAndKeepsTheRest()
    {
        using TempFactoryRoot root = new();
        root.AddJob("1-good", JobSamples.State("Alpha", "build", 1));
        root.AddJob("2-missing", null);
        root.AddJob("3-big", "{\"note\":\"" + new string('a', (int)JobReader.MaxStateBytes) + "\"}");
        root.AddJob("4-broken", "{\"project\":");
        root.AddJob("5-array", "[]");
        string linked = root.AddJob("6-linked-state", null);
        string outside = Path.Combine(root.Path, "outside.json");
        File.WriteAllText(outside, JobSamples.State("Secret", "build", 1));
        File.CreateSymbolicLink(Path.Combine(linked, "state.json"), outside);
        string folderState = root.AddJob("7-folder-state", null);
        Directory.CreateDirectory(Path.Combine(folderState, "state.json"));
        if (!OperatingSystem.IsWindows())
        {
            string fifo = root.AddJob("8-fifo", null);
            TempFactoryRoot.MakeFifo(Path.Combine(fifo, "state.json"));
        }

        root.AddJob("9-good", JobSamples.State("Beta", "done", 1));

        IReadOnlyList<JobSummary> jobs = JobReader.ReadAll(root.Path);

        Assert.Equal(["1-good", "9-good"], jobs.Select(job => job.Id));
    }

    [Fact]
    public void ReadAll_StateExactlyAtTheCapIsRead()
    {
        using TempFactoryRoot root = new();
        string head = "{\"note\":\"";
        string tail = "\"}";
        root.AddJob("1-edge", head + new string('a', (int)JobReader.MaxStateBytes - head.Length - tail.Length) + tail);

        Assert.Single(JobReader.ReadAll(root.Path));
    }

    [Fact]
    public void JobsFolder_MissingFactoryOrJobsGivesNoJobs()
    {
        using TempFactoryRoot root = new();
        Assert.Null(JobReader.JobsFolder(root.Path));
        Directory.CreateDirectory(Path.Combine(root.Path, "factory"));
        Assert.Null(JobReader.JobsFolder(root.Path));
        Assert.Empty(JobReader.ReadAll(root.Path));
    }

    [Fact]
    public void JobsFolder_SymlinkedJobsIsRefused()
    {
        using TempFactoryRoot root = new();
        string real = Path.Combine(root.Path, ".real-jobs");
        Directory.CreateDirectory(Path.Combine(real, "1-a"));
        File.WriteAllText(Path.Combine(real, "1-a", "state.json"), "{}");
        Directory.CreateDirectory(Path.Combine(root.Path, "factory"));
        Directory.CreateSymbolicLink(root.JobsPath, real);

        Assert.Null(JobReader.JobsFolder(root.Path));
        Assert.Empty(JobReader.ReadAll(root.Path));
    }

    [Fact]
    public void JobsFolder_SymlinkedFactoryIsRefused()
    {
        using TempFactoryRoot root = new();
        string real = Path.Combine(root.Path, ".real-factory");
        Directory.CreateDirectory(Path.Combine(real, "jobs", "1-a"));
        File.WriteAllText(Path.Combine(real, "jobs", "1-a", "state.json"), "{}");
        Directory.CreateSymbolicLink(Path.Combine(root.Path, "factory"), real);

        Assert.Null(JobReader.JobsFolder(root.Path));
        Assert.Empty(JobReader.ReadAll(root.Path));
    }
}
