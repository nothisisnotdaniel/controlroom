using System.Text.RegularExpressions;

namespace ControlRoom.Server.Tests;

/// <summary>Done in the story bar of a <c>[h]</c> item (#134), checked in <c>edit.js</c> and <c>CLAUDE.md</c> as text
/// (there is no JS test runtime yet, see #59). The server side of the <c>done</c> change is covered by
/// <c>BacklogChangeApiTests.Done_*</c> and <c>BacklogEditorTests.Done_*</c>. Tapping the button is checked by hand.</summary>
public sealed partial class StoryDoneWebAppTests
{
    [Fact]
    public async Task EditJs_DoneButtonHasAFixedTextAndANameWithTheNumber()
    {
        string edit = await Fetch("/edit.js");
        string button = Between(edit, "function storyDoneButton(target, number) {", "\n}\n");

        Assert.Contains("markDone: \"Done\",", edit, StringComparison.Ordinal);
        Assert.Contains("editButton(EditTexts.markDone, \"edit-primary story-done-button\", () => {\n    doneStory(target, number);\n  });",
            button, StringComparison.Ordinal);
        Assert.Contains("done.setAttribute(\"aria-label\", EditTexts.markDone + \" #\" + number);", button,
            StringComparison.Ordinal);
        Assert.DoesNotContain("confirm(", button, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EditJs_DoneShowsOnlyForAHandLineAndIsKeptOrRemovedOnRefresh()
    {
        string edit = await Fetch("/edit.js");
        string show = Between(edit, "async function showStoryDecision(target, data) {", "\n}\n");
        string done = Between(edit, "function showStoryDone(bar, target, number, found) {", "\n}\n");

        Assert.Contains("showStoryCategory(bar, target, data, found);\n  showStoryDone(bar, target, number, found);", show,
            StringComparison.Ordinal);
        Assert.Contains(
            "if (found === null || found.group !== \"user\") {\n    if (old !== null) {\n      old.remove();\n    }\n    return;\n  }",
            done, StringComparison.Ordinal);
        Assert.Contains("if (old === null) {\n    bar.appendChild(storyDoneButton(target, number));\n  }", done,
            StringComparison.Ordinal);
        Assert.Contains("const found = notDone(entry);", show, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EditJs_DoneComesAfterTheCategorySelect()
    {
        string edit = await Fetch("/edit.js");
        string category = Between(edit, "function showStoryCategory(bar, target, data, found) {", "\n}\n");

        Assert.Contains("const decision = bar.querySelector(\".story-approve-button, .story-done-button\");\n    bar.insertBefore(next, decision);",
            category, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EditJs_DoneSendsTheDoneChangeWithVersionAndLineFromTheTap()
    {
        string edit = await Fetch("/edit.js");
        string send = Between(edit, "async function doneStory(target, number) {", "\n}\n");

        Assert.Contains("found = await findStoryLine(target.project, number);", send, StringComparison.Ordinal);
        Assert.Contains(
            "const ok = await sendChange({ project: target.project, version: found.version, line: found.line, action: \"done\" });",
            send, StringComparison.Ordinal);
        Assert.Single(SendChangeCall().Matches(send));
        Assert.Contains(
            "if (found === null || found.group !== \"user\") {\n    showEditNotice(ChangeErrors.changed, true);\n    loadStory(target);\n    return;\n  }",
            send, StringComparison.Ordinal);
        Assert.Contains("} catch {\n    if (!tokenViewShown()) {\n      showEditNotice(ChangeErrors.unreachable, true);\n    }\n    return;\n  }",
            send, StringComparison.Ordinal);
        Assert.Contains("if (changeBusy || storyFormOpen()) {\n    return;\n  }", send, StringComparison.Ordinal);
        Assert.DoesNotContain("authFetch", send, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EditJs_ASuccessfulDoneLeavesTheStoryAndAFailedOneStays()
    {
        string edit = await Fetch("/edit.js");
        string send = Between(edit, "async function doneStory(target, number) {", "\n}\n");

        Assert.Contains("if (!ok) {\n    return;\n  }\n  leaveDecidedStory();", send, StringComparison.Ordinal);
        Assert.Single(LeaveCall().Matches(send));
        string afterOk = send[send.IndexOf("if (!ok) {", StringComparison.Ordinal)..];
        Assert.DoesNotContain("loadStory(target);", afterOk, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EditJs_DoneIsHiddenWhileAStoryFormIsOpen()
    {
        string edit = await Fetch("/edit.js");
        string button = Between(edit, "function storyDoneButton(target, number) {", "\n}\n");
        string buttons = Between(edit, "function showStoryButtons(visible) {", "\n}\n");

        Assert.Contains("#story .story-done-button", buttons, StringComparison.Ordinal);
        Assert.Contains("done.hidden = storyFormOpen();", button, StringComparison.Ordinal);
    }

    [Fact]
    public void ClaudeMd_DescribesTheDoneButton()
    {
        string text = File.ReadAllText(Path.Combine(RepoFolder(), "CLAUDE.md"));

        Assert.Contains("Done in the story bar of a `[h]` item only (#134, `story-done-button`", text,
            StringComparison.Ordinal);
    }

    private static string Between(string text, string start, string end)
    {
        int from = text.IndexOf(start, StringComparison.Ordinal);
        Assert.True(from >= 0, start);
        int to = text.IndexOf(end, from + start.Length, StringComparison.Ordinal);
        Assert.True(to > from, end);
        return text[from..to];
    }

    private static string RepoFolder()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "ControlRoom.slnx")))
        {
            current = current.Parent;
        }

        Assert.NotNull(current);
        return current.FullName;
    }

    private static async Task<string> Fetch(string path)
    {
        using TempFactoryRoot root = new();
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateClient();
        return await client.GetStringAsync(path);
    }

    [GeneratedRegex(@"\bleaveDecidedStory\(\)")]
    private static partial Regex LeaveCall();

    [GeneratedRegex(@"\bsendChange\(")]
    private static partial Regex SendChangeCall();
}
