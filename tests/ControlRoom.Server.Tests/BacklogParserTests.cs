namespace ControlRoom.Server.Tests;

public sealed class BacklogParserTests
{
    [Fact]
    public void Parse_GroupsMarkedLinesInFileOrderAndIgnoresTheRest()
    {
        Backlog backlog = BacklogParser.Parse(TempFactoryRoot.NormalBacklog.Split('\n'));

        Assert.Equal(
            [new BacklogEntry("First open", "do `this` → 125-x"), new BacklogEntry("Second open without rest", "")],
            backlog.Open);
        Assert.Equal([new BacklogEntry("Proposal", "wait for me", Mark: "?")], backlog.Approval);
        Assert.Equal([new BacklogEntry("Hand task", "for you", Mark: "h")], backlog.User);
        Assert.Equal(
            [new BacklogEntry("Done one", "finished", Mark: "x"), new BacklogEntry("Done two", "a: b", Mark: "x")],
            backlog.Done);
        Assert.All(backlog.All, entry => Assert.Null(entry.Number));
        Assert.All(backlog.All, entry => Assert.Empty(entry.After));
    }

    [Theory]
    [InlineData("Title: rest", "Title", "rest")]
    [InlineData("No separator", "No separator", "")]
    [InlineData("Colon:without space", "Colon:without space", "")]
    [InlineData("A: b: c", "A", "b: c")]
    [InlineData("Run `x: y` **bold** <b>tag</b>", "Run `x", "y` **bold** <b>tag</b>")]
    [InlineData("", "", "")]
    public void ToEntry_SplitsAtFirstColonSpace(string text, string title, string rest)
    {
        Assert.Equal(new BacklogEntry(title, rest), BacklogParser.ToEntry(text));
    }

    [Theory]
    [InlineData("## - [ ] heading")]
    [InlineData(" - [ ] Indented: x")]
    [InlineData("- [ ]")]
    [InlineData("- [-] Other: x")]
    [InlineData("- [H] Upper h: x")]
    [InlineData("Prose")]
    public void Parse_IgnoresLinesWithoutAKnownMark(string line)
    {
        Backlog backlog = BacklogParser.Parse([line]);

        Assert.Empty(backlog.Open);
        Assert.Empty(backlog.Approval);
        Assert.Empty(backlog.User);
        Assert.Empty(backlog.Done);
    }

    [Theory]
    [InlineData("- [ ] ", " ")]
    [InlineData("- [?] ", "?")]
    [InlineData("- [h] ", "h")]
    public void Parse_NumberedLineGivesNumberMarkTitleStoryAndAfter(string prefix, string mark)
    {
        Backlog backlog = BacklogParser.Parse(
            [prefix + "[#4](backlog/004-edit-backlogs.md) Edit backlogs (after: #3; Times#5)"]);

        BacklogEntry entry = Assert.Single(backlog.All);
        Assert.Equal(
            new BacklogEntry(
                "Edit backlogs",
                "",
                4,
                mark,
                "backlog/004-edit-backlogs.md",
                [new BacklogDependency(null, 3), new BacklogDependency("Times", 5)]),
            entry);
    }

    [Fact]
    public void Parse_NumberedLineWithoutAfterHasEmptyAfter()
    {
        BacklogEntry entry = Assert.Single(
            BacklogParser.Parse(["- [ ] [#1](backlog/001-installable-web-app.md) Installable web app"]).Open);

        Assert.Equal(1, entry.Number);
        Assert.Equal("Installable web app", entry.Title);
        Assert.Equal("", entry.Rest);
        Assert.Empty(entry.After);
    }

    [Fact]
    public void Parse_OldFormatDoneLineParsesAsBefore()
    {
        BacklogEntry entry = Assert.Single(BacklogParser.Parse(["- [x] Server view: text → 125-x"]).Done);

        Assert.Equal(new BacklogEntry("Server view", "text → 125-x", Mark: "x"), entry);
        Assert.Null(entry.Number);
    }

    [Theory]
    [InlineData("- [x] [#14](backlog/014-x.md) Title → 181-x")]
    [InlineData("- [X] [#14](backlog/014-x.md) Title → 181-x")]
    public void Parse_NumberedDoneLineKeepsJobTailInRest(string line)
    {
        BacklogEntry entry = Assert.Single(BacklogParser.Parse([line]).Done);

        Assert.Equal(new BacklogEntry("Title", "→ 181-x", 14, "x", "backlog/014-x.md"), entry);
    }

    [Fact]
    public void Parse_AfterAndJobTailTogether()
    {
        BacklogEntry entry = Assert.Single(
            BacklogParser.Parse(["- [x] [#27](backlog/027-my-tasks.md) My tasks (after: #4; #14) → 190-y"]).Done);

        Assert.Equal("My tasks", entry.Title);
        Assert.Equal("→ 190-y", entry.Rest);
        Assert.Equal([new BacklogDependency(null, 4), new BacklogDependency(null, 14)], entry.After);
    }

