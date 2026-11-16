using System.Text;

namespace ControlRoom.Server.Tests;

/// <summary>Pure line edits of <c>backlog.md</c> (no git, no files).</summary>
public sealed class BacklogEditorTests
{
    private const string Sample =
        "# Backlog\n"                                                     // 0
        + "\n"                                                            // 1
        + "- [x] [#1](backlog/001-done.md) Done one\n"                    // 2
        + "- [ ] [#2](backlog/002-open.md) Open two (after: #1) → 9-x\n"  // 3
        + "Some prose stays here.\n"                                      // 4
        + "- [?] [#3](backlog/003-proposal.md) Proposal three\n"          // 5
        + "- [h] Hand task: for you\n"                                    // 6
        + "  - [ ] Indented: not an item\n"                               // 7
        + "- [?] Old proposal: no number\n"                               // 8
        + "\n"                                                            // 9
        + "Trailing prose.\n";                                            // 10

    [Fact]
    public void Parse_ThenToBytes_KeepsEveryByte()
    {
        foreach (string text in new[] { Sample, "no final newline", "", "\n", "a\r\nb\r\n", "\uFEFF- [ ] x\n" })
        {
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            EditableText parsed = EditableText.Parse(bytes)!;
            Assert.Equal(bytes, parsed.ToBytes());
        }
    }

    [Fact]
    public void Parse_RefusesInvalidUtf8()
    {
        Assert.Null(EditableText.Parse([0x2D, 0x20, 0xFF, 0x0A]));
    }

    [Fact]
    public void Approve_TurnsAProposalOpenAndKeepsTheRest()
    {
        BacklogEdit edit = BacklogEditor.Approve(Text(Sample), 5)!;

        Assert.Equal(Sample.Replace("- [?] [#3]", "- [ ] [#3]", StringComparison.Ordinal), edit.Text.Text);
        Assert.Equal("#3", edit.Subject);
        Assert.Equal("line 9", BacklogEditor.Approve(Text(Sample), 8)!.Subject);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(-1)]
    [InlineData(11)]
    public void Approve_And_Drop_RefuseAnyOtherLine(int line)
    {
        Assert.Null(BacklogEditor.Approve(Text(Sample), line));
        Assert.Null(BacklogEditor.Drop(Text(Sample), line));
    }

    [Fact]
    public void Drop_RemovesOnlyThatLine()
    {
        BacklogEdit edit = BacklogEditor.Drop(Text(Sample), 5)!;

        Assert.Equal(Sample.Replace("- [?] [#3](backlog/003-proposal.md) Proposal three\n", "", StringComparison.Ordinal),
            edit.Text.Text);
        Assert.Equal("#3", edit.Subject);
    }

    [Fact]
    public void MoveUpAndDown_SwapWithTheNearestItemAndKeepProse()
    {
        BacklogEdit up = BacklogEditor.Move(Text(Sample), 5, MoveDirection.Up)!;
        string[] lines = up.Text.Lines.ToArray();
        Assert.StartsWith("- [?] [#3]", lines[3], StringComparison.Ordinal);
        Assert.Equal("Some prose stays here.", lines[4]);
        Assert.StartsWith("- [ ] [#2]", lines[5], StringComparison.Ordinal);

        BacklogEdit down = BacklogEditor.Move(Text(Sample), 6, MoveDirection.Down)!;
        lines = down.Text.Lines.ToArray();
        Assert.Equal("- [?] Old proposal: no number", lines[6]);
        Assert.Equal("  - [ ] Indented: not an item", lines[7]);
        Assert.Equal("- [h] Hand task: for you", lines[8]);
        Assert.Equal("line 7", down.Subject);
    }

    [Fact]
    public void Move_ThatChangesNothingOrADoneLine_IsRefused()
    {
        string text = "Intro\n- [ ] A\nprose\n- [ ] B\n";
        Assert.Null(BacklogEditor.Move(Text(text), 1, MoveDirection.Up));
        Assert.Null(BacklogEditor.Move(Text(text), 3, MoveDirection.Down));
        Assert.Null(BacklogEditor.Move(Text(text), 1, MoveDirection.Top));
        Assert.Null(BacklogEditor.Move(Text(text), 2, MoveDirection.Up));
        Assert.Null(BacklogEditor.Move(Text(Sample), 2, MoveDirection.Down));
    }

