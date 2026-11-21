namespace ControlRoom.Server.Tests;

/// <summary>Checks the folding groups on Backlogs in <c>app.js</c> and <c>app.css</c> as text (there is no JS test
/// runtime): every line group and the plan's Waiting group as a <c>details</c> with a <c>summary</c>, the defaults
/// (only Done closed), the stored state and its fallbacks, the toggle listener and the live refresh copy by group
/// name. The look and the folding itself are checked by hand.</summary>
public sealed class CollapsibleGroupsWebAppTests
{
    [Fact]
    public async Task AppJs_EveryLineGroupIsADetailsWithASummaryInTheOldOrder()
    {
        string app = await Fetch("/app.js");
        string group = Between(app, "function renderGroup(projectName, version, group, entries) {", "\n}\n");

        Assert.Contains("const heading = group.label + \" (\" + entries.length + \")\";", group, StringComparison.Ordinal);
        Assert.Contains("const details = element(\"details\", \"group\");", group, StringComparison.Ordinal);
        Assert.Contains("details.appendChild(element(\"summary\", null, heading));", group, StringComparison.Ordinal);
        Assert.Contains("details.appendChild(renderLines(projectName, version, entries));", group, StringComparison.Ordinal);
        Assert.DoesNotContain("\"section\"", group, StringComparison.Ordinal);
        Assert.DoesNotContain("\"h3\"", group, StringComparison.Ordinal);
        Assert.DoesNotContain("collapsed", group, StringComparison.Ordinal);
        int open = app.IndexOf("{ key: \"open\", label: \"Open\" }", StringComparison.Ordinal);
        int approval = app.IndexOf("{ key: \"approval\", label: \"Waiting for approval\" }", StringComparison.Ordinal);
        int user = app.IndexOf("{ key: \"user\", label: \"For you\" }", StringComparison.Ordinal);
        int done = app.IndexOf("{ key: \"done\", label: \"Done\", collapsed: true }", StringComparison.Ordinal);
        Assert.True(open >= 0 && open < approval && approval < user && user < done);
    }