    [Theory]
    [InlineData("[#](backlog/004-x.md) Title: rest", "[#](backlog/004-x.md) Title", "rest")]
    [InlineData("[#a](backlog/004-x.md) Title: rest", "[#a](backlog/004-x.md) Title", "rest")]
    [InlineData("[#4](backlog/004-x.md Title: rest", "[#4](backlog/004-x.md Title", "rest")]
    [InlineData("[#4] Title: rest", "[#4] Title", "rest")]
    [InlineData("[#99999999999](backlog/x.md) Big", "[#99999999999](backlog/x.md) Big", "")]
    [InlineData("[#-4](backlog/x.md) Minus", "[#-4](backlog/x.md) Minus", "")]
    [InlineData("[#٤](backlog/x.md) Arabic digit", "[#٤](backlog/x.md) Arabic digit", "")]
    public void Parse_MalformedLinkFallsBackToOldParse(string text, string title, string rest)
    {
        BacklogEntry entry = Assert.Single(BacklogParser.Parse(["- [ ] " + text]).Open);

        Assert.Equal(new BacklogEntry(title, rest), entry);
        Assert.Null(entry.Number);
    }

    [Theory]
    [InlineData("(after: )")]
    [InlineData("(after: #)")]
    [InlineData("(after: x; #a; Times#)")]
    [InlineData("(after: #3")]
    public void Parse_MalformedAfterNeverThrows(string after)
    {
        BacklogEntry entry = Assert.Single(BacklogParser.Parse(["- [ ] [#4](backlog/004-x.md) Title " + after]).Open);

        Assert.Equal(4, entry.Number);
        Assert.Empty(entry.After);
    }

    [Fact]
    public async Task Parse_LongSpaceRunInANumberedLineStaysFast()
    {
        string line = "- [ ] [#4](backlog/004-x.md) A" + new string(' ', 1024 * 1024) + "B (after: #3) → 1-x";

        BacklogEntry entry = await Task.Run(() => Assert.Single(BacklogParser.Parse([line]).Open))
            .WaitAsync(TimeSpan.FromSeconds(20));

        Assert.Equal([new BacklogDependency(null, 3)], entry.After);
        Assert.Equal("→ 1-x", entry.Rest);
        Assert.EndsWith("B", entry.Title, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_StopLine_IsTheIndexOfTheFirstExactStopLineAndNoEntry()
    {
        string text =
            "# Backlog\n"                                   // 0
            + "- [ ] [#1](backlog/001-a.md) One\n"          // 1
            + "- [STOP] Auto stops here\n"                  // 2
            + "- [stop] auto stops here\n"                  // 3
            + "- [stop] Auto stops here now\n"              // 4
            + " - [stop] Auto stops here\n"                 // 5
            + "- [stop] Auto stops here\r\n"                // 6
            + "- [?] [#2](backlog/002-a.md) Two\n"          // 7
            + "- [stop] Auto stops here\n"                  // 8
            + "- [h] [#3](backlog/003-a.md) Three\n"        // 9
            + "- [x] [#4](backlog/004-a.md) Four\n"         // 10
            + "- [ ] [#5](backlog/005-a.md) Five\n";        // 11

        Backlog backlog = BacklogParser.Parse(text.Split('\n'));

        Assert.Equal(6, backlog.StopLine);
        Assert.Equal([1, 11], backlog.Open.Select(entry => entry.Line));
        Assert.Equal([7], backlog.Approval.Select(entry => entry.Line));
        Assert.Equal([9], backlog.User.Select(entry => entry.Line));
        Assert.Equal([10], backlog.Done.Select(entry => entry.Line));
        Assert.DoesNotContain(backlog.All, entry => entry.Title.Contains("stops", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("- [stop] Auto stops here", true)]
    [InlineData("- [stop] Auto stops here\r", true)]
    [InlineData("- [stop] Auto stops here\r\n", true)]
    [InlineData("- [stop] Auto stops here\n", true)]
    [InlineData("- [STOP] Auto stops here", false)]
    [InlineData("- [stop] auto stops here", false)]
    [InlineData("- [stop] Auto stops here.", false)]
    [InlineData("- [stop] Auto stops here ", false)]
    [InlineData("  - [stop] Auto stops here", false)]
    [InlineData("- [stop]", false)]
    [InlineData("", false)]
    public void IsStopLine_IsAnExactMatchAfterTheLineEnd(string line, bool expected)
    {
        Assert.Equal(expected, BacklogParser.IsStopLine(line));
    }

    [Fact]
    public void Parse_WithoutAStopLine_StopLineIsNull()
    {
        Assert.Null(BacklogParser.Parse(TempFactoryRoot.NormalBacklog.Split('\n')).StopLine);
        Assert.Null(BacklogParser.Parse(["- [STOP] Auto stops here", "- [stop] Auto stops"]).StopLine);
        Assert.Null(Backlog.Empty.StopLine);
    }
}
