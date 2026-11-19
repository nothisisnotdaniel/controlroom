namespace ControlRoom.Server.Tests;

/// <summary>Checks the plan of each project in <c>app.js</c> and <c>app.css</c> as text (there is no JS test
/// runtime): the fixed texts, text-only rendering, the <c>data-plan</c> signature and the replace rule in
/// <c>refreshBacklogs</c>. The rendered layout and the live replacement are checked by hand.</summary>
public sealed class NextAndBlockedWebAppTests
{
    [Fact]
    public async Task AppJs_PlanTextsAreFixedStringsInTexts()
    {
        string app = await Fetch("/app.js");

        Assert.Contains("  next: \"Next:\",\n", app, StringComparison.Ordinal);
        Assert.Contains("  nothingReady: \"Nothing ready to run.\",\n", app, StringComparison.Ordinal);
        Assert.Contains(
            "  stopped: \"The factory can't pick a next item until the missing items below are fixed.\",\n", app,
            StringComparison.Ordinal);
        Assert.Contains("  spike: \"Spike\",\n", app, StringComparison.Ordinal);
        Assert.Contains("  waitsFor: \"Waits for\",\n", app, StringComparison.Ordinal);
        Assert.Contains("  missing: \"No such item\",\n", app, StringComparison.Ordinal);
        Assert.Contains("  waiting: \"Waiting\",\n", app, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AppJs_RendersThePlanBetweenTheHeadAndTheGroupsForReadableProjectsOnly()
    {
        string app = await Fetch("/app.js");
        string render = Between(app, "function renderProject(project) {", "\nfunction show(");

        int unreadable = render.IndexOf("section.appendChild(element(\"p\", \"error\", Texts.unreadable));\n    return section;",
            StringComparison.Ordinal);
        int plan = render.IndexOf("section.appendChild(renderPlan(name, project));", StringComparison.Ordinal);
        int groups = render.IndexOf("for (const group of Groups) {", StringComparison.Ordinal);
        Assert.True(unreadable >= 0 && plan > unreadable && groups > plan);
        Assert.Contains("const plan = element(\"div\", \"plan\");", app, StringComparison.Ordinal);
        Assert.Contains("const next = element(\"p\", \"next\");", app, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AppJs_NextShowsStoppedOrTheItemWithASpikeBadgeOrNothingReady()
    {
        string app = await Fetch("/app.js");
        string plan = Between(app, "function renderPlan(projectName, project) {", "\nfunction planSignature(");

        int stopped = plan.IndexOf("if (project.stopped === true) {", StringComparison.Ordinal);
        int item = plan.IndexOf("} else if (number !== null) {", StringComparison.Ordinal);
        int nothing = plan.IndexOf("next.textContent = Texts.nothingReady;", StringComparison.Ordinal);
        Assert.True(stopped >= 0 && item > stopped && nothing > item);
        Assert.Contains("next.textContent = Texts.stopped;", plan, StringComparison.Ordinal);
        Assert.Contains("next.appendChild(element(\"span\", \"next-label\", Texts.next));", plan, StringComparison.Ordinal);
        Assert.Contains("next.appendChild(planItemLink(projectName, number, item.title));", plan, StringComparison.Ordinal);
        Assert.Contains("if (item.spike === true) {\n      next.appendChild(element(\"span\", \"badge badge-spike\", Texts.spike));",
            plan, StringComparison.Ordinal);
        Assert.Contains("link.href = storyHash(projectName, number);", app, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AppJs_WaitingGroupListsBlockedItemsWithDependencyLinksAndMissingWarnings()
    {
        string app = await Fetch("/app.js");
        string plan = Between(app, "function renderPlan(projectName, project) {", "\nfunction planSignature(");
        string waits = Between(app, "function renderWaitsFor(projectName, after) {", "\n}\n");

        Assert.Contains("const group = element(\"section\", \"group waiting-group\");", plan, StringComparison.Ordinal);
        Assert.Contains("group.appendChild(element(\"h3\", null, Texts.waiting + \" (\" + blocked.length + \")\"));", plan,
            StringComparison.Ordinal);
        Assert.Contains("if (blocked.length > 0) {", plan, StringComparison.Ordinal);
        Assert.Contains("row.appendChild(planItemLink(projectName, entry.number, entry.title));", plan, StringComparison.Ordinal);
        Assert.Contains("row.appendChild(renderWaitsFor(projectName, asList(entry.after)));", plan, StringComparison.Ordinal);
        Assert.Contains("line.appendChild(element(\"span\", \"after-label\", Texts.waitsFor));", waits, StringComparison.Ordinal);
        Assert.Contains("const label = (target === projectName ? \"\" : target) + \"#\" + number;", waits,
            StringComparison.Ordinal);
        Assert.Contains(
            "if (dependency.missing === true) {\n      line.appendChild(element(\"span\", \"dependency-missing\", label));\n"
            + "      line.appendChild(element(\"span\", \"missing\", Texts.missing));\n      continue;",
            waits, StringComparison.Ordinal);
        Assert.Contains("link.href = storyHash(target, number);", waits, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AppJs_PlanSetsFileTextOnlyAsText()
    {
        string app = await Fetch("/app.js");
        string plan = Between(app, "// A link \"#N Title\" to a story", "\nfunction renderProject(");

        Assert.DoesNotContain("innerHTML", app, StringComparison.Ordinal);
        Assert.DoesNotContain("insertAdjacentHTML", app, StringComparison.Ordinal);
        Assert.DoesNotContain("outerHTML", app, StringComparison.Ordinal);
        // Titles go through titleElement and element(), both textContent only.
        Assert.Contains("link.appendChild(titleElement(title));", plan, StringComparison.Ordinal);
        Assert.Contains("    node.textContent = text;", app, StringComparison.Ordinal);
        Assert.Equal(2, plan.Split(".textContent = ").Length - 1);
        Assert.Contains("next.textContent = Texts.", plan, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AppJs_SectionsKeepAPlanSignatureAndAChangedOneIsReplaced()
    {
        string app = await Fetch("/app.js");
        string refresh = Between(app, "async function refreshBacklogs(names) {", "\nfunction restoreListScroll(");

        Assert.Contains("section.dataset.plan = planSignature(project);", app, StringComparison.Ordinal);
        Assert.Contains(
            "return JSON.stringify([project.next || null, project.stopped === true, asList(project.blocked)]);", app,
            StringComparison.Ordinal);
        Assert.Contains(
            "const changed = wanted.has(name) || (old !== undefined && old.dataset.plan !== planSignature(project));",
            refresh, StringComparison.Ordinal);
        // An unchanged section is kept; a busy one is kept and tried again at the next check.
        Assert.Contains("if (old && (!changed || sectionBusy(old))) {\n      if (changed) {\n        left.push(name);",
            refresh, StringComparison.Ordinal);
        Assert.Contains("keepOpenGroups(old, next);\n      old.replaceWith(next);", refresh, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AppCss_StylesThePlanInLightAndDarkThroughVariables()
    {
        string css = await Fetch("/app.css");

        Assert.Contains(".plan {", css, StringComparison.Ordinal);
        Assert.Contains(".plan .next-stopped {\n  color: var(--waiting-fg);\n  background: var(--waiting-bg);", css,
            StringComparison.Ordinal);
        Assert.Contains(".badge-spike {", css, StringComparison.Ordinal);
        Assert.Contains(".missing {\n  color: var(--error);", css, StringComparison.Ordinal);
        Assert.Contains(".dependency-missing {", css, StringComparison.Ordinal);
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
