using System.Text;

namespace ControlRoom.Server.Tests;

/// <summary><see cref="StoryCategory"/>: the parse rule of <c>job.py with_category</c> and the exact bytes of
/// <see cref="StoryCategory.Set"/>.</summary>
public sealed class StoryCategoryTests
{
    [Fact]
    public void Names_AreTheNineOfJobPyInOrder()
    {
        Assert.Equal(
            ["feature", "bug", "security", "ux", "ui", "tidy-up", "test", "ci", "docs"], StoryCategory.Names);
    }

    [Theory]
    [InlineData("feature")]
    [InlineData("bug")]
    [InlineData("security")]
    [InlineData("ux")]
    [InlineData("ui")]
    [InlineData("tidy-up")]
    [InlineData("test")]
    [InlineData("ci")]
    [InlineData("docs")]
    public void Parse_EachOfTheNineNames(string name)
    {
        Assert.Equal(name, StoryCategory.Parse("# 001 T\n\nCategory: " + name + "\n\n## What\n\nx\n"));
    }

    [Theory]
    [InlineData("# T\n\nCategory: bug \n")]
    [InlineData("# T\n\nCategory: Feature\n")]
    [InlineData("# T\n\nCategory: bugfix\n")]
    [InlineData("# T\n\nCategory: FEATURE\n")]
    [InlineData("# T\n\nCategory:\n")]
    [InlineData("# T\n\n## What\n\nCategory: bug\n")]
    [InlineData("# T\n\n category: bug\n")]
    [InlineData("# T\n\n Category: bug\n")]
    [InlineData("# T\n\nText.\n")]
    [InlineData("")]
    public void Parse_OtherwiseIsNull(string text)
    {
        Assert.Null(StoryCategory.Parse(text));
    }

    [Fact]
    public void Parse_NullTextIsNull()
    {
        Assert.Null(StoryCategory.Parse(null));
    }

    [Fact]
    public void Parse_TheFirstCountingLineWins_EvenWhenItIsInvalid()
    {
        Assert.Equal("bug", StoryCategory.Parse("# T\nCategory: bug\nCategory: docs\n"));
        Assert.Null(StoryCategory.Parse("# T\nCategory: nope\nCategory: docs\n"));
    }

    [Fact]
    public void Parse_TabsAndNoSpace_Crlf_AndABom()
    {
        Assert.Equal("ux", StoryCategory.Parse("# T\nCategory:\t ux\n"));
        Assert.Equal("ux", StoryCategory.Parse("# T\nCategory:ux\n"));
        Assert.Equal("security", StoryCategory.Parse("# T\r\n\r\nCategory: security\r\n\r\n## What\r\n"));
        Assert.Equal("ci", StoryCategory.Parse("﻿Category: ci\n# T\n"));
        Assert.Equal("ci", StoryCategory.Parse("# T\nCategory: ci"));
    }

    [Fact]
    public void Set_ReplacesTheFirstCountingLine_ValidOrNot()
    {
        Assert.Equal(
            "# T\n\nCategory: bug\n\nCategory: docs\n\n## What\n",
            SetText("# T\n\nCategory: Nope \n\nCategory: docs\n\n## What\n", "bug"));
    }

    [Fact]
    public void Set_InsertsBeforeTheFirstHeadingWithOneBlankLineEachSide()
    {
        Assert.Equal(
            "# 001 T\n\nCategory: feature\n\n## What\n\nx\n\n## Why\n",
            SetText("# 001 T\n\n\n\n## What\n\nx\n\n## Why\n", "feature"));
        Assert.Equal("# T\n\nCategory: ui\n\n## What\n", SetText("# T\n## What\n", "ui"));
    }

    [Fact]
    public void Set_IgnoresACategoryLineAfterTheHeading()
    {
        Assert.Equal(
            "# T\n\nCategory: test\n\n## What\n\nCategory: bug\n",
            SetText("# T\n\n## What\n\nCategory: bug\n", "test"));
    }

    [Fact]
    public void Set_AppendsWithoutAHeading()
    {
        Assert.Equal("# T\n\nText.\n\nCategory: docs\n", SetText("# T\n\nText.\n\n\n", "docs"));
        Assert.Equal("# T\n\nCategory: docs\n", SetText("# T", "docs"));
    }

    [Fact]
    public void Set_KeepsCrlfAndTheBom()
    {
        byte[] before = Encoding.UTF8.GetBytes("﻿# T\r\n\r\n## What\r\n\r\nx\r\n");
        EditableText story = EditableText.Parse(before)!;

        byte[] after = StoryCategory.Set(story, "bug").ToBytes();

        Assert.Equal(Encoding.UTF8.GetBytes("﻿# T\r\n\r\nCategory: bug\r\n\r\n## What\r\n\r\nx\r\n"), after);
        byte[] replaced = StoryCategory.Set(EditableText.Parse(after)!, "ci").ToBytes();
        Assert.Equal(Encoding.UTF8.GetBytes("﻿# T\r\n\r\nCategory: ci\r\n\r\n## What\r\n\r\nx\r\n"), replaced);
    }

    [Fact]
    public void Set_ThenParse_GivesTheName()
    {
        foreach (string name in StoryCategory.Names)
        {
            Assert.Equal(name, StoryCategory.Parse(SetText("# T\n\n## What\n", name)));
        }
    }

    private static string SetText(string text, string name)
    {
        return StoryCategory.Set(EditableText.Parse(Encoding.UTF8.GetBytes(text))!, name).Text;
    }
}