    [Fact]
    public async Task AppJs_WaitingGroupIsADetailsWithASummaryShownOnlyWhenItemsWait()
    {
        string app = await Fetch("/app.js");
        string plan = Between(app, "function renderPlan(projectName, project) {", "\nfunction planSignature(");

        int shown = plan.IndexOf("if (blocked.length > 0) {", StringComparison.Ordinal);
        int details = plan.IndexOf("const group = element(\"details\", \"group waiting-group\");", StringComparison.Ordinal);
        Assert.True(shown >= 0 && details > shown);
        Assert.Contains("group.appendChild(element(\"summary\", null, Texts.waiting + \" (\" + blocked.length + \")\"));",
            plan, StringComparison.Ordinal);
        Assert.Contains("row.appendChild(renderWaitsFor(projectName, asList(entry.after)));", plan, StringComparison.Ordinal);
        Assert.Contains("foldGroup(group, projectName, \"waiting\");", plan, StringComparison.Ordinal);
        Assert.DoesNotContain("\"h3\"", plan, StringComparison.Ordinal);
        Assert.DoesNotContain("\"section\"", plan, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AppJs_OnlyDoneStartsClosedWithoutAStoredState()
    {
        string app = await Fetch("/app.js");
        string groups = Between(app, "const Groups = [", "];");
        string defaults = Between(app, "function groupDefaultOpen(key) {", "\n}\n");

        Assert.Equal(1, Count(groups, "collapsed"));
        Assert.Contains("{ key: \"done\", label: \"Done\", collapsed: true }", groups, StringComparison.Ordinal);
        // "waiting" has no Groups entry, so it falls through to open.
        Assert.Contains("const group = Groups.find((entry) => entry.key === key);", defaults, StringComparison.Ordinal);
        Assert.Contains("return !(group && group.collapsed === true);", defaults, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AppJs_StateIsReadDefensivelyFromOneKey()
    {
        string app = await Fetch("/app.js");
        string read = Between(app, "function storedGroupStates() {", "\n}\n");
        string open = Between(app, "function groupOpen(projectName, key) {", "\n}\n");
        string isObject = Between(app, "function isObject(value) {", "\n}\n");

        Assert.Equal(1, Count(app, "\"controlroom-backlog-groups\""));
        Assert.Contains("const BacklogGroupsKey = \"controlroom-backlog-groups\";", app, StringComparison.Ordinal);
        Assert.Equal(1, Count(read, "localStorage."));
        Assert.Contains("try {\n    states = JSON.parse(localStorage.getItem(BacklogGroupsKey));\n  } catch {\n"
            + "    return Object.create(null);\n  }", read, StringComparison.Ordinal);
        Assert.Contains("return Object.assign(Object.create(null), isObject(states) ? states : {});", read,
            StringComparison.Ordinal);
        Assert.Contains("return typeof value === \"object\" && value !== null && !Array.isArray(value);", isObject,
            StringComparison.Ordinal);
        Assert.Contains("Object.hasOwn(states, projectName) ? states[projectName] : null;", open, StringComparison.Ordinal);
        Assert.Contains("isObject(project) && Object.hasOwn(project, key) ? project[key] : null;", open,
            StringComparison.Ordinal);
        Assert.Contains("return typeof open === \"boolean\" ? open : groupDefaultOpen(key);", open, StringComparison.Ordinal);
        Assert.DoesNotContain("localStorage.", open, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AppJs_StateIsWrittenSafelyKeepingOtherEntries()
    {
        string app = await Fetch("/app.js");
        string write = Between(app, "function storeGroupOpen(projectName, key, open) {", "\n}\n");

        Assert.Equal(1, Count(write, "localStorage."));
        Assert.Contains("const states = storedGroupStates();", write, StringComparison.Ordinal);
        Assert.Contains("const project = Object.assign(Object.create(null), isObject(old) ? old : {});", write,
            StringComparison.Ordinal);
        Assert.Contains("project[key] = open;", write, StringComparison.Ordinal);
        Assert.Contains("try {\n    localStorage.setItem(BacklogGroupsKey, JSON.stringify(states));\n  } catch {", write,
            StringComparison.Ordinal);
        Assert.DoesNotContain("token", write, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("innerHTML", app, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AppJs_EachGroupIsNamedOpenedFromStateAndStoresTheUsersToggle()
    {
        string app = await Fetch("/app.js");
        string fold = Between(app, "function foldGroup(details, projectName, key) {", "\n}\n");
        string group = Between(app, "function renderGroup(projectName, version, group, entries) {", "\n}\n");
        string render = Between(app, "function renderProject(project) {", "\nfunction show(");

        Assert.Contains("details.dataset.group = key;", fold, StringComparison.Ordinal);
        Assert.Contains("details.open = groupOpen(projectName, key);", fold, StringComparison.Ordinal);
        Assert.Contains("details.addEventListener(\"toggle\", () => {", fold, StringComparison.Ordinal);
        Assert.Contains("storeGroupOpen(projectName, key, details.open);", fold, StringComparison.Ordinal);
        Assert.Contains("foldGroup(details, projectName, group.key);", group, StringComparison.Ordinal);
        Assert.Contains("section.appendChild(renderPlan(name, project));", render, StringComparison.Ordinal);
        Assert.Contains("section.appendChild(renderGroup(name, version, group, entries));", render, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AppJs_LiveRefreshCopiesOpenStateByGroupName()
    {
        string app = await Fetch("/app.js");
        string keep = Between(app, "function keepOpenGroups(old, next) {", "\n}\n");
        string refresh = Between(app, "async function refreshBacklogs(names) {", "\n}\n");

        Assert.Contains("const next = renderProject(project);", refresh, StringComparison.Ordinal);
        Assert.Contains("keepOpenGroups(old, next);\n      old.replaceWith(next);", refresh, StringComparison.Ordinal);
        Assert.Contains("for (const group of old.querySelectorAll(\"details.group\")) {\n"
            + "    before.set(group.dataset.group, group.open);", keep, StringComparison.Ordinal);
        Assert.Contains("if (before.has(group.dataset.group)) {\n      group.open = before.get(group.dataset.group);", keep,
            StringComparison.Ordinal);
        Assert.DoesNotContain("before.length === after.length", keep, StringComparison.Ordinal);
        Assert.DoesNotContain("[index]", keep, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AppJs_SectionBusyIsUnchanged()
    {
        string app = await Fetch("/app.js");
        string busy = Between(app, "function sectionBusy(section) {", "\n}\n");

        Assert.Equal("function sectionBusy(section) {\n  return section.querySelector(\".edit-title-form\") !== null\n"
            + "    || section.querySelector(\"details.edit-add[open]\") !== null\n"
            + "    || section.contains(document.activeElement);", busy);
    }

    [Fact]
    public async Task AppCss_GroupSummaryKeepsTheTouchHeightAndHoldsOnlyText()
    {
        string css = await Fetch("/app.css");
        string app = await Fetch("/app.js");
        string summary = Between(css, "\n.group summary {\n  list-style: none;", "}");

        Assert.Contains("min-height: 44px;", summary, StringComparison.Ordinal);
        Assert.Contains(".group[open] > summary::before {", css, StringComparison.Ordinal);
        // Both summaries are built with text only; no link inside a summary.
        Assert.Equal(2, Count(app, "element(\"summary\", null, "));
    }

    private static int Count(string text, string part)
    {
        int count = 0;
        for (int at = text.IndexOf(part, StringComparison.Ordinal); at >= 0;
             at = text.IndexOf(part, at + part.Length, StringComparison.Ordinal))
        {
            count += 1;
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
