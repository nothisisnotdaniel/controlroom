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

    private const string Mixed =
        "# Backlog\r\n"                        // 0
        + "- [ ] [#1](backlog/001-a.md) A\r\n"  // 1
        + "- [?] [#2](backlog/002-p.md) P\n"    // 2
        + "\r\n"                               // 3
        + "- [ ] [#3](backlog/003-b.md) B\n"    // 4
        + "- [x] [#4](backlog/004-d.md) D\n"    // 5
        + "- [h] [#5](backlog/005-h.md) H\n"    // 6
        + "Prose here.\r\n"                    // 7
        + "- [ ] [#6](backlog/006-c.md) C\r\n"  // 8
        + "- [?] Q old\n"                       // 9
        + "- [ ] D old\n"                       // 10
        + "End.";                               // 11

    /// <summary>The <c>[ ]</c> group is lines 1, 4, 8, 10 (A, B, C, D); the <c>[?]</c> group is lines 2, 9.
    /// <paramref name="order"/> is the expected group order as the source line indexes.</summary>
    [Theory]
    [InlineData(1, 8, false, "#1", new[] { 4, 8, 1, 10 })]   // first, forward, after
    [InlineData(1, 10, false, "#1", new[] { 4, 8, 10, 1 })]  // first to last
    [InlineData(1, 8, true, "#1", new[] { 4, 1, 8, 10 })]    // forward, before, over the done line
    [InlineData(10, 1, true, "line 11", new[] { 10, 1, 4, 8 })] // last to first, unnumbered
    [InlineData(8, 4, true, "#6", new[] { 1, 8, 4, 10 })]    // backward, before
    [InlineData(10, 1, false, "line 11", new[] { 1, 10, 4, 8 })] // backward, after
    [InlineData(4, 8, false, "#3", new[] { 1, 8, 4, 10 })]   // next, after
    public void Place_ReordersTheOpenGroupInItsOwnIndexes(int line, int target, bool before, string subject, int[] order)
    {
        AssertPlaced(Mixed, line, target, before, subject, [1, 4, 8, 10], order);
    }

    [Theory]
    [InlineData(9, 2, true, "line 10")]
    [InlineData(2, 9, false, "#2")]
    public void Place_ReordersTheApprovalGroup(int line, int target, bool before, string subject)
    {
        AssertPlaced(Mixed, line, target, before, subject, [2, 9], [9, 2]);
    }

    [Theory]
    [InlineData(1, 2, true)]   // marks differ
    [InlineData(2, 4, false)]  // marks differ
    [InlineData(1, 1, true)]   // same line
    [InlineData(1, 5, true)]   // done target
    [InlineData(5, 1, true)]   // done line
    [InlineData(6, 1, true)]   // [h] line
    [InlineData(1, 6, false)]  // [h] target
    [InlineData(1, 7, true)]   // prose
    [InlineData(3, 1, true)]   // blank
    [InlineData(1, 4, true)]   // no-op: already before
    [InlineData(4, 1, false)]  // no-op: already after
    [InlineData(10, 8, false)] // no-op at the end
    [InlineData(-1, 1, true)]
    [InlineData(1, 12, true)]
    [InlineData(12, 1, true)]
    public void Place_RefusesOtherLinesAndNoOps(int line, int target, bool before)
    {
        Assert.Null(BacklogEditor.Place(Text(Mixed), line, target, before));
    }

    [Fact]
    public void Move_ThatChangesNothingOrADoneLine_IsRefused()
    {
        string text = "Intro\n- [ ] A\nprose\n- [ ] B\n";
        Assert.Null(BacklogEditor.Place(Text(text), 1, 3, true));
        Assert.Null(BacklogEditor.Place(Text(text), 3, 1, false));
        Assert.Null(BacklogEditor.Move(Text(text), 1, MoveDirection.Top));
        Assert.Null(BacklogEditor.Place(Text(text), 2, 1, true));
        Assert.Null(BacklogEditor.Place(Text(Sample), 2, 3, true));
    }

    private static void AssertPlaced(
        string source, int line, int target, bool before, string subject, int[] slots, int[] order)
    {
        EditableText text = Text(source);
        BacklogEdit edit = BacklogEditor.Place(text, line, target, before)!;

        Assert.Equal(subject, edit.Subject);
        List<string> expected = [.. text.Lines];
        for (int i = 0; i < slots.Length; i++)
        {
            expected[slots[i]] = text.Lines[order[i]];
        }

        Assert.Equal(expected, edit.Text.Lines);
        for (int i = 0; i < text.Lines.Count; i++)
        {
            if (!slots.Contains(i))
            {
                Assert.Equal(text.Lines[i], edit.Text.Lines[i]);
            }
        }

        byte[] bytes = edit.Text.ToBytes();
        Assert.Equal(Encoding.UTF8.GetBytes(source).Length, bytes.Length);
        Assert.Equal(Encoding.UTF8.GetBytes(string.Join("\n", expected)), bytes);
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

    [Fact]
    public void Done_TicksOffAHandLineAndKeepsEveryOtherByte()
    {
        BacklogEdit edit = BacklogEditor.Done(Text(Sample), 6)!;

        Assert.Equal(
            Encoding.UTF8.GetBytes(Sample.Replace("- [h] Hand task", "- [x] Hand task", StringComparison.Ordinal)),
            edit.Text.ToBytes());
        Assert.Equal("line 7", edit.Subject);
    }

    [Fact]
    public void Done_OnANumberedLineNamesItsNumber_AndKeepsCrlfAndTheTail()
    {
        const string Crlf = "# B\r\n- [h] [#4](backlog/004-hand.md) Hand four (after: Other#1) → 3-x\r\n- [ ] Next: line\r\n";

        BacklogEdit edit = BacklogEditor.Done(Text(Crlf), 1)!;

        Assert.Equal(
            Encoding.UTF8.GetBytes(Crlf.Replace("- [h] [#4]", "- [x] [#4]", StringComparison.Ordinal)),
            edit.Text.ToBytes());
        Assert.Equal("#4", edit.Subject);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(-1)]
    [InlineData(11)]
    public void Done_RefusesEveryLineThatIsNotHand(int line)
    {
        Assert.Null(BacklogEditor.Done(Text(Sample), line));
    }

    private static readonly DateOnly NoteDate = new(2026, 3, 2);

    [Fact]
    public void AppendNote_GoesAfterTheLastLineOfTheNotesSectionBeforeTheNextHeading()
    {
        string story = "# 004 Four\n\n## Notes\n\nSome remark.\n\n\n## Other\n\nText.\n";

        string result = BacklogEditor.AppendNote(Text(story), NoteDate, "Use the blue one.").Text;

        Assert.Equal(
            "# 004 Four\n\n## Notes\n\nSome remark.\n\n- 2026-03-02: Use the blue one.\n\n\n## Other\n\nText.\n", result);
    }

    [Fact]
    public void AppendNote_SectionEndsOnlyAtALevelOneOrTwoHeading()
    {
        string story = "# 004 Four\n\n## Notes\n\n### Sub\n\nDeep.\n\n# Next\n";

        string result = BacklogEditor.AppendNote(Text(story), NoteDate, "n").Text;

        Assert.Equal("# 004 Four\n\n## Notes\n\n### Sub\n\nDeep.\n\n- 2026-03-02: n\n\n# Next\n", result);
    }

    [Fact]
    public void AppendNote_AtTheEndOfTheFileFollowsAPreviousNoteWithoutABlankLine()
    {
        string story = "# 004 Four\n\n## Notes\n\n- 2026-01-05: First.\n\n";

        string result = BacklogEditor.AppendNote(Text(story), NoteDate, "Second.").Text;

        Assert.Equal("# 004 Four\n\n## Notes\n\n- 2026-01-05: First.\n- 2026-03-02: Second.\n\n", result);
    }

    [Fact]
    public void AppendNote_AfterAParagraphOrAPlainListLineAddsOneBlankLine()
    {
        Assert.Equal(
            "## Notes\nProposed by job 12-x.\n\n- 2026-03-02: n\n",
            BacklogEditor.AppendNote(Text("## Notes\nProposed by job 12-x.\n"), NoteDate, "n").Text);
        Assert.Equal(
            "## Notes\n\n- plain item\n\n- 2026-03-02: n\n",
            BacklogEditor.AppendNote(Text("## Notes\n\n- plain item\n"), NoteDate, "n").Text);
    }

    [Fact]
    public void AppendNote_EmptySectionPutsTheNoteAfterTheHeadingAndUsesTheFirstSection()
    {
        Assert.Equal(
            "## Notes\n\n- 2026-03-02: n\n\n## Notes\n\nSecond.\n",
            BacklogEditor.AppendNote(Text("## Notes\n\n## Notes\n\nSecond.\n"), NoteDate, "n").Text);
    }

    [Fact]
    public void AppendNote_HeadingIsMatchedExactlyAndCaseSensitively()
    {
        string story = "# 004 Four\n\n## notes\n\nlower.\n";

        string result = BacklogEditor.AppendNote(Text(story), NoteDate, "n").Text;

        Assert.Equal(story + "\n## Notes\n\n- 2026-03-02: n\n", result);
    }

    [Fact]
    public void AppendNote_KeepsCrlfAndTheBom()
    {
        byte[] bytes = Encoding.UTF8.GetBytes("\uFEFF# 004 Four\r\n\r\n## Notes\r\n\r\nOld.\r\n\r\n## Other\r\n");

        byte[] result = BacklogEditor.AppendNote(EditableText.Parse(bytes)!, NoteDate, "n").ToBytes();

        Assert.Equal(
            Encoding.UTF8.GetBytes(
                "\uFEFF# 004 Four\r\n\r\n## Notes\r\n\r\nOld.\r\n\r\n- 2026-03-02: n\r\n\r\n## Other\r\n"),
            result);
    }

    [Fact]
    public void AppendNote_WithoutASectionAddsOneAfterExactlyOneBlankLine()
    {
        Assert.Equal(
            "# 004 Four\n\nText.\n\n## Notes\n\n- 2026-03-02: n\n",
            BacklogEditor.AppendNote(Text("# 004 Four\n\nText.\n"), NoteDate, "n").Text);
        Assert.Equal(
            "# 004 Four\n\nText.\n\n## Notes\n\n- 2026-03-02: n\n",
            BacklogEditor.AppendNote(Text("# 004 Four\n\nText.\n\n\n"), NoteDate, "n").Text);
        Assert.Equal(
            "# 004 Four\r\n\r\n## Notes\r\n\r\n- 2026-03-02: n\r\n",
            BacklogEditor.AppendNote(Text("# 004 Four\r\n"), NoteDate, "n").Text);
    }

    [Fact]
    public void AppendNote_FileWithoutTrailingLineBreakEndsWithOne()
    {
        Assert.Equal(
            "# 004 Four\n\nText.\n\n## Notes\n\n- 2026-03-02: n\n",
            BacklogEditor.AppendNote(Text("# 004 Four\n\nText."), NoteDate, "n").Text);
        Assert.Equal(
            "## Notes\n\n- 2026-01-01: a\n- 2026-03-02: n\n",
            BacklogEditor.AppendNote(Text("## Notes\n\n- 2026-01-01: a"), NoteDate, "n").Text);
        Assert.Equal(
            "## Notes\r\n\r\nOld.\r\n\r\n- 2026-03-02: n\r\n",
            BacklogEditor.AppendNote(Text("## Notes\r\n\r\nOld."), NoteDate, "n").Text);
    }

    [Fact]
    public void AppendNote_InTheMiddleKeepsAMissingFinalLineBreak()
    {
        Assert.Equal(
            "## Notes\n\nOld.\n\n- 2026-03-02: n\n\n## Other",
            BacklogEditor.AppendNote(Text("## Notes\n\nOld.\n\n## Other"), NoteDate, "n").Text);
    }

    [Theory]
    [InlineData("  Use\tthe\n\nblue   one. ", "Use the blue one.")]
    [InlineData("a\r\nb", "a b")]
    [InlineData("Emoji \U0001F600 ok", "Emoji \U0001F600 ok")]
    [InlineData("<script>alert(1)</script>", "<script>alert(1)</script>")]
    public void CleanNote_JoinsWhitespaceIntoOneLine(string note, string clean)
    {
        Assert.Equal(clean, BacklogEditor.CleanNote(note));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \n\t ")]
    [InlineData("bell \u0007 here")]
    [InlineData("del \u007F here")]
    [InlineData("Proposed by job 12-x")]
    [InlineData("see: proposed BY job 12-x")]
    [InlineData("Proposed\n by \t job 12-x")]
    public void CleanNote_RefusesEmptyControlInvalidAndProposedByJob(string? note)
    {
        Assert.Null(BacklogEditor.CleanNote(note));
    }

    [Fact]
    public void CleanNote_RefusesALoneSurrogate()
    {
        Assert.Null(BacklogEditor.CleanNote("lone " + (char)0xD800 + " surrogate"));
    }

    [Fact]
    public void CleanNote_AllowsAtMost1000Runes()
    {
        string emoji = "\U0001F600";
        Assert.Equal(1000, BacklogEditor.CleanNote(string.Concat(Enumerable.Repeat(emoji, 1000)))!.EnumerateRunes().Count());
        Assert.Null(BacklogEditor.CleanNote(string.Concat(Enumerable.Repeat(emoji, 1001))));
        Assert.NotNull(BacklogEditor.CleanNote(new string('a', 1000)));
        Assert.Null(BacklogEditor.CleanNote(new string('a', 1001)));
        Assert.NotNull(BacklogEditor.CleanNote("  " + new string('a', 1000) + "\n"));
    }

    // AC-2: the title of a capture. 60 runes stay whole; longer text is cut at the last space at or before rune 60
    // (a hard cut at 60 when there is none); trailing .,;:!? and spaces go.
    [Theory]
    [InlineData("Short idea", "Short idea")]
    [InlineData("Short idea.", "Short idea")]
    [InlineData("What about this?!", "What about this")]
    [InlineData("Trailing mix ,;: . ", "Trailing mix")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
        "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaab",
        "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("Show the jobs board on the phone with a compact layout and big buttons",
        "Show the jobs board on the phone with a compact layout and")]
    [InlineData("Show the jobs board on the phone with a compact layout, then more",
        "Show the jobs board on the phone with a compact layout")]
    [InlineData("!!!", "")]
    public void CaptureTitle_CutsAtTheLastSpaceAndDropsTrailingPunctuation(string clean, string title)
    {
        Assert.Equal(title, BacklogEditor.CaptureTitle(clean));
        Assert.True(BacklogEditor.CaptureTitle(clean).EnumerateRunes().Count() <= 60);
    }

    [Fact]
    public void CaptureTitle_CountsRunesNotChars()
    {
        string emoji = "\U0001F600";
        string text = string.Concat(Enumerable.Repeat(emoji, 61));

        Assert.Equal(string.Concat(Enumerable.Repeat(emoji, 60)), BacklogEditor.CaptureTitle(text));
    }

    // The arrow and "(after:" are refused by CleanTitle only when they land inside the cut title.
    [Theory]
    [InlineData("Move it → later", false)]
    [InlineData("Wait (after: #3) for it", false)]
    [InlineData("A long enough idea that pushes the arrow past rune sixty here → x", true)]
    public void CaptureTitle_PassesCleanTitleOnlyWithoutArrowOrAfterInTheTitle(string text, bool accepted)
    {
        string? clean = BacklogEditor.CleanCapture(text);
        Assert.NotNull(clean);
        Assert.Equal(accepted, BacklogEditor.CleanTitle(BacklogEditor.CaptureTitle(clean)) is not null);
    }

    [Theory]
    [InlineData("  Use the\n\nblue\tone. ", "Use the blue one.")]
    [InlineData("one", "one")]
    public void CleanCapture_JoinsWhitespaceIntoOneLine(string text, string clean)
    {
        Assert.Equal(clean, BacklogEditor.CleanCapture(text));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \n\t ")]
    [InlineData("bell \u0007 here")]
    [InlineData("del \u007F here")]
    [InlineData("Proposed by job 12-x")]
    [InlineData("see: proposed BY\n job 12-x")]
    public void CleanCapture_RefusesEmptyControlAndProposedByJob(string? text)
    {
        Assert.Null(BacklogEditor.CleanCapture(text));
    }

    [Fact]
    public void CleanCapture_RefusesALoneSurrogateAndAllowsAtMost2000Runes()
    {
        string emoji = "\U0001F600";
        Assert.Null(BacklogEditor.CleanCapture("lone " + (char)0xD800 + " surrogate"));
        Assert.NotNull(BacklogEditor.CleanCapture(string.Concat(Enumerable.Repeat(emoji, 2000))));
        Assert.Null(BacklogEditor.CleanCapture(string.Concat(Enumerable.Repeat(emoji, 2001))));
        Assert.NotNull(BacklogEditor.CleanCapture(" " + new string('a', 2000) + "\n"));
        Assert.Null(BacklogEditor.CleanCapture(new string('a', 2001)));
    }

    [Fact]
    public void RenderDraft_IsTheFixedDraftTemplate()
    {
        Assert.Equal(
            "# 007 Idea\n\n## What\n\nIdea. With more.\n\n## Why\n\nStill to be written: this is a quick-capture draft.\n\n"
            + "## Notes\n\n- 2026-03-02: Draft from ControlRoom\n",
            BacklogEditor.RenderDraft(7, "Idea", "Idea. With more.", new DateOnly(2026, 3, 2)));
    }

    [Fact]
    public void Add_WithTheQuestionMarkWritesADraftLine()
    {
        BacklogEdit edit = BacklogEditor.Add(Text(Sample), 12, "Draft", AddPosition.Bottom, "?");

        Assert.Equal("- [?] [#12](backlog/012-draft.md) Draft", edit.Text.Lines[9]);
        Assert.Equal("#12", edit.Subject);
    }

    private static EditableText Text(string text)
    {
        return EditableText.Parse(Encoding.UTF8.GetBytes(text))!;
    }
}
