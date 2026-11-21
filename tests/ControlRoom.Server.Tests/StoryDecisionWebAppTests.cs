using System.Text.RegularExpressions;

namespace ControlRoom.Server.Tests;

/// <summary>Approve and Drop in the story bar of a <c>[?]</c> item (#105; both leave the story since #128), checked in <c>edit.js</c>, <c>app.js</c>
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

        Assert.Contains("return { version: match.version, line: entry.line, group: group, mark: asText(entry.mark) };", edit,
            StringComparison.Ordinal);
        Assert.Contains("entry = await findStoryEntry(target.project, number);", show, StringComparison.Ordinal);
        Assert.Contains("} catch {\n      entry = null;\n    }", show, StringComparison.Ordinal);
        Assert.Contains("const found = notDone(entry);", show, StringComparison.Ordinal);
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
    public async Task EditJs_ApproveAndDropBothRefetchTheInboxAndLeaveTheStory()
    {
        string edit = await Fetch("/edit.js");
        string decide = Between(edit, "async function decideStory(target, number, action) {", "\n}\n");

        Assert.Contains("if (!ok) {\n    return;\n  }\n  loadInbox();\n  leaveDecidedStory();", decide,
            StringComparison.Ordinal);
        Assert.DoesNotContain("action === \"approve\"", decide, StringComparison.Ordinal);
        Assert.DoesNotContain("leaveDroppedStory", edit, StringComparison.Ordinal);
        string afterOk = decide[decide.IndexOf("if (!ok) {", StringComparison.Ordinal)..];
        Assert.DoesNotContain("loadStory(target);", afterOk, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EditJs_AFailedDecisionStaysOnTheStory()
    {
        string edit = await Fetch("/edit.js");
        string decide = Between(edit, "async function decideStory(target, number, action) {", "\n}\n");

        int failed = decide.IndexOf("if (!ok) {\n    return;\n  }", StringComparison.Ordinal);
        Assert.True(failed >= 0);
        Assert.True(failed < decide.IndexOf("loadInbox();", StringComparison.Ordinal));
        Assert.True(failed < decide.IndexOf("leaveDecidedStory();", StringComparison.Ordinal));
        Assert.Single(LeaveCall().Matches(decide));
        Assert.Contains("} catch {\n    if (!tokenViewShown()) {\n      showEditNotice(ChangeErrors.unreachable, true);\n    }\n    return;\n  }",
            decide, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InboxAndBacklogOpenTheStoryFromAList()
    {
        string app = await Fetch("/app.js");
        string inbox = await Fetch("/inbox.js");
        string open = Between(app, "function openStory(project, number) {", "\n}\n");

        Assert.Contains("openedFromList = true;\n  location.hash = storyHash(project, number);", open,
            StringComparison.Ordinal);
        Assert.Contains("event.stopPropagation();\n      openStory(item.project, item.number);", inbox,
            StringComparison.Ordinal);
        Assert.Contains("return;\n      }\n      openStory(item.project, item.number);", inbox, StringComparison.Ordinal);
        Assert.Contains(
            "if (event.target instanceof Element && event.target.closest(\"a\")) {\n      openedFromList = true;\n      return;\n    }\n    openStory(projectName, number);",
            app, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EditJs_ApproveAndDropLeaveTheStoryBackOrToTheInboxKeepingTheNotice()
    {
        string edit = await Fetch("/edit.js");
        string app = await Fetch("/app.js");
        string leave = Between(edit, "function leaveDecidedStory() {", "\n}\n");
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
            "\"#story .edit-story-button, #story .add-note-button, #story .story-category, #story .story-approve-button, \"\n    + \"#story .story-drop-button\")",
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
        Assert.Contains("after either succeeds the view goes `history.back()` or to `#inbox` (#128, `leaveDecidedStory`)", text,
            StringComparison.Ordinal);
        Assert.DoesNotContain("Approve reloads the story", text, StringComparison.Ordinal);
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

    [GeneratedRegex(@"\bleaveDecidedStory\(\)")]
    private static partial Regex LeaveCall();

    [GeneratedRegex("\"/api/backlog/change\"")]
    private static partial Regex ChangeApi();
}
