namespace ControlRoom.Server.Tests;

/// <summary>The "Your decision" section in <c>act.js</c>, its hooks in <c>board.js</c>, <c>index.html</c>,
/// <c>app.css</c> and <c>sw.js</c>, and the rules in <c>CLAUDE.md</c>, checked as text (there is no JS test runtime;
/// the files are served synchronously): fixed texts, script order, rework note validation, the needs-human rule and the
/// live-refresh guard. Tapping through on the phone is checked by hand.</summary>
public sealed class ActWebAppTests
{
    [Fact]
    public async Task IndexHtml_LoadsActJsRightAfterBoardJs()
    {
        string html = await Fetch("/index.html");

        Assert.Contains(
            "<script src=\"/board.js\" defer></script>\n  <script src=\"/act.js\" defer></script>\n"
            + "  <script src=\"/push.js\" defer></script>", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ActJs_TextsAreFixedStrings()
    {
        string js = await Fetch("/act.js");
        string texts = Between(js, "const ActTexts = {", "\n};\n") + "\n";

        Assert.Contains("  heading: \"Your decision\",\n", texts, StringComparison.Ordinal);
        Assert.Contains("  approve: \"Approve\",\n", texts, StringComparison.Ordinal);
        Assert.Contains("  send: \"Send\",\n", texts, StringComparison.Ordinal);
        Assert.Contains("  waitingStart: \"Waiting for the next /factory session for \",\n", texts, StringComparison.Ordinal);
        Assert.Contains("  approveSent: \"Approve\",\n", texts, StringComparison.Ordinal);
        Assert.Contains("  reworkSent: \"Rework\",\n", texts, StringComparison.Ordinal);
        Assert.Contains("  sent: \" sent \",\n", texts, StringComparison.Ordinal);
        Assert.Contains("  cancel: \"Cancel request\",\n", texts, StringComparison.Ordinal);
        Assert.Contains("  hintStart: \"Nothing happens until the next /factory session for \",\n", texts,
            StringComparison.Ordinal);
        Assert.Contains("const when = actLocalTime(asText(request.requested));", js, StringComparison.Ordinal);
        Assert.Contains("ActTexts.waitingStart + project + \": \" + label + (when ? ActTexts.sent + when : \"\")", js,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task ActJs_EveryErrorCodeHasItsOwnFixedText()
    {
        string js = await Fetch("/act.js");
        string errors = Between(js, "const ActErrors = {", "\n};\n") + "\n";

        foreach (string code in new[]
                 {
                     "  invalid: \"", "  \"not found\": \"", "  not_waiting: \"", "  changed: \"", "  pending: \"",
                     "  not_pending: \"", "  write_failed: \"", "  unreachable: \"", "  failed: \"",
                 })
        {
            Assert.Contains(code, errors, StringComparison.Ordinal);
        }

        Assert.Contains("Object.prototype.hasOwnProperty.call(ActErrors, code) ? ActErrors[code] : ActErrors.failed", js,
            StringComparison.Ordinal);
        Assert.Contains("pending: \"A request for this job is already waiting.\"", errors, StringComparison.Ordinal);
        Assert.DoesNotContain(".message", js, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ActJs_ApproveAsksForConfirmationAndReworkKeepsTheDraftWithALiveError()
    {
        string js = await Fetch("/act.js");
        string choices = Between(js, "function renderActChoices(section, job) {", "\n}\n");

        Assert.Contains("if (window.confirm(ActTexts.confirmApprove)) {\n      sendJobAction(job, { action: \"approve\" });",
            choices, StringComparison.Ordinal);
        Assert.Contains("element(\"textarea\", \"act-note\")", choices, StringComparison.Ordinal);
        Assert.Contains("label.htmlFor = \"act-note\";", choices, StringComparison.Ordinal);
        Assert.DoesNotContain(".maxLength", js, StringComparison.Ordinal);
        Assert.DoesNotContain("\"maxlength\"", js, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("note.value =", choices, StringComparison.Ordinal);
        Assert.DoesNotContain(".blur()", choices, StringComparison.Ordinal);
        Assert.Contains("note.setAttribute(\"aria-describedby\", hint.id);", choices, StringComparison.Ordinal);
        Assert.Contains("hint.setAttribute(\"aria-live\", \"polite\");", choices, StringComparison.Ordinal);
        Assert.Contains("note.setAttribute(\"aria-invalid\", String(state.tooLong));", choices, StringComparison.Ordinal);
        Assert.Contains("hint.textContent = state.tooLong ? ActTexts.noteTooLong : ActTexts.noteHint;", choices,
            StringComparison.Ordinal);
        Assert.Contains("noteTooLong: \"Shorten your note to 1,000 characters or fewer.\"", js, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ActJs_InputSendAndButtonResetUseTheCleanedScalarLimitAndBusyState()
    {
        string js = await Fetch("/act.js");
        string state = Between(js, "function actNoteState(value) {", "\n}\n");
        string choices = Between(js, "function renderActChoices(section, job) {", "\n}\n");
        string buttons = Between(js, "function setActButtons() {", "\n}\n");

        Assert.Contains("const ActNoteMaxLength = 1000;", js, StringComparison.Ordinal);
        Assert.Contains("for (const scalar of value)", state, StringComparison.Ordinal);
        Assert.Contains("/\\p{White_Space}/u.test(scalar)", state, StringComparison.Ordinal);
        Assert.Contains("length += separator.length + 1;", state, StringComparison.Ordinal);
        Assert.Contains("if (length > ActNoteMaxLength) {\n      return { text: null, tooLong: true, disabled: true };",
            state, StringComparison.Ordinal);
        Assert.Contains("disabled: activeAction !== null || length === 0", state, StringComparison.Ordinal);
        Assert.Contains("send.disabled = true;", choices, StringComparison.Ordinal);
        Assert.Contains("note.addEventListener(\"input\", () => {\n    const state = actNoteState(note.value);\n"
            + "    send.disabled = state.disabled;", choices, StringComparison.Ordinal);
        Assert.Contains("const state = actNoteState(note.value);\n    if (!state.disabled) {\n"
            + "      sendJobAction(job, { action: \"rework\", note: state.text });", choices, StringComparison.Ordinal);
        Assert.Contains("button.disabled = button.id === \"act-send\" ? actNoteState(note.value).disabled : activeAction !== null;",
            buttons, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ActJs_LocksTheDraftUntilTheConfirmedRequestReplacesItAndUnlocksOnFailure()
    {
        string js = await Fetch("/act.js");
        string buttons = Between(js, "function setActButtons() {", "\n}\n");
        string send = Between(js, "async function sendJobAction(job, body) {", "\n}\n");
        string apply = Between(js, "function applyDecisionResult(section, current, submitted, request) {", "\n}\n");

        Assert.Contains("note.readOnly = activeAction !== null;", buttons, StringComparison.Ordinal);
        int locked = send.IndexOf("activeAction = submitted;\n  setActButtons();", StringComparison.Ordinal);
        int post = send.IndexOf("await authFetch(ActRoute", StringComparison.Ordinal);
        int settled = send.IndexOf("activeAction = null;", StringComparison.Ordinal);
        int confirmed = send.IndexOf("applyDecisionResult(active, current, submitted, notPending ? null : data.request)",
            StringComparison.Ordinal);
        int unlocked = send.IndexOf("setActButtons();", confirmed, StringComparison.Ordinal);
        int refresh = send.IndexOf("loadJob(asText(job.id), true);", confirmed, StringComparison.Ordinal);
        Assert.True(locked >= 0 && locked < post && post < settled && settled < confirmed && confirmed < unlocked && unlocked < refresh);
        Assert.Contains("const next = renderJobDecision({ job: current.job, request: pending });\n    section.replaceWith(next);",
            apply, StringComparison.Ordinal);
        Assert.Contains("const text = notPending ? ActErrors.not_pending : ActTexts.cancelled;", send, StringComparison.Ordinal);
        Assert.Contains("showActStatus(text, notPending);", send, StringComparison.Ordinal);
        Assert.DoesNotContain("if (data.request)", send, StringComparison.Ordinal);
        Assert.DoesNotContain("note.value =", send, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ActJs_PendingShowsTheNoteAsTextAndCancel()
    {
        string js = await Fetch("/act.js");
        string pending = Between(js, "function renderActPending(section, job, request) {", "\n}\n");

        Assert.Contains("element(\"p\", \"act-note-text\", note)", pending, StringComparison.Ordinal);
        Assert.Contains("sendJobAction(job, { action: \"cancel\" })", pending, StringComparison.Ordinal);
        string button = Between(js, "function actButton(text, className, onClick) {", "\n}\n");
        Assert.Contains("button.isConnected && !button.disabled && button.getAttribute(\"aria-disabled\") !== \"true\"", button,
            StringComparison.Ordinal);
        Assert.DoesNotContain("innerHTML", js, StringComparison.Ordinal);
        Assert.Contains("authFetch(ActRoute, {", js, StringComparison.Ordinal);
        Assert.Contains("const ActRoute = \"/api/job/act\";", js, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ActJs_BuildsTheSectionOnlyForNeedsHuman()
    {
        string js = await Fetch("/act.js");
        string render = Between(js, "function renderJobDecision(data) {", "\n}\n");

        Assert.Contains("if (asText(job.stage) !== \"needs-human\") {\n    return null;\n  }", render,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task BoardJs_PlacesTheSectionUnderTheStateSectionAndGuardsTheLiveRefresh()
    {
        string js = await Fetch("/board.js");
        string page = Between(js, "function renderJobPage(data) {", "\n}\n");
        string poll = Between(js, "async function pollBoard() {", "\n}\n");
        string refresh = Between(js, "function refreshOpenJob(jobs) {", "\n}\n");
        string load = Between(js, "async function loadJob(id, refresh) {", "\n}\n");
        string act = await Fetch("/act.js");
        string live = await Fetch("/live.js");
        string busy = Between(act, "function jobDecisionBusy(job, request) {", "\n}\n");

        int state = page.IndexOf("article.appendChild(state);", StringComparison.Ordinal);
        int decision = page.IndexOf("article.appendChild(decision);", StringComparison.Ordinal);
        int tabs = page.IndexOf("renderTabs(records, select)", StringComparison.Ordinal);
        Assert.True(state >= 0 && decision > state && tabs > decision);
        int observation = poll.IndexOf("refreshOpenJob(data?.jobs);", StringComparison.Ordinal);
        Assert.True(observation >= 0 && observation < poll.IndexOf("if (text === boardText)", StringComparison.Ordinal));
        Assert.Contains("observeDecisionBoard(entry)", refresh, StringComparison.Ordinal);
        Assert.Contains("jobRefreshHeld = true;\n      return;", refresh, StringComparison.Ordinal);
        Assert.Contains("if (refresh && jobDecisionHeld(data?.job, data?.request ?? null)) {", load, StringComparison.Ordinal);
        Assert.Contains("current.detail = { job, request };", busy, StringComparison.Ordinal);
        Assert.Contains("cancel && document.activeElement === cancel", busy, StringComparison.Ordinal);
        Assert.Contains("return activeAction !== null ||", busy, StringComparison.Ordinal);
        Assert.Contains("await recheckPendingCancel(previousJobs !== next.jobs || recovered, { includeForm: previousJobs === undefined });",
            live, StringComparison.Ordinal);
        Assert.Contains("document.activeElement === note || /\\P{White_Space}/u.test(note.value)", busy,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task BoardJs_CardShowsTheSentChip()
    {
        string js = await Fetch("/board.js");

        Assert.Contains("  approveSent: \"Approve sent\",\n", js, StringComparison.Ordinal);
        Assert.Contains("  reworkSent: \"Rework sent\",\n", js, StringComparison.Ordinal);
        Assert.Contains("const sent = requestChipText(job);", Between(js, "function renderCard(job) {", "\n}\n"),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task AppCss_StylesTheSection()
    {
        string css = await Fetch("/app.css");

        Assert.Contains(".act {", css, StringComparison.Ordinal);
        Assert.Contains(".act-note {", css, StringComparison.Ordinal);
        Assert.Contains(".chip-sent {", css, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ServiceWorker_ListsActJs()
    {
        string js = await Fetch("/sw.js");
        string room = await Fetch("/room.js");

        Assert.Contains("  \"/board.js\",\n  \"/act.js\",\n", room, StringComparison.Ordinal);
        Assert.Contains("const CacheVersion = \"v23\";", room, StringComparison.Ordinal);
    }

    [Fact]
    public void ClaudeMd_StatesTheWriteRuleRouteMethodRuleAndSchema()
    {
        string text = File.ReadAllText(Path.Combine(RepoFolder(), "CLAUDE.md"));

        Assert.Contains("`human.json`", text, StringComparison.Ordinal);
        Assert.Contains("`POST /api/job/act`", text, StringComparison.Ordinal);
        Assert.Contains("`/api/job/act`", text, StringComparison.Ordinal);
        Assert.Contains("{\"action\":\"approve\",\"round\":R,\"requested\":\"YYYY-MM-DDTHH:MM:SSZ\"}", text,
            StringComparison.Ordinal);
        Assert.Contains("{\"action\":\"rework\",\"round\":R,\"note\":\"…\",\"requested\":\"…\"}", text,
            StringComparison.Ordinal);
        Assert.Contains("`act.js`", text, StringComparison.Ordinal);
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
