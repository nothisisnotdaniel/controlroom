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
        Assert.Contains("note.setAttribute(\"aria-describedby\", hint.id + \" \" + over.id);", choices, StringComparison.Ordinal);
        Assert.Contains("hint.setAttribute(\"role\", \"status\");", choices, StringComparison.Ordinal);
        Assert.Contains("hint.setAttribute(\"aria-live\", \"polite\");", choices, StringComparison.Ordinal);
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
        Assert.Contains("if (length <= ActNoteMaxLength) {\n      text += separator + scalar;\n    }", state,
            StringComparison.Ordinal);
        Assert.Contains("separator = length === 0 ? \"\" : \" \";", state, StringComparison.Ordinal);
        Assert.Contains("if (length > ActNoteMaxLength) {\n"
            + "    return { text: null, tooLong: true, over: length - ActNoteMaxLength, disabled: true };",
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
        string load = Between(js, "async function loadJob(id, refresh, entryText = null) {", "\n}\n");
        string act = await Fetch("/act.js");
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
        Assert.Contains("const CacheVersion = \"v27\";", room, StringComparison.Ordinal);
        Assert.Contains("  \"/app.css\",\n  \"/room.css\",\n", room, StringComparison.Ordinal);
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

    [Fact]
    public async Task AppJs_RegistersTheTwoRoomKitHooksAndNoAppFileReplacesARoomKitGlobal()
    {
        string app = await Fetch("/app.js");
        string html = await Fetch("/index.html");

        Assert.Contains("roomKitHooks.on(\"jobCounts\", (jobs) => {\n  refreshOpenJob(jobs);\n});", app, StringComparison.Ordinal);
        Assert.Contains("roomKitHooks.on(\"liveChecked\", ({ jobsChanged, recovered, firstCheck }) =>\n"
            + "  recheckPendingCancel(jobsChanged || recovered || firstCheck, { includeForm: firstCheck }));", app,
            StringComparison.Ordinal);
        // Registered at load time, before the first counts read and live check start.
        int hooks = app.IndexOf("roomKitHooks.on(\"liveChecked\"", StringComparison.Ordinal);
        Assert.True(hooks < app.LastIndexOf("\nloadJobCounts();", StringComparison.Ordinal));
        Assert.True(hooks < app.IndexOf("\nstartLiveChecks();", StringComparison.Ordinal));
        int counts = html.IndexOf("<script src=\"/counts.js\" defer></script>", StringComparison.Ordinal);
        int live = html.IndexOf("<script src=\"/live.js\" defer></script>", StringComparison.Ordinal);
        int appScript = html.IndexOf("<script src=\"/app.js\" defer></script>", StringComparison.Ordinal);
        Assert.True(counts >= 0 && live > counts && appScript > live);

        foreach (string file in Directory.GetFiles(Path.Combine(AccessTokenTests.SourceFolder(), "wwwroot"), "*.*",
                     SearchOption.AllDirectories).Where(file => file.EndsWith(".js", StringComparison.Ordinal)
                     || file.EndsWith(".html", StringComparison.Ordinal)))
        {
            string text = File.ReadAllText(file);
            foreach (string name in new[] { "roomKitHooks", "loadJobCounts", "checkChanges" })
            {
                Assert.DoesNotMatch(@"\b(const|let|var|function|class)\s+" + name + @"\b", text);
                Assert.DoesNotMatch(@"(?<![.\w])" + name + @"\s*=(?!=)", text);
                Assert.DoesNotMatch(@"\b(window|globalThis|self)\." + name + @"\s*=(?!=)", text);
            }
        }
    }

    [Fact]
    public async Task ActJs_OverTheLimitKeepsTheDraftAnnouncesOnceAndShowsHowFarOver()
    {
        string js = await Fetch("/act.js");
        string choices = Between(js, "function renderActChoices(section, job) {", "\n}\n");
        string input = Between(choices, "note.addEventListener(\"input\", () => {", "\n  });\n");

        Assert.Contains("  over: \" over\",\n", js, StringComparison.Ordinal);
        Assert.Contains("const over = element(\"p\", \"act-hint act-over\", \"\");", choices, StringComparison.Ordinal);
        Assert.Contains("over.hidden = true;", choices, StringComparison.Ordinal);
        // The count is not a live region; only the hint is.
        Assert.DoesNotContain("over.setAttribute(\"role\"", choices, StringComparison.Ordinal);
        Assert.DoesNotContain("over.setAttribute(\"aria-live\"", choices, StringComparison.Ordinal);
        Assert.Contains("over.textContent = state.tooLong ? state.over.toLocaleString(\"en-US\") + ActTexts.over : \"\";",
            input, StringComparison.Ordinal);
        Assert.Contains("over.hidden = !state.tooLong;", input, StringComparison.Ordinal);
        // The hint text changes only when the note crosses the limit.
        string crossing = input[input.IndexOf("if (state.tooLong !== (note.getAttribute(\"aria-invalid\") === \"true\")) {",
            StringComparison.Ordinal)..];
        Assert.Contains("if (state.tooLong !== (note.getAttribute(\"aria-invalid\") === \"true\")) {", crossing, StringComparison.Ordinal);
        Assert.Contains("note.setAttribute(\"aria-invalid\", \"true\");", crossing, StringComparison.Ordinal);
        Assert.Contains("note.removeAttribute(\"aria-invalid\");", crossing, StringComparison.Ordinal);
        Assert.Contains("hint.className = state.tooLong ? \"act-hint act-hint-error\" : \"act-hint\";", crossing,
            StringComparison.Ordinal);
        Assert.Contains("hint.textContent = state.tooLong ? ActTexts.noteTooLong : ActTexts.noteHint;", crossing,
            StringComparison.Ordinal);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(input, "hint.textContent ="));
        Assert.Contains("section.append(label, note, hint, over, sendRow);", choices, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RoomCss_StylesTheInvalidAndReadOnlyNoteAndIsLinkedAfterAppCss()
    {
        string css = await Fetch("/room.css");
        string html = await Fetch("/index.html");
        string room = await Fetch("/room.js");

        Assert.Contains(".act-note[aria-invalid=\"true\"] {\n  border-color: var(--error);\n}", css, StringComparison.Ordinal);
        Assert.Contains(".act-note:read-only {\n  color: var(--muted);", css, StringComparison.Ordinal);
        Assert.Contains(".act-hint.act-hint-error,\n.act-over {\n  color: var(--error);\n}", css, StringComparison.Ordinal);
        Assert.DoesNotContain("font-size", css, StringComparison.Ordinal);
        Assert.Contains("<link rel=\"stylesheet\" href=\"/app.css\">\n  <link rel=\"stylesheet\" href=\"/room.css\">", html,
            StringComparison.Ordinal);
        Assert.Contains("\"/room.css\",", room, StringComparison.Ordinal);
        Assert.DoesNotContain("style=", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ActJs_HeldButtonsSayWhyWhileCheckingAndAfterAFailedCheck()
    {
        string js = await Fetch("/act.js");
        string texts = Between(js, "const ActTexts = {", "\n};\n") + "\n";
        string checking = Between(js, "function setDecisionChecking(section, checking, holdForm = false, failed = false) {", "\n}\n");
        string failed = Between(js, "function decisionDetailFailed() {", "\n}\n");

        Assert.Contains("  checking: \"Checking the current request…\",\n", texts, StringComparison.Ordinal);
        Assert.Contains("  checkFailed: \"Could not check the request. Retrying.\",\n", texts, StringComparison.Ordinal);
        Assert.Contains("button.setAttribute(\"aria-disabled\", \"true\");", checking, StringComparison.Ordinal);
        Assert.Contains("current.checkFailed = checking && failed;", checking, StringComparison.Ordinal);
        Assert.Contains("current.holdText = !hold ? \"\" : current.checkFailed ? ActTexts.checkFailed : ActTexts.checking;",
            checking, StringComparison.Ordinal);
        Assert.Contains("renderActStatus(status, current);", checking, StringComparison.Ordinal);
        string render = Between(js, "function renderActStatus(status, current) {", "\n}\n");
        Assert.Contains("const text = notice.text && hold ? notice.text + \" \" + hold : notice.text || hold;", render,
            StringComparison.Ordinal);
        Assert.Contains("if (status.textContent !== text) {", render, StringComparison.Ordinal);
        Assert.Contains("notice.error || hold === ActTexts.checkFailed ? \"error act-status\" : \"status act-status\"", render,
            StringComparison.Ordinal);
        Assert.Contains("setDecisionChecking(section, true, false, true);", failed, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ActJs_SaysWhenAnotherRequestReplacedTheOneJustSent()
    {
        string js = await Fetch("/act.js");
        string send = Between(js, "async function sendJobAction(job, body) {", "\n}\n");
        string apply = Between(js, "function applyDecisionResult(section, current, submitted, request) {", "\n}\n");

        Assert.Contains("  replaced: \"Another request replaced yours.\",\n", js, StringComparison.Ordinal);
        Assert.Contains("return !preserveObserved;", apply, StringComparison.Ordinal);
        Assert.Contains("if (!applied && body.action !== \"cancel\") {\n"
            + "      // Another tab's request is kept instead of the Approve or Rework just sent.\n"
            + "      showActStatus(ActTexts.replaced, true);", send, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BoardJs_StartsNoSecondDetailGetForTheSameEntryAndTimesItOut()
    {
        string js = await Fetch("/board.js");
        string refresh = Between(js, "function refreshOpenJob(jobs) {", "\n}\n");
        string load = Between(js, "async function loadJob(id, refresh, entryText = null) {", "\n}\n");

        Assert.Contains("const JobDetailTimeout = 10000;", js, StringComparison.Ordinal);
        Assert.Contains("signal: AbortSignal.timeout(JobDetailTimeout)", load, StringComparison.Ordinal);
        Assert.Contains("const request = { boards: [], entry: entryText };", load, StringComparison.Ordinal);
        Assert.Contains("const entryText = JSON.stringify(entry);\n    if (jobRequest !== null && jobRequest.entry === entryText) {\n"
            + "      return;\n    }\n    loadJob(openJobId, true, entryText);", refresh, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ActJs_CommentsTheDecisionStates()
    {
        string js = await Fetch("/act.js");
        string block = Between(js, "// Decision states:", "\nconst decisions = new WeakMap();");

        foreach (string state in new[] { "// - form:", "// - pending:", "// - sending:", "// - checking:", "// - check-failed:" })
        {
            Assert.Contains(state, block, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ClaudeMd_DescribesTheReworkNoteRulesHooksAndPin()
    {
        string text = File.ReadAllText(Path.Combine(RepoFolder(), "CLAUDE.md"));

        Assert.DoesNotContain("(`maxlength` 1000)", text, StringComparison.Ordinal);
        Assert.Contains("Version=\"0.2.0\"", text, StringComparison.Ordinal);
        Assert.Contains("roomKitHooks.on(\"jobCounts\"", text, StringComparison.Ordinal);
        Assert.Contains("roomKitHooks.on(\"liveChecked\"", text, StringComparison.Ordinal);
        Assert.Contains("`room.css`", text, StringComparison.Ordinal);
        Assert.Contains("Shorten your note to 1,000 characters or fewer.", text, StringComparison.Ordinal);
        Assert.Contains("`N over`", text, StringComparison.Ordinal);
        Assert.Contains("Checking the current request…", text, StringComparison.Ordinal);
        Assert.Contains("Could not check the request. Retrying.", text, StringComparison.Ordinal);
        Assert.Contains("Another request replaced yours.", text, StringComparison.Ordinal);
        Assert.Contains("`JobDetailTimeout`", text, StringComparison.Ordinal);
        Assert.Contains("Cancel sends only `id`, `round` and `action`", text, StringComparison.Ordinal);
        Assert.Contains("not covered", text, StringComparison.Ordinal);
        Assert.Contains("Browser/rework-submission.html", text, StringComparison.Ordinal);
        Assert.Contains("`#result`", text, StringComparison.Ordinal);
    }

    [Fact]
    public void BrowserFixture_LoadsOnlyTheAppsBoardAndActScripts()
    {
        string repo = RepoFolder();
        string fixtureFolder = Path.Combine(repo, "tests", "ControlRoom.Server.Tests", "Browser");
        string html = File.ReadAllText(Path.Combine(fixtureFolder, "rework-submission.html"));
        string[] sources = [.. System.Text.RegularExpressions.Regex.Matches(html, "src=\"([^\"]*)\"")
            .Select(match => match.Groups[1].Value)];

        Assert.Equal(["../../../src/ControlRoom.Server/wwwroot/board.js", "../../../src/ControlRoom.Server/wwwroot/act.js"],
            sources);
        foreach (string source in sources)
        {
            Assert.DoesNotContain("src/RoomKit/", source, StringComparison.Ordinal);
            Assert.True(File.Exists(Path.GetFullPath(Path.Combine(fixtureFolder, source))), source);
        }

        // The hooks app.js registers, called directly instead of loading RoomKit's counts.js and live.js.
        string app = Squash(File.ReadAllText(Path.Combine(AccessTokenTests.SourceFolder(), "wwwroot", "app.js")));
        string page = Squash(html);
        const string JobCounts = "(jobs) => { refreshOpenJob(jobs); }";
        const string LiveChecked = "({ jobsChanged, recovered, firstCheck }) => "
            + "recheckPendingCancel(jobsChanged || recovered || firstCheck, { includeForm: firstCheck })";
        Assert.Contains("roomKitHooks.on(\"jobCounts\", " + JobCounts + ");", app, StringComparison.Ordinal);
        Assert.Contains("roomKitHooks.on(\"liveChecked\", " + LiveChecked + ");", app, StringComparison.Ordinal);
        Assert.Contains("jobCounts: " + JobCounts + ",", page, StringComparison.Ordinal);
        Assert.Contains("liveChecked: " + LiveChecked + ",", page, StringComparison.Ordinal);
        Assert.Contains("await hostHooks.jobCounts(jobs);", page, StringComparison.Ordinal);
        Assert.Contains("await hostHooks.liveChecked({", page, StringComparison.Ordinal);
        Assert.DoesNotContain("roomKitHooks", html, StringComparison.Ordinal);
    }

    private static string Squash(string text)
    {
        return System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ");
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
