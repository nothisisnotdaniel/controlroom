using System.Text.RegularExpressions;

namespace ControlRoom.Server.Tests;

/// <summary>Approve and Drop in the story bar of a <c>[?]</c> item (#105), checked in <c>edit.js</c>, <c>app.js</c>
/// and <c>CLAUDE.md</c> as text (there is no JS test runtime yet, see #59). Tapping the buttons is checked by hand.</summary>
public sealed partial class StoryDecisionWebAppTests
{
    [Fact]
    public async Task EditJs_StoryBarGetsApproveAndDropWithFixedTextsAndNames()
    {
        string edit = await Fetch("/edit.js");
        string buttons = Between(edit, "function storyDecisionButtons(target, number) {", "\n}\n");

        Assert.Contains("editButton(EditTexts.approve, \"edit-primary story-approve-button\", () => {", buttons,
            StringComparison.Ordinal);
        Assert.Contains("editButton(EditTexts.drop, \"edit-danger story-drop-button\", () => {", buttons,
            StringComparison.Ordinal);
        Assert.Contains("const subject = \"#\" + number;", buttons, StringComparison.Ordinal);
        Assert.Contains("approve.setAttribute(\"aria-label\", EditTexts.approve + \" \" + subject);", buttons,
            StringComparison.Ordinal);
        Assert.Contains("drop.setAttribute(\"aria-label\", EditTexts.drop + \" \" + subject);", buttons,
            StringComparison.Ordinal);
        Assert.Contains("const bar = document.querySelector(\"#story .story-bar\");", edit, StringComparison.Ordinal);
        Assert.Contains("bar.append(...storyDecisionButtons(target, number));", edit, StringComparison.Ordinal);
        Assert.DoesNotContain("innerHTML", edit, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EditJs_ButtonsShowOnlyForAnApprovalLineAndAreKeptOrRemovedOnRefresh()
    {
        string edit = await Fetch("/edit.js");
        string show = Between(edit, "async function showStoryDecision(target, data) {", "\n}\n");

        Assert.Contains("return { version: match.version, line: entry.line, group: group };", edit, StringComparison.Ordinal);
        Assert.Contains("found = await findStoryLine(target.project, number);", show, StringComparison.Ordinal);
        Assert.Contains("} catch {\n      found = null;\n    }", show, StringComparison.Ordinal);
        Assert.Contains(
            "if (found === null || found.group !== \"approval\") {\n    for (const button of shown) {\n      button.remove();\n    }\n    return;\n  }",
            show, StringComparison.Ordinal);
        Assert.Contains("if (shown.length === 0) {\n    bar.append(", show, StringComparison.Ordinal);
        Assert.Contains("if (request !== storyDecisionRequest || story !== storyRequest || currentView !== \"story\") {",
            show, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AppJs_LoadStoryAndRefreshStoryAskForTheButtons()
    {
        string app = await Fetch("/app.js");
        string load = Between(app, "async function loadStory(target) {", "\n}\n");
        string refresh = Between(app, "async function refreshStory() {", "\n}\n");

        Assert.Contains("title.focus({ preventScroll: true });\n  showStoryDecision(target, data);", load,
            StringComparison.Ordinal);
        Assert.Contains("showStoryDecision(target, data);\n  window.scrollTo(0, scroll);", refresh, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EditJs_DecisionSendsVersionAndLineFromTheTapThroughSendChange()
    {
        string edit = await Fetch("/edit.js");
        string decide = Between(edit, "async function decideStory(target, number, action) {", "\n}\n");

        Assert.Contains("found = await findStoryLine(target.project, number);", decide, StringComparison.Ordinal);
        Assert.Contains(
            "const ok = await sendChange({ project: target.project, version: found.version, line: found.line, action: action });",
            decide, StringComparison.Ordinal);
        Assert.Contains("decideStory(target, number, \"approve\");", edit, StringComparison.Ordinal);
        Assert.Contains(
            "if (found === null || found.group !== \"approval\") {\n    showEditNotice(ChangeErrors.changed, true);\n    loadStory(target);\n    return;\n  }",
            decide, StringComparison.Ordinal);
        Assert.Contains("showEditNotice(ChangeErrors.unreachable, true);", decide, StringComparison.Ordinal);
        Assert.Single(ChangeApi().Matches(edit));
        Assert.Contains("response = await authFetch(\"/api/backlog/change\", {", edit, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EditJs_DropAsksFirst()
    {
        string edit = await Fetch("/edit.js");
        string buttons = Between(edit, "function storyDecisionButtons(target, number) {", "\n}\n");

        Assert.Contains("if (confirm(EditTexts.confirmDrop)) {\n      decideStory(target, number, \"drop\");\n    }", buttons,
            StringComparison.Ordinal);
        Assert.Single(ConfirmCall().Matches(buttons));
    }

    [Fact]
    public async Task EditJs_ApproveReloadsTheStoryAndBothRefetchTheInbox()
    {
        string edit = await Fetch("/edit.js");
        string decide = Between(edit, "async function decideStory(target, number, action) {", "\n}\n");

        Assert.Contains(
            "if (!ok) {\n    return;\n  }\n  loadInbox();\n  if (action === \"approve\") {\n    loadStory(target);\n    return;\n  }\n  leaveDroppedStory();",
            decide, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EditJs_DropLeavesTheStoryBackOrToTheInboxKeepingTheNotice()
    {
        string edit = await Fetch("/edit.js");
        string app = await Fetch("/app.js");
        string leave = Between(edit, "function leaveDroppedStory() {", "\n}\n");
        string route = Between(app, "function route() {", "\n}\n");

        Assert.Contains(
            "keepEditNotice();\n  if (openedFromList) {\n    history.back();\n    return;\n  }\n  history.replaceState(null, \"\", InboxRoute);\n  route();",
            leave, StringComparison.Ordinal);
        Assert.Contains("currentView = next;\n  routeEditNotice();", route, StringComparison.Ordinal);
        Assert.DoesNotContain("hideEditNotice", route, StringComparison.Ordinal);
        Assert.Contains("if (kept !== null) {\n    showEditNotice(kept.text, kept.isError);\n  } else {\n    hideEditNotice();\n  }",
            edit, StringComparison.Ordinal);
        Assert.Contains("keptEditNotice = null;", edit, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EditJs_ButtonsAreHiddenWhileAStoryFormIsOpenAndNeverSentWhileBusy()
    {
        string edit = await Fetch("/edit.js");
        string buttons = Between(edit, "function storyDecisionButtons(target, number) {", "\n}\n");
        string decide = Between(edit, "async function decideStory(target, number, action) {", "\n}\n");

        Assert.Contains(
            "\"#story .edit-story-button, #story .add-note-button, #story .story-approve-button, #story .story-drop-button\")",
            edit, StringComparison.Ordinal);
        Assert.Contains("const hidden = storyFormOpen();\n  approve.hidden = hidden;\n  drop.hidden = hidden;", buttons,
            StringComparison.Ordinal);
        Assert.Contains("if (changeBusy || storyFormOpen()) {\n    return;\n  }", decide, StringComparison.Ordinal);
        Assert.Contains("if (changeBusy) {\n    return false;\n  }", edit, StringComparison.Ordinal);
    }

    [Fact]
    public void ClaudeMd_DescribesTheStoryDecisionButtons()
    {
        string text = File.ReadAllText(Path.Combine(RepoFolder(), "CLAUDE.md"));

        Assert.Contains("Approve and Drop in the story bar of a `[?]` item only (`story-approve-button`", text,
            StringComparison.Ordinal);
        Assert.Contains("(`routeEditNotice`)", text, StringComparison.Ordinal);
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

    [GeneratedRegex(@"\bconfirm\(")]
    private static partial Regex ConfirmCall();

    [GeneratedRegex("\"/api/backlog/change\"")]
    private static partial Regex ChangeApi();
}
