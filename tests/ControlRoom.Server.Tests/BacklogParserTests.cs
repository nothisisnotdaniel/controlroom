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
}
