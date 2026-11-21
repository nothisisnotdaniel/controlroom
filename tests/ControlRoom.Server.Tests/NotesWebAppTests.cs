namespace ControlRoom.Server.Tests;

/// <summary>Add note in <c>edit.js</c> and <c>app.js</c>, and its rules in <c>CLAUDE.md</c>, checked as text (there is
/// no JS test runtime): fixed texts, the class names, the request fields, text-only rendering and the
/// <c>refreshStory</c> guard. Opening, saving and the live refresh are checked by hand.</summary>
public sealed class NotesWebAppTests
{
    [Fact]
    public async Task EditJs_NoteTextsAreFixedStringsInEditTexts()
    {
        string edit = await Fetch("/edit.js");

        Assert.Contains("  addNote: \"Add note\",\n", edit, StringComparison.Ordinal);
        Assert.Contains("  noteLabel: \"Note\",\n", edit, StringComparison.Ordinal);
        Assert.Contains("  noteHint: \"Saved as one line under Notes, with today's date.\",\n", edit,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task EditJs_AddNoteOpensAFormWithALabelledTextareaAndSendsTheNoteAction()
    {
        string edit = await Fetch("/edit.js");
        string note = Between(edit, "function storyNoteButton(target, data, reload) {", "\n}\n");

        Assert.Contains("editButton(EditTexts.addNote, \"add-note-button\",", note, StringComparison.Ordinal);
        Assert.Contains("element(\"form\", \"edit-form add-note-form\")", note, StringComparison.Ordinal);
        Assert.Contains("element(\"textarea\", \"edit-input add-note-text\")", note, StringComparison.Ordinal);
        Assert.Contains("area.setAttribute(\"maxlength\", \"1000\");", note, StringComparison.Ordinal);
        Assert.Contains("labelled(EditTexts.noteLabel, area)", note, StringComparison.Ordinal);
        Assert.Contains("found = await findStoryLine(target.project, number);", note, StringComparison.Ordinal);
        Assert.Contains("const ok = await sendChange({", note, StringComparison.Ordinal);
        Assert.Contains("action: \"note\",", note, StringComparison.Ordinal);
        Assert.Contains("version: found.version,", note, StringComparison.Ordinal);
        Assert.Contains("line: found.line,", note, StringComparison.Ordinal);
        Assert.Contains("storyVersion: storyVersion,", note, StringComparison.Ordinal);
        Assert.Contains("note: area.value,", note, StringComparison.Ordinal);
        Assert.Contains("if (ok) {\n        reload();", note, StringComparison.Ordinal);
        Assert.Contains("editButton(EditTexts.cancel, null, () => {\n      form.replaceWith(shown);", note,
            StringComparison.Ordinal);
        Assert.Contains("showEditNotice(ChangeErrors.notEditable, true);", note, StringComparison.Ordinal);
        Assert.Contains("showEditNotice(ChangeErrors.unreachable, true);", note, StringComparison.Ordinal);
        Assert.Contains("save.disabled = true;", note, StringComparison.Ordinal);
        Assert.Contains("const blank = () => area.value.trim() === \"\";", note, StringComparison.Ordinal);
        Assert.Contains("save.disabled = blank();", note, StringComparison.Ordinal);
        Assert.Contains("if (changeBusy || blank()) {", note, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EditJs_NeverUsesInnerHtml()
    {
        string edit = await Fetch("/edit.js");

        Assert.DoesNotContain("innerHTML", edit, StringComparison.Ordinal);
        Assert.DoesNotContain("insertAdjacentHTML", edit, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EditJs_EditStoryAndAddNoteAreNeverOpenAtOnce()
    {
        string edit = await Fetch("/edit.js");
        string storyEdit = Between(edit, "function storyEditButton(target, data, reload) {", "\n}\n");
        string note = Between(edit, "function storyNoteButton(target, data, reload) {", "\n}\n");

        Assert.Contains(
            "document.querySelector(\"#story .edit-story-form, #story .add-note-form\") !== null", edit,
            StringComparison.Ordinal);
        Assert.Contains(
            "\"#story .edit-story-button, #story .add-note-button, #story .story-category, #story .story-approve-button, \"\n    + \"#story .story-drop-button, #story .story-done-button\")", edit,
            StringComparison.Ordinal);
        foreach (string handler in new[] { storyEdit, note })
        {
            Assert.Contains("if (!shown || storyFormOpen()) {", handler, StringComparison.Ordinal);
            Assert.Contains("showStoryButtons(false);", handler, StringComparison.Ordinal);
            Assert.Contains("showStoryButtons(true);", handler, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task AppJs_StoryBarHasBothButtonsAndRefreshStoryWaitsWhileANoteIsOpen()
    {
        string app = await Fetch("/app.js");
        string refresh = Between(app, "async function refreshStory() {", "\n}\n");

        Assert.Contains(
            "showStoryContent([article], [\n    storyEditButton(target, data, () => loadStory(target)),\n"
            + "    storyNoteButton(target, data, () => loadStory(target)),\n  ]);",
            app, StringComparison.Ordinal);
        Assert.Contains("document.querySelector(\"#story .add-note-form\") !== null", refresh, StringComparison.Ordinal);
        Assert.Contains("if (editing()) {\n    return false;\n  }", refresh, StringComparison.Ordinal);
        Assert.Contains("[\".add-note-button\", storyNoteButton]", refresh, StringComparison.Ordinal);
        Assert.Contains("[\".edit-story-button\", storyEditButton]", refresh, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AppCss_NoteTextareaDoesNotZoomOnTheIphone()
    {
        string css = await Fetch("/app.css");
        string input = Between(css, ".edit-input {", "}");

        Assert.Contains("font-size: 1rem;", input, StringComparison.Ordinal);
        Assert.Contains(".edit-hint {", css, StringComparison.Ordinal);
    }

    [Fact]
    public void ClaudeMd_DescribesTheNoteAction()
    {
        string text = File.ReadAllText(Path.Combine(RepoFolder(), "CLAUDE.md"));

        Assert.Contains("and `note` (on a numbered open", text, StringComparison.Ordinal);
        Assert.Contains("at most 1000", text, StringComparison.Ordinal);
        Assert.Contains("`- YYYY-MM-DD: <note>`", text, StringComparison.Ordinal);
        Assert.Contains("## Notes", text, StringComparison.Ordinal);
        Assert.Contains("only the story file is committed", text, StringComparison.Ordinal);
        Assert.Contains("`backlog: note on #N`", text, StringComparison.Ordinal);
        Assert.Contains("Add note (`add-note-button`", text, StringComparison.Ordinal);
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
}
