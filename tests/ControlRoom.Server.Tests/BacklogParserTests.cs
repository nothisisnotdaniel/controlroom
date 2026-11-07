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
        Assert.Equal([new BacklogEntry("Proposal", "wait for me")], backlog.Approval);
        Assert.Equal([new BacklogEntry("Hand task", "for you")], backlog.User);
        Assert.Equal([new BacklogEntry("Done one", "finished"), new BacklogEntry("Done two", "a: b")], backlog.Done);
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
}
