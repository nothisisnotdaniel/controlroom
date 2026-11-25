namespace ControlRoom.Server.Tests;

/// <summary>Checks the factory backlog section (#147) in <c>app.js</c> and <c>edit.js</c> as text (there is no JS
/// test runtime): the fixed reason texts, chosen only for the <c>factory</c> entry and its two known reasons, the
/// section without status and release lines, and the change error text. The rendered page is checked by hand.</summary>
public sealed class FactoryBacklogWebAppTests
{
    [Fact]
    public async Task AppJs_ReasonTextsAreFixedStrings()
    {
        string app = await Fetch("/app.js");

        Assert.Contains(
            "  noWorktree: \"The factory's develop worktree is missing. Run any job.py backlog command once to create it.\",\n",
            app, StringComparison.Ordinal);
        Assert.Contains(
            "  notDevelop: \"The factory's develop folder is not its develop worktree. Move it away or set "
            + "FACTORY_DEVELOP_DIR, then reload.\",\n",
            app, StringComparison.Ordinal);
        Assert.Contains("const FactoryName = \"factory\";", app, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AppJs_ReasonTextOnlyForTheFactoryAndItsTwoReasons()
    {
        string app = await Fetch("/app.js");
        string reason = Between(app, "function reasonText(project) {", "\n}\n");

        Assert.Contains("if (!project || asText(project.name) !== FactoryName) {\n    return null;\n  }", reason,
            StringComparison.Ordinal);
        Assert.Contains("if (project.reason === \"no_worktree\") {\n    return Texts.noWorktree;\n  }", reason,
            StringComparison.Ordinal);
        Assert.Contains("if (project.reason === \"not_develop\") {\n    return Texts.notDevelop;\n  }", reason,
            StringComparison.Ordinal);
        Assert.EndsWith("  return null;", reason, StringComparison.Ordinal);
        Assert.DoesNotContain("textContent = project.reason", app, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AppJs_UnreadableFactoryShowsItsReasonTextAndCanBePicked()
    {
        string app = await Fetch("/app.js");
        string render = Between(app, "function renderProject(project) {", "\nfunction show(");

        int reason = render.IndexOf(
            "const reason = reasonText(project);\n    if (reason !== null) {\n      section.appendChild(element(\"p\", \"error\", reason));\n      return section;\n    }",
            StringComparison.Ordinal);
        int unreadable = render.IndexOf("section.appendChild(element(\"p\", \"error\", Texts.unreadable));",
            StringComparison.Ordinal);
        Assert.True(reason >= 0 && unreadable > reason);
        Assert.Contains(
            ".filter((project) => (project && project.readable === true) || reasonText(project) !== null)",
            Between(app, "function readableNames(projects) {", "\n}\n"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AppJs_FactorySectionHasNoStatusOrReleaseLine()
    {
        string app = await Fetch("/app.js");
        string render = Between(app, "function renderProject(project) {", "\nfunction show(");

        Assert.Contains(
            "  if (name !== FactoryName) {\n    section.appendChild(statusLine(name));\n    section.appendChild(releaseBlock(name));\n  }\n  section.appendChild(renderPlan(name, project));",
            render, StringComparison.Ordinal);
        Assert.Equal(1, Count(render, "statusLine(name)"));
        Assert.Equal(1, Count(render, "releaseBlock(name)"));
    }

    [Fact]
    public async Task EditJs_NoWorktreeHasAFixedText()
    {
        string edit = await Fetch("/edit.js");

        Assert.Contains("  no_worktree: \"The factory's develop worktree is missing. Nothing was changed.\",\n", edit,
            StringComparison.Ordinal);
        Assert.Contains("  not_develop: ", edit, StringComparison.Ordinal);
        Assert.Contains("  push_failed: \"Committed, but the push failed.\",\n", edit, StringComparison.Ordinal);
    }

    private static int Count(string text, string part)
    {
        int count = 0;
        for (int at = text.IndexOf(part, StringComparison.Ordinal); at >= 0;
            at = text.IndexOf(part, at + part.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }

    private static string Between(string text, string start, string end)
    {
        int from = text.IndexOf(start, StringComparison.Ordinal);
        Assert.True(from >= 0, start);
        int to = text.IndexOf(end, from + start.Length, StringComparison.Ordinal);
        Assert.True(to > from, end);
        return text[from..to];
    }

    private static async Task<string> Fetch(string path)
    {
        using TempFactoryRoot root = new();
        using ServerFactory factory = new(root.Path);
        using HttpClient client = factory.CreateClient();
        return await client.GetStringAsync(path);
    }
}
