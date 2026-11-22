namespace ControlRoom.Server.Tests;

/// <summary>Checks the auto stop line on Backlogs in <c>app.js</c>, <c>edit.js</c> and <c>app.css</c> as text (there
/// is no JS test runtime): the divider in the Open list, its edit controls, drag and keys through <c>stop_move</c>,
/// the add and remove buttons, the plan's <c>Auto stops before #N</c> and the fixed error text. The feel of the drag
/// on the iPhone is checked by hand.</summary>
public sealed class StopLineWebAppTests
{
    [Fact]
    public async Task AppJs_TheDividerIsASeparatorWithAFixedTextAndNoPosition()
    {
        string app = await Fetch("/app.js");
        string divider = Between(app, "function renderStopLine(projectName, version, line) {", "\n}\n");

        Assert.Contains("  stopLine: \"Auto stops here\",\n", app, StringComparison.Ordinal);
        Assert.Contains("const item = element(\"li\", \"stop-line\");", divider, StringComparison.Ordinal);
        Assert.Contains("item.dataset.line = String(line);", divider, StringComparison.Ordinal);
        Assert.Contains("element(\"span\", \"stop-line-label\", Texts.stopLine)", divider, StringComparison.Ordinal);
        Assert.Contains("label.setAttribute(\"role\", \"separator\");", divider, StringComparison.Ordinal);
        Assert.Contains("label.setAttribute(\"aria-label\", Texts.stopLine);", divider, StringComparison.Ordinal);
        Assert.Contains("appendStopEdit(item, projectName, version, line);", divider, StringComparison.Ordinal);
        Assert.DoesNotContain("positionElement", divider, StringComparison.Ordinal);
        Assert.DoesNotContain("innerHTML", divider, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AppJs_TheDividerGoesBeforeTheFirstRowBelowTheStopLineElseAtTheEnd()
    {
        string app = await Fetch("/app.js");
        string place = Between(app, "function placeStopLine(details, projectName, version, stopLine, entries) {", "\n}\n");
        string render = Between(app, "function renderProject(project) {", "\nfunction show(");

        Assert.Contains("return line !== null && line > stopLine;", place, StringComparison.Ordinal);
        Assert.Contains(
            "list.insertBefore(renderStopLine(projectName, version, stopLine), below < 0 ? null : rows[below]);", place,
            StringComparison.Ordinal);
        // The Open group shows with only the divider when it has no rows; the positions stay those of renderLines.
        Assert.Contains("const stopLine = asNumber(project.stopLine);", render, StringComparison.Ordinal);
        Assert.Contains("if (entries.length > 0 || (open && stopLine !== null)) {", render, StringComparison.Ordinal);
        Assert.Contains("placeStopLine(section.lastElementChild, name, version, stopLine, entries);", render,
            StringComparison.Ordinal);
        Assert.DoesNotContain("position", place, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EditJs_TheDividerHasAMoveHandleAndARemoveButtonWithAConfirm()
    {
        string js = await Fetch("/edit.js");
        string edit = Between(js, "function appendStopEdit(item, projectName, version, line) {", "\n}\n");

        Assert.Contains("  moveStop: \"Move stop line\",\n", js, StringComparison.Ordinal);
        Assert.Contains("  removeStop: \"Remove stop line\",\n", js, StringComparison.Ordinal);
        Assert.Contains("element(\"button\", \"drag-handle edit-only\", \"≡\")", edit, StringComparison.Ordinal);
        Assert.Contains("handle.setAttribute(\"aria-label\", EditTexts.moveStop);", edit, StringComparison.Ordinal);
        Assert.Contains("handle.setAttribute(\"aria-describedby\", hint.id);", edit, StringComparison.Ordinal);
        Assert.Contains("handle.dataset.line = String(line);", edit, StringComparison.Ordinal);
        Assert.Contains("const place = { project: projectName, version: version, stop: true };", edit,
            StringComparison.Ordinal);
        Assert.Contains("item.prepend(handle, hint);", edit, StringComparison.Ordinal);
        Assert.Contains("if (confirm(EditTexts.confirmRemoveStop)) {", edit, StringComparison.Ordinal);
        Assert.Contains(
            "changeAndReload({ project: projectName, version: version, action: \"stop_remove\", line: line });", edit,
            StringComparison.Ordinal);
        Assert.Contains("element(\"div\", \"edit-row edit-only\")", edit, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EditJs_DividerDragAndKeysSendOneStopMoveAndEscapeOrCancelSendNothing()
    {
        string js = await Fetch("/edit.js");
        string edit = Between(js, "function appendStopEdit(item, projectName, version, line) {", "\n}\n");
        string end = Between(js, "function endDrag(event, release) {", "\n}\n");
        string key = Between(js, "function keyMove(event, handle, place) {", "\n}\n");
        string send = Between(js, "async function sendStopMove(place, line, target, where, focus) {", "\n}\n");
        string escape = Between(js, "function dragKey(event) {", "\n}\n");

        foreach (string name in new[] { "pointerdown", "pointermove", "pointerup", "keydown" })
        {
            Assert.Contains("handle.addEventListener(\"" + name + "\"", edit, StringComparison.Ordinal);
        }

        Assert.Contains("handle.addEventListener(\"pointercancel\", (event) => endDrag(event, false));", edit,
            StringComparison.Ordinal);
        Assert.Contains("handle.addEventListener(\"lostpointercapture\", (event) => endDrag(event, false));", edit,
            StringComparison.Ordinal);
        Assert.Contains(
            "if (state.place.stop === true) {\n    sendStopMove(state.place, rowLine(state.row), rowLine(target), "
            + "before ? \"before\" : \"after\", null);\n    return;", end, StringComparison.Ordinal);
        Assert.Contains(
            "if (place.stop === true) {\n    sendStopMove(place, line, target, up ? \"before\" : \"after\", { stop: true });",
            key, StringComparison.Ordinal);
        Assert.Equal(1, Count(js, "action: \"stop_move\""));
        Assert.Contains(
            "project: place.project,\n    version: place.version,\n    action: \"stop_move\",\n    line: line,\n"
            + "    target: target,\n    where: where,\n", send, StringComparison.Ordinal);
        Assert.Contains("const ok = await sendChange({", send, StringComparison.Ordinal);
        Assert.DoesNotContain("sendStopMove", escape, StringComparison.Ordinal);
        // A drop at the original place, or outside the list, sends nothing for the divider too.
        int refuse = end.IndexOf("if (!release || drop === null || drop.index === state.original", StringComparison.Ordinal);
        int stop = end.IndexOf("sendStopMove(", StringComparison.Ordinal);
        Assert.True(refuse >= 0 && refuse < stop);
    }

    [Fact]
    public async Task EditJs_DraggedItemsCountTheDividerAsARowAndTakeItsLineAsTarget()
    {
        string js = await Fetch("/edit.js");
        string rows = Between(js, "function dragRows(list) {", "\n}\n");
        string line = Between(js, "function rowLine(row) {", "\n}\n");
        string end = Between(js, "function endDrag(event, release) {", "\n}\n");

        // The divider's handle is a direct child with data-line, so it is one of the rows and a place target.
        Assert.Contains("row.querySelector(\":scope > .drag-handle\") !== null", rows, StringComparison.Ordinal);
        Assert.Contains("Number(row.querySelector(\":scope > .drag-handle\").dataset.line)", line, StringComparison.Ordinal);
        Assert.Contains("const target = before ? drop.others[0] : drop.others[drop.index - 1];", end,
            StringComparison.Ordinal);
        Assert.Contains(
            "sendPlace(state.place, rowLine(state.row), rowLine(target), before ? \"before\" : \"after\", null);", end,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task EditJs_FocusReturnsToTheDividerHandleAfterAKeyMove()
    {
        string js = await Fetch("/edit.js");
        string focus = Between(js, "function focusHandle(projectName, focus) {", "\n}\n");
        string send = Between(js, "async function sendStopMove(place, line, target, where, focus) {", "\n}\n");

        Assert.Contains("if (focus.stop === true) {", focus, StringComparison.Ordinal);
        Assert.Contains("section.querySelector(\".stop-line > .drag-handle\")", focus, StringComparison.Ordinal);
        Assert.Contains("await load();", send, StringComparison.Ordinal);
        Assert.Contains("focusHandle(place.project, focus);", send, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EditJs_WithoutAStopLineEveryOpenRowHasStopAfterThisAndTheGroupAddStopLine()
    {
        string app = await Fetch("/app.js");
        string js = await Fetch("/edit.js");
        string place = Between(app, "function placeStopLine(details, projectName, version, stopLine, entries) {", "\n}\n");
        string after = Between(js, "function appendStopAfter(item, projectName, version, entry) {", "\n}\n");
        string add = Between(js, "function addStopButton(projectName, version, entries) {", "\n}\n");

        // Both only show without a stop line: placeStopLine returns after placing the divider.
        int divider = place.IndexOf("return;", StringComparison.Ordinal);
        int perRow = place.IndexOf("appendStopAfter(rows[index], projectName, version, entry)", StringComparison.Ordinal);
        int group = place.IndexOf("const add = addStopButton(projectName, version, entries);", StringComparison.Ordinal);
        Assert.True(divider >= 0 && divider < perRow && perRow < group);

        Assert.Contains("  stopAfter: \"Stop after this\",\n", js, StringComparison.Ordinal);
        Assert.Contains("  addStop: \"Add stop line\",\n", js, StringComparison.Ordinal);
        Assert.Contains("if (asText(entry && entry.mark) !== \" \" || line === null || version === \"\" || row === null) {",
            after, StringComparison.Ordinal);
        Assert.Contains("changeAndReload({ project: projectName, version: version, action: \"stop_add\", line: line });",
            after, StringComparison.Ordinal);
        Assert.Contains("button.setAttribute(\"aria-label\", EditTexts.stopAfter + \" \" + subject);", after,
            StringComparison.Ordinal);
        // Add stop line: after the first [ ] row, and nothing without one.
        Assert.Contains("const first = entries.find((entry) => asText(entry && entry.mark) === \" \"", add,
            StringComparison.Ordinal);
        Assert.Contains("if (version === \"\" || first === undefined) {\n    return null;", add, StringComparison.Ordinal);
        Assert.Contains(
            "changeAndReload({ project: projectName, version: version, action: \"stop_add\", line: first.line });", add,
            StringComparison.Ordinal);
        Assert.Contains("element(\"div\", \"edit-row edit-only stop-add-row\")", add, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EditJs_EveryStopChangeGoesThroughSendChange()
    {
        string js = await Fetch("/edit.js");
        string load = Between(js, "async function changeAndReload(body) {", "\n}\n");

        Assert.Contains("if (await sendChange(body)) {", load, StringComparison.Ordinal);
        Assert.Equal(1, Count(js, "authFetch(\"/api/backlog/change\""));
        Assert.Equal(2, Count(js, "action: \"stop_add\""));
        Assert.Equal(1, Count(js, "action: \"stop_remove\""));
        // The stop text is the server's: the client never sends it.
        Assert.DoesNotContain("Auto stops here", js, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AppJs_ThePlanSaysAutoStopsBeforeTheNextItemAndTheSignatureHasTheStopLine()
    {
        string app = await Fetch("/app.js");
        string plan = Between(app, "function renderPlan(projectName, project) {", "\nfunction planSignature(");
        string signature = Between(app, "function planSignature(project) {", "\n}\n");

        Assert.Contains("  stopsBefore: \"Auto stops before #\",\n", app, StringComparison.Ordinal);
        int link = plan.IndexOf("next.appendChild(planItemLink(projectName, number, item.title));", StringComparison.Ordinal);
        int stop = plan.IndexOf(
            "if (item.belowStop === true) {\n      next.appendChild(element(\"span\", \"next-stop\", Texts.stopsBefore + number));",
            StringComparison.Ordinal);
        Assert.True(link >= 0 && stop > link);
        Assert.Contains("const stop = asNumber(project.stopLine);", signature, StringComparison.Ordinal);
        Assert.Contains("asList(project.blocked), stop]);", signature, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EditJs_TheStopLineErrorHasAFixedText()
    {
        string js = await Fetch("/edit.js");
        string errors = Between(js, "const ChangeErrors = {", "};");

        Assert.Contains(
            "  stop_line: \"This backlog has more than one stop line, or one with other text. Fix backlog.md first.\",\n",
            errors, StringComparison.Ordinal);
        Assert.Contains(
            "Object.prototype.hasOwnProperty.call(ChangeErrors, code) ? ChangeErrors[code] : ChangeErrors.failed", js,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task AppCss_TheDividerAndTheStopNoteUseThemeColoursForLightAndDark()
    {
        string css = await Fetch("/app.css");
        string divider = Between(css, "\n.lines li.stop-line {", "\n}\n");
        string label = Between(css, "\n.stop-line-label {", "\n}\n");
        string next = Between(css, "\n.plan .next-stop {", "\n}\n");
        string dark = Between(css, "@media (prefers-color-scheme: dark) {", "\n}\n");

        Assert.Contains("border-top: 2px dashed var(--waiting-fg);", divider, StringComparison.Ordinal);
        // The rule under the divider skips the drop indicator, which keeps its accent line next to a last-row divider.
        Assert.Contains("\n.lines li.stop-line + li:not(.drop-indicator) {", css, StringComparison.Ordinal);
        Assert.DoesNotContain("\n.lines li.stop-line + li {", css, StringComparison.Ordinal);
        Assert.Contains("background: var(--waiting-bg);", label, StringComparison.Ordinal);
        Assert.Contains("color: var(--waiting-fg);", label, StringComparison.Ordinal);
        Assert.Contains("background: var(--waiting-bg);", next, StringComparison.Ordinal);
        Assert.Contains("color: var(--waiting-fg);", next, StringComparison.Ordinal);
        Assert.Contains("--waiting-bg:", dark, StringComparison.Ordinal);
        Assert.Contains("--waiting-fg:", dark, StringComparison.Ordinal);
        // The divider's handle is a .drag-handle, so it has the same 44 px target as the item handles.
        string handle = Between(css, "\n.drag-handle {", "\n}\n");
        Assert.Contains("min-height: 44px;", handle, StringComparison.Ordinal);
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
