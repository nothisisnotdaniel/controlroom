namespace ControlRoom.Server.Tests;

public sealed class StoryMarkdownTests
{
    [Fact]
    public void Parse_GivesHeadingsParagraphsListsAndCode()
    {
        string markdown =
            "# Title\n"
            + "\n"
            + "## Why\n"
            + "First line\n"
            + "second line.\n"
            + "\n"
            + "### Steps\n"
            + "- one\n"
            + "* two\n"
            + "  continued\n"
            + "\n"
            + "1. first\n"
            + "2) second\n"
            + "\n"
            + "```bash\n"
            + "  <b>keep</b> **as is**\n"
            + "\n"
            + "echo `x`\n"
            + "```\n"
            + "#### Deep\n"
            + "---\n"
            + "After rule\n";

        IReadOnlyList<StoryBlock> blocks = StoryMarkdown.Parse(markdown);

        Assert.Equal(
            ["heading", "heading", "paragraph", "heading", "list", "list", "code", "heading", "paragraph"],
            blocks.Select(block => block.Kind));
        Assert.Equal(1, blocks[0].Level);
        Assert.Equal([new StoryInline("text", "Title")], blocks[0].Inlines);
        Assert.Equal(2, blocks[1].Level);
        Assert.Equal([new StoryInline("text", "First line second line.")], blocks[2].Inlines);
        Assert.Equal(3, blocks[3].Level);
        Assert.False(blocks[4].Ordered);
        Assert.Equal(["one", "two continued"], blocks[4].Items!.Select(Plain));
        Assert.True(blocks[5].Ordered);
        Assert.Equal(["first", "second"], blocks[5].Items!.Select(Plain));
        Assert.Equal("  <b>keep</b> **as is**\n\necho `x`", blocks[6].Text);
        Assert.Equal(3, blocks[7].Level);
        Assert.Equal([new StoryInline("text", "After rule")], blocks[8].Inlines);
    }

    [Fact]
    public void Parse_UnclosedFenceRunsToTheEnd()
    {
        IReadOnlyList<StoryBlock> blocks = StoryMarkdown.Parse("text\n~~~\ncode\r\nmore");

        Assert.Equal(["paragraph", "code"], blocks.Select(block => block.Kind));
        Assert.Equal("code\nmore", blocks[1].Text);
    }

    [Fact]
    public void ParseInlines_GivesTextCodeStrongAndEm()
    {
        List<StoryInline> inlines = StoryMarkdown.ParseInlines(
            "Run `dotnet test` with **all tests** and *care* or _this_ and __that__.");

        Assert.Equal(
            [
                new StoryInline("text", "Run "),
                new StoryInline("code", "dotnet test"),
                new StoryInline("text", " with "),
                new StoryInline("strong", "all tests"),
                new StoryInline("text", " and "),
                new StoryInline("em", "care"),
                new StoryInline("text", " or "),
                new StoryInline("em", "this"),
                new StoryInline("text", " and "),
                new StoryInline("strong", "that"),
                new StoryInline("text", "."),
            ],
            inlines);
    }

    [Theory]
    [InlineData("See [the docs](https://example.com/x) now", "See the docs now")]
    [InlineData("[`code` link](javascript:alert(1))", "code link")]
    [InlineData("![alt text](img.png)", "alt text")]
    [InlineData("snake_case_name stays", "snake_case_name stays")]
    [InlineData("2 * 3 * 4", "2 * 3 * 4")]
    [InlineData("an ** empty", "an ** empty")]
    [InlineData("escaped \\*star\\* and \\`tick\\`", "escaped *star* and `tick`")]
    [InlineData("unclosed `tick and [link", "unclosed `tick and [link")]
    public void ParseInlines_LinksKeepOnlyTheirTextAndLiteralsStay(string text, string plain)
    {
        List<StoryInline> inlines = StoryMarkdown.ParseInlines(text);

        Assert.Equal(plain, Plain(inlines));
        Assert.DoesNotContain(inlines, inline => inline.Text.Contains("](", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("<img src=x onerror=alert(1)>")]
    [InlineData("<b>bold</b>")]
    [InlineData("<a href=\"javascript:x\">x</a>")]
    public void Parse_RawHtmlStaysLiteralText(string html)
    {
        IReadOnlyList<StoryBlock> blocks = StoryMarkdown.Parse("Before " + html + " after\n\n" + html + "\n");

        Assert.Equal(["paragraph", "paragraph"], blocks.Select(block => block.Kind));
        Assert.All(blocks, block => Assert.All(block.Inlines!, inline => Assert.Equal("text", inline.Kind)));
        Assert.Equal("Before " + html + " after", Plain(blocks[0].Inlines!));
        Assert.Equal(html, Plain(blocks[1].Inlines!));
    }

    [Fact]
    public void Parse_OutputHoldsNoMarkupOfItsOwn()
    {
        string markdown = "# Head **x**\n\nA *b* `c` [d](e)\n\n- f\n\n```\ng\n```\n";

        IReadOnlyList<StoryBlock> blocks = StoryMarkdown.Parse(markdown);
        string json = System.Text.Json.JsonSerializer.Serialize(blocks);

        Assert.DoesNotContain("<", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\\u003C", json, StringComparison.Ordinal);
        Assert.DoesNotContain("**", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_EmptyTextGivesNoBlocks()
    {
        Assert.Empty(StoryMarkdown.Parse(""));
        Assert.Empty(StoryMarkdown.Parse("\n\n  \n"));
    }

    private static string Plain(IEnumerable<StoryInline> inlines)
    {
        return string.Concat(inlines.Select(inline => inline.Text));
    }
}