    [Fact]
    public void MoveTop_PutsTheLineBeforeTheFirstOpenItem()
    {
        BacklogEdit edit = BacklogEditor.Move(Text(Sample), 8, MoveDirection.Top)!;

        string[] lines = edit.Text.Lines.ToArray();
        Assert.StartsWith("- [x] [#1]", lines[2], StringComparison.Ordinal);
        Assert.Equal("- [?] Old proposal: no number", lines[3]);
        Assert.StartsWith("- [ ] [#2]", lines[4], StringComparison.Ordinal);
        Assert.Equal("Some prose stays here.", lines[5]);
        Assert.Equal(Sample.Length, edit.Text.Text.Length);
    }

    [Fact]
    public void Title_ReplacesOnlyTheTitleAndKeepsLinkAfterAndTail()
    {
        BacklogEdit edit = BacklogEditor.Title(Text(Sample), 3, "New  title")!;

        Assert.Equal("- [ ] [#2](backlog/002-open.md) New title (after: #1) → 9-x", edit.Text.Lines[3]);
        Assert.Equal(Sample.Replace("Open two", "New title", StringComparison.Ordinal), edit.Text.Text);
        Assert.Equal("#2", edit.Subject);
        Assert.Equal(
            "- [?] [#3](backlog/003-proposal.md) Other",
            BacklogEditor.Title(Text(Sample), 5, "Other")!.Text.Lines[5]);
    }

    [Fact]
    public void Title_KeepsACarriageReturnAndRefusesDoneUnnumberedAndSameTitle()
    {
        BacklogEdit edit = BacklogEditor.Title(Text("- [ ] [#4](backlog/004-a.md) Old\r\n"), 0, "New")!;
        Assert.Equal("- [ ] [#4](backlog/004-a.md) New\r\n", edit.Text.Text);

        Assert.Null(BacklogEditor.Title(Text(Sample), 2, "Other"));
        Assert.Null(BacklogEditor.Title(Text(Sample), 6, "Other"));
        Assert.Null(BacklogEditor.Title(Text(Sample), 3, "Open two"));
    }

