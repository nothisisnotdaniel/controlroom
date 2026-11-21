namespace ControlRoom.Server.Tests;

/// <summary>Checks drag to reorder on Backlogs in <c>edit.js</c>, <c>app.js</c>, <c>app.css</c> and <c>live.js</c> as
/// text (there is no JS test runtime): a handle on every <c>[ ]</c> and <c>[?]</c> row, pointer events only, a drop
/// indicator, auto-scroll by animation frames, one <c>place</c> change per drop or arrow key, and no live refresh
/// while a drag is in progress. The feel on the iPhone and the Mac is checked by hand.</summary>
public sealed class DragDropWebAppTests
{
    [Fact]
    public async Task EditJs_HandleIsOnlyOnOpenAndApprovalRowsWithLabelAndHint()
    {
        string js = await Fetch("/edit.js");
        string handle = Between(js, "function dragHandle(projectName, version, entry) {", "\n}\n");

        Assert.Contains("if ((mark !== \" \" && mark !== \"?\") || line === null || version === \"\") {\n    return null;",
            handle, StringComparison.Ordinal);
        Assert.Contains("element(\"button\", \"drag-handle edit-only\", \"≡\")", handle, StringComparison.Ordinal);
        Assert.Contains("handle.type = \"button\";", handle, StringComparison.Ordinal);
        Assert.Contains("const subject = number !== null ? \"#\" + number : titleText(entry.title);", handle,
            StringComparison.Ordinal);
        Assert.Contains("handle.setAttribute(\"aria-label\", EditTexts.move + \" \" + subject);", handle,
            StringComparison.Ordinal);
        Assert.Contains("handle.setAttribute(\"aria-describedby\", hint.id);", handle, StringComparison.Ordinal);
        Assert.Contains("element(\"span\", \"visually-hidden drag-hint\", EditTexts.moveHint)", handle,
            StringComparison.Ordinal);
        Assert.Contains("  move: \"Move\",\n", js, StringComparison.Ordinal);
        Assert.Contains("  moveHint: \"Use arrow keys to move within the group.\",\n", js, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AppJs_HandleStartsTheRowOnlyThroughAppendEditRow()
    {
        string app = await Fetch("/app.js");
        string append = Between(app, "function appendEditRow(item, projectName, version, entry) {", "\n}\n");
        string plan = Between(app, "function renderPlan(projectName, project) {", "\nfunction planSignature(");

        int handle = append.IndexOf("const handle = dragHandle(projectName, version, entry);", StringComparison.Ordinal);
        int prepend = append.IndexOf("item.prepend(...handle);", StringComparison.Ordinal);
        int row = append.IndexOf("const row = editRow(projectName, version, entry);", StringComparison.Ordinal);
        Assert.True(handle >= 0 && handle < prepend && prepend < row);
        Assert.Equal(1, Count(app, "dragHandle("));
        // The plan's Waiting rows get no edit row and no handle.
        Assert.DoesNotContain("appendEditRow", plan, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EditJs_HasNoUpOrDownButHasToTop()
    {
        string js = await Fetch("/edit.js");
        string row = Between(js, "function editRow(projectName, version, entry) {", "\n}\n");

        Assert.DoesNotContain("\"Up\"", js, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Down\"", js, StringComparison.Ordinal);
        Assert.DoesNotContain("action: \"up\"", js, StringComparison.Ordinal);
        Assert.DoesNotContain("action: \"down\"", js, StringComparison.Ordinal);
        Assert.DoesNotContain("EditTexts.up", js, StringComparison.Ordinal);
        Assert.DoesNotContain("EditTexts.down", js, StringComparison.Ordinal);
        Assert.Contains("editButton(EditTexts.toTop, null, () => send({ action: \"top\" }))", row,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task EditJs_DragsWithPointerEventsOnlyAndCapturesThePointer()
    {
        string js = await Fetch("/edit.js");
        string handle = Between(js, "function dragHandle(projectName, version, entry) {", "\n}\n");

        foreach (string name in new[] { "pointerdown", "pointermove", "pointerup", "pointercancel" })
        {
            Assert.Contains("handle.addEventListener(\"" + name + "\"", handle, StringComparison.Ordinal);
        }

        Assert.Contains("handle.setPointerCapture(event.pointerId);", js, StringComparison.Ordinal);
        foreach (string html5 in new[] { "draggable", "dragstart", "dataTransfer", "dragover", "addEventListener(\"drop\"" })
        {
            Assert.DoesNotContain(html5, js, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task AppCss_OnlyTheHandleStopsTouchScrollingAndHasA44PxTarget()
    {
        string css = await Fetch("/app.css");
        string handle = Between(css, "\n.drag-handle {", "\n}\n");

        Assert.Equal(1, Count(css, "touch-action"));
        Assert.Contains("touch-action: none;", handle, StringComparison.Ordinal);
        Assert.Contains("min-width: 44px;", handle, StringComparison.Ordinal);
        Assert.Contains("min-height: 44px;", handle, StringComparison.Ordinal);
        Assert.Contains("\n.project:not(.editing) .edit-only {\n  display: none;\n}", css, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AppCss_StylesTheDraggedRowAndTheIndicatorWithThemeColours()
    {
        string css = await Fetch("/app.css");
        string dragging = Between(css, "\n.lines li.dragging {", "\n}\n");
        string indicator = Between(css, "\n.lines li.drop-indicator {", "\n}\n");

        Assert.Contains("var(--card)", dragging, StringComparison.Ordinal);
        Assert.Contains("position: absolute;", indicator, StringComparison.Ordinal);
        Assert.Contains("var(--accent)", indicator, StringComparison.Ordinal);
        Assert.Contains("pointer-events: none;", indicator, StringComparison.Ordinal);
        Assert.Contains("\n.lines.drag-list {\n  position: relative;\n}", css, StringComparison.Ordinal);
        // --card and --accent have a light value and a dark one.
        string dark = Between(css, "@media (prefers-color-scheme: dark) {", "\n}\n");
        Assert.Contains("--card:", dark, StringComparison.Ordinal);
        Assert.Contains("--accent:", dark, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EditJs_MarksTheRowAndShowsOneIndicatorWhileDragging()
    {
        string js = await Fetch("/edit.js");
        string start = Between(js, "function startDrag(event, handle, place) {", "\n}\n");
        string cancel = Between(js, "function cancelDrag() {", "\n}\n");

        Assert.Contains("row.classList.add(\"dragging\");", start, StringComparison.Ordinal);
        Assert.Equal(1, Count(js, "element(\"li\", \"drop-indicator\")"));
        Assert.Contains("list.appendChild(indicator);", start, StringComparison.Ordinal);
        Assert.Contains("dragBusy = true;", start, StringComparison.Ordinal);
        Assert.Contains("state.indicator.remove();", cancel, StringComparison.Ordinal);
        Assert.Contains("state.row.classList.remove(\"dragging\");", cancel, StringComparison.Ordinal);
        Assert.Contains("dragBusy = false;", cancel, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EditJs_AutoScrollsNearTheEdgesByAnimationFramesWithoutTimers()
    {
        string js = await Fetch("/edit.js");
        string frame = Between(js, "function dragFrame() {", "\n}\n");
        string cancel = Between(js, "function cancelDrag() {", "\n}\n");

        Assert.Contains("const DragEdge = 48;", js, StringComparison.Ordinal);
        Assert.Contains("if (dragState.y < DragEdge) {", frame, StringComparison.Ordinal);
        Assert.Contains("} else if (dragState.y > window.innerHeight - DragEdge) {", frame, StringComparison.Ordinal);
        Assert.Contains("window.scrollBy(0, step);", frame, StringComparison.Ordinal);
        Assert.Contains("dragState.frame = requestAnimationFrame(dragFrame);", frame, StringComparison.Ordinal);
        Assert.Contains("cancelAnimationFrame(state.frame);", cancel, StringComparison.Ordinal);
        Assert.DoesNotContain("setTimeout", js, StringComparison.Ordinal);
        Assert.DoesNotContain("setInterval", js, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EditJs_ADropAtANewPlaceInsideTheListSendsOnePlaceAndReloads()
    {
        string js = await Fetch("/edit.js");
        string place = Between(js, "function dropPlace() {", "\n}\n");
        string end = Between(js, "function endDrag(event, release) {", "\n}\n");
        string send = Between(js, "async function sendPlace(place, line, target, where, focus) {", "\n}\n");

        Assert.Contains(
            "if (state.x < box.left || state.x > box.right || state.y < box.top || state.y > box.bottom) {\n    return null;",
            place, StringComparison.Ordinal);
        Assert.Contains("const others = state.rows.filter((row) => row !== state.row);", place, StringComparison.Ordinal);
        Assert.Contains(
            "if (!release || drop === null || drop.index === state.original || drop.others.length === 0) {\n    return;",
            end, StringComparison.Ordinal);
        Assert.Contains("const target = before ? drop.others[0] : drop.others[drop.index - 1];", end,
            StringComparison.Ordinal);
        Assert.Equal(1, Count(end, "sendPlace("));
        Assert.Contains(
            "project: place.project,\n    version: place.version,\n    action: \"place\",\n    line: line,\n"
            + "    target: target,\n    where: where,\n", send, StringComparison.Ordinal);
        Assert.Contains("const ok = await sendChange({", send, StringComparison.Ordinal);
        Assert.Contains("await load();", send, StringComparison.Ordinal);
        Assert.Equal(1, Count(js, "action: \"place\""));
    }

    [Fact]
    public async Task EditJs_EscapeAndPointercancelEndTheDragWithoutSending()
    {
        string js = await Fetch("/edit.js");
        string handle = Between(js, "function dragHandle(projectName, version, entry) {", "\n}\n");
        string key = Between(js, "function dragKey(event) {", "\n}\n");

        Assert.Contains("handle.addEventListener(\"pointercancel\", (event) => endDrag(event, false));", handle,
            StringComparison.Ordinal);
        Assert.Contains("handle.addEventListener(\"lostpointercapture\", (event) => endDrag(event, false));", handle,
            StringComparison.Ordinal);
        Assert.Contains("if (event.key === \"Escape\" && dragState !== null) {", key, StringComparison.Ordinal);
        Assert.Contains("cancelDrag();", key, StringComparison.Ordinal);
        Assert.DoesNotContain("sendPlace", key, StringComparison.Ordinal);
        Assert.Contains("document.addEventListener(\"keydown\", dragKey, true);", js, StringComparison.Ordinal);
        Assert.Contains("document.removeEventListener(\"keydown\", dragKey, true);", js, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EditJs_TheHandleClickNeverOpensTheStory()
    {
        string js = await Fetch("/edit.js");
        string handle = Between(js, "function dragHandle(projectName, version, entry) {", "\n}\n");

        Assert.Contains(
            "handle.addEventListener(\"click\", (event) => {\n    event.preventDefault();\n    event.stopPropagation();\n  });",
            handle, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EditJs_ArrowKeysPlaceBeforeThePreviousOrAfterTheNextAndRefocus()
    {
        string js = await Fetch("/edit.js");
        string key = Between(js, "function keyMove(event, handle, place) {", "\n}\n");
        string focus = Between(js, "function focusHandle(projectName, focus) {", "\n}\n");
        string send = Between(js, "async function sendPlace(place, line, target, where, focus) {", "\n}\n");

        Assert.Contains("if (event.key !== \"ArrowUp\" && event.key !== \"ArrowDown\") {", key, StringComparison.Ordinal);
        Assert.Contains("const other = up ? index - 1 : index + 1;", key, StringComparison.Ordinal);
        Assert.Contains("if (index < 0 || other < 0 || other >= rows.length) {\n    return;", key, StringComparison.Ordinal);
        Assert.Contains("sendPlace(place, line, target, up ? \"before\" : \"after\", focus);", key,
            StringComparison.Ordinal);
        Assert.Contains("const focus = { number: handle.dataset.number || null, line: target };", key,
            StringComparison.Ordinal);
        Assert.Contains("focusHandle(place.project, focus);", send, StringComparison.Ordinal);
        Assert.Contains("node.dataset.number === focus.number", focus, StringComparison.Ordinal);
        Assert.Contains("handle.focus();", focus, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EditJs_ASecondDragOrKeyMoveIsIgnoredWhileBusy()
    {
        string js = await Fetch("/edit.js");
        string start = Between(js, "function startDrag(event, handle, place) {", "\n}\n");
        string key = Between(js, "function keyMove(event, handle, place) {", "\n}\n");

        Assert.Contains("let dragBusy = false;", js, StringComparison.Ordinal);
        Assert.StartsWith("\n  if (dragBusy || changeBusy ||", start[start.IndexOf('\n', StringComparison.Ordinal)..],
            StringComparison.Ordinal);
        Assert.Contains("if (dragBusy || changeBusy) {\n    return;", key, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LiveJs_ReplacesNoSectionWhileADragIsInProgress()
    {
        string js = await Fetch("/live.js");
        string refresh = Between(js, "async function refreshStaleBacklogs() {", "\n}\n");

        Assert.StartsWith("async function refreshStaleBacklogs() {\n  if (dragBusy || ", refresh, StringComparison.Ordinal);
        int check = refresh.IndexOf("dragBusy", StringComparison.Ordinal);
        int call = refresh.IndexOf("refreshBacklogs(names)", StringComparison.Ordinal);
        Assert.True(check >= 0 && check < call);
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