    [Fact]
    public void Add_TopGoesBeforeTheFirstOpenItemAndBottomAfterTheLastItem()
    {
        BacklogEdit top = BacklogEditor.Add(Text(Sample), 12, "Edit backlogs", AddPosition.Top);
        Assert.Equal("- [ ] [#12](backlog/012-edit-backlogs.md) Edit backlogs", top.Text.Lines[3]);
        Assert.StartsWith("- [ ] [#2]", top.Text.Lines[4], StringComparison.Ordinal);
        Assert.Equal("#12", top.Subject);

        BacklogEdit bottom = BacklogEditor.Add(Text(Sample), 12, "Edit backlogs", AddPosition.Bottom);
        Assert.Equal("- [?] Old proposal: no number", bottom.Text.Lines[8]);
        Assert.Equal("- [ ] [#12](backlog/012-edit-backlogs.md) Edit backlogs", bottom.Text.Lines[9]);
        Assert.Equal("", bottom.Text.Lines[10]);
        Assert.EndsWith("Trailing prose.\n", bottom.Text.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Add_ToAFileWithoutItemsGoesToTheEnd()
    {
        Assert.Equal(
            "# Title\n- [ ] [#1](backlog/001-a.md) A\n",
            BacklogEditor.Add(Text("# Title\n"), 1, "A", AddPosition.Top).Text.Text);
        Assert.Equal(
            "- [ ] [#1](backlog/001-a.md) A\n",
            BacklogEditor.Add(Text(""), 1, "A", AddPosition.Bottom).Text.Text);
        Assert.Equal(
            "- [x] Done\n- [ ] [#1](backlog/001-a.md) A",
            BacklogEditor.Add(Text("- [x] Done"), 1, "A", AddPosition.Top).Text.Text);
    }

    [Fact]
    public void NextNumber_IsTheHighestOfLinesAndFilesPlusOne()
    {
        Assert.Equal(4, BacklogEditor.NextNumber(Text(Sample), []));
        Assert.Equal(
            42,
            BacklogEditor.NextNumber(Text(Sample), ["041-gap.md", "notes.md", "x-12.md", "010-older.md"]));
        Assert.Equal(8, BacklogEditor.NextNumber(Text("- [x] [#7](backlog/007-a.md) A\nnot [#99](x) an item\n"), []));
        Assert.Equal(1, BacklogEditor.NextNumber(Text(""), []));
    }

    [Theory]
    [InlineData("Edit backlogs", "edit-backlogs")]
    [InlineData("  Hello, World!  ", "hello-world")]
    [InlineData("one two three four five six seven", "one-two-three-four-five-six")]
    [InlineData("Ünïcödé — only", "n-c-d-only")]
    [InlineData("!!!", "item")]
    [InlineData("abcdefghijklmnopqrstuvwxyz abcdefghijklmnopqrstuvwxyz", "abcdefghijklmnopqrstuvwxyz-abcdefghijklmnopqrstuvw")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa b", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public void Slugify_MatchesJobPy(string title, string slug)
    {
        Assert.Equal(slug, BacklogEditor.Slugify(title));
    }

    [Fact]
    public void RenderStory_MatchesJobPy()
    {
        Assert.Equal(
            "# 007 Edit backlogs\n\n## What\n\nDo it.\n\n## Why\n\nBecause.\n",
            BacklogEditor.RenderStory(7, " Edit backlogs ", null, " Do it.\n", "Because."));
        Assert.Equal(
            "# 1234 T\n\nAs a user.\n\n## What\n\nA\nB\n\n## Why\n\nC\n",
            BacklogEditor.RenderStory(1234, "T", "  As a user.\n", "A\nB", "C"));
        Assert.Equal(
            "# 002 T\n\n\n\n## What\n\nw\n\n## Why\n\ny\n",
            BacklogEditor.RenderStory(2, "T", "   ", "w", "y"));
    }

    [Fact]
    public void RetitleStory_ChangesOnlyAnExactHeading()
    {
        Assert.Equal("# 004 New\n\nBody\n", BacklogEditor.RetitleStory("# 004 Old\n\nBody\n", 4, "Old", "New"));
        Assert.Equal("# 004 New", BacklogEditor.RetitleStory("# 004 Old", 4, "Old", "New"));
        Assert.Null(BacklogEditor.RetitleStory("# 004 Older\n", 4, "Old", "New"));
        Assert.Null(BacklogEditor.RetitleStory("# 4 Old\n", 4, "Old", "New"));
        Assert.Null(BacklogEditor.RetitleStory("Intro\n# 004 Old\n", 4, "Old", "New"));
    }

    [Theory]
    [InlineData("  Fine title  ", "Fine title")]
    [InlineData("Fine (not after) title", "Fine (not after) title")]
    [InlineData("x", "x")]
    public void CleanTitle_TrimsGoodTitles(string title, string clean)
    {
        Assert.Equal(clean, BacklogEditor.CleanTitle(title));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("two\nlines")]
    [InlineData("tab\there")]
    [InlineData("bell\u0007")]
    [InlineData("line\u2028sep")]
    [InlineData("arrow → job")]
    [InlineData("needs (after: #3)")]
    public void CleanTitle_RefusesBadTitles(string title)
    {
        Assert.Null(BacklogEditor.CleanTitle(title));
    }

    [Fact]
    public void CleanTitle_And_IsStoryText_RefuseALoneSurrogate()
    {
        string lone = "lone " + (char)0xD800 + " surrogate";
        Assert.Null(BacklogEditor.CleanTitle(lone));
        Assert.False(BacklogEditor.IsStoryText(lone));
    }

    [Fact]
    public void CleanTitle_AllowsAtMost200Characters()
    {
        Assert.NotNull(BacklogEditor.CleanTitle(new string('a', 200)));
        Assert.Null(BacklogEditor.CleanTitle(new string('a', 201)));
        Assert.NotNull(BacklogEditor.CleanTitle(string.Concat(Enumerable.Repeat("😀", 200))));
    }

    [Fact]
    public void IsStoryText_AllowsLfAndTabOnly()
    {
        Assert.True(BacklogEditor.IsStoryText("# 001 A\n\n\tcode\n"));
        Assert.False(BacklogEditor.IsStoryText("a\r\nb"));
        Assert.False(BacklogEditor.IsStoryText("nul\0"));
    }

    private static EditableText Text(string text)
    {
        return EditableText.Parse(Encoding.UTF8.GetBytes(text))!;
    }
}
