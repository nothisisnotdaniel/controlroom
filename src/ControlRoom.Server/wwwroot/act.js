"use strict";

// The "Your decision" section on the page of a needs-human job. Loaded after board.js; board.js calls
// renderJobDecision while it builds the job page and jobDecisionBusy before a live refresh rebuilds it.
// Approve and Rework only ask the server to write the job's human.json; nothing happens until the next /factory
// session for the project runs and picks it up. Everything is built with createElement and textContent; fixed texts
// only, never server or exception text.

const ActTexts = {
  heading: "Your decision",
  hintStart: "Nothing happens until the next /factory session for ",
  hintEnd: " runs. It then approves the job or sends it back with your note.",
  approve: "Approve",
  confirmApprove: "Approve this job? The next /factory session for this project may merge it into develop.",
  noteLabel: "Rework note",
  noteHint: "Sent back to the builder as one line, at most 1,000 characters.",
  noteTooLong: "Shorten your note to 1,000 characters or fewer.",
  send: "Send",
  waitingStart: "Waiting for the next /factory session for ",
  approveSent: "Approve",
  reworkSent: "Rework",
  sent: " sent ",
  unreadable: "A request file for this job is there, but it can't be read.",
  cancel: "Cancel request",
  cancelled: "Request cancelled.",
  thisProject: "this project",
};

// One fixed text per error code; anything else gets the generic text.
const ActErrors = {
  invalid: "The note can't be sent. Write one line of text, at most 1000 characters.",
  "not found": "This job is gone.",
  not_waiting: "This job no longer waits for you.",
  changed: "The job changed since this page was loaded. Look again, then decide.",
  pending: "A request for this job is already waiting.",
  not_pending: "There is no request to cancel.",
  write_failed: "Can't write the request file on the Mac.",
  unreachable: "Can't reach the server. Try again.",
  failed: "Something went wrong. Try again.",
};

const ActRoute = "/api/job/act";
const ActNoteMaxLength = 1000;
const decisions = new WeakMap();

let activeAction = null;
// Keep a same-round operation notice visible through its detail refresh.
let actNotice = null;

function actNote() {
  return document.getElementById("act-note");
}

// Count a whitespace run only when another scalar follows it, as in BacklogEditor.CleanNote.
function actNoteState(value) {
  let text = "";
  let length = 0;
  let separator = "";
  for (const scalar of value) {
    if (/\p{White_Space}/u.test(scalar)) {
      separator = length === 0 ? "" : " ";
      continue;
    }
    length += separator.length + 1;
    if (length > ActNoteMaxLength) {
      return { text: null, tooLong: true, disabled: true };
    }
    text += separator + scalar;
    separator = "";
  }
  return { text, tooLong: false, disabled: activeAction !== null || length === 0 };
}

function sameActRequest(a, b) {
  return a === b || (a && b && a.action === b.action && a.round === b.round &&
    a.requested === b.requested && a.note === b.note);
}

function sameDecisionRound(a, b) {
  return a && b && a.id === b.id && a.stage === b.stage && a.round === b.round;
}

function boardMatchesDecision(entry, job, request) {
  return sameDecisionRound(entry, job) && (entry.request ?? null) === (request?.action ?? null);
}

// A summary cannot identify a pending file. Keep the last full note and the focused button in place
// while checking. Only a detail read begun after the conflicting observations can release Cancel.
function setDecisionChecking(section, checking, holdForm = false) {
  const current = decisions.get(section);
  current.checking = checking;
  const staleRound = current.board !== undefined && !sameDecisionRound(current.board, current.job);
  const hold = checking && (current.request !== null || staleRound || holdForm);
  for (const button of section.querySelectorAll("button")) {
    if (hold) {
      button.setAttribute("aria-disabled", "true");
    } else {
      button.removeAttribute("aria-disabled");
    }
  }
  section.classList.toggle("edit-busy", hold);
}

function observeDecisionBoard(entry) {
  const section = document.getElementById("act");
  const current = decisions.get(section);
  if (!current) {
    return false;
  }
  const { job, request } = current.detail;
  const conflict = !boardMatchesDecision(entry, job, request);
  if (conflict || current.checking || activeAction) {
    current.board = entry;
  }
  if (conflict) {
    const note = actNote();
    setDecisionChecking(section, true, note && !note.value && document.activeElement !== note);
  }
  return conflict;
}

function decisionDetailFailed() {
  if (openingJob) {
    openingJob.needsCheck = true;
  }
  const section = document.getElementById("act");
  if (section) {
    setDecisionChecking(section, true);
  }
}

// Two different requests can have the same board entry. Keep the focused button in place but inert
// until the detail confirms it. A failed GET leaves it marked for the next visible live check.
function recheckPendingCancel(jobsChanged, { includeForm = false } = {}) {
  if (jobsChanged && activeAction) {
    activeAction.needsCheck = true;
  }
  if (openJobId === null) {
    return;
  }
  if (openingJob?.loaded && openJobEntry === null) {
    openingJob.needsCheck ||= jobsChanged;
    return;
  }
  const section = document.getElementById("act");
  const current = decisions.get(section);
  const verifyDetail = jobsChanged && (jobRequest !== null || includeForm || current?.request);
  if (!openingJob?.needsCheck && !current?.checking && !verifyDetail) {
    return;
  }
  if (current) {
    setDecisionChecking(section, true, includeForm && !actNote()?.value);
  }
  return loadJob(openJobId, true);
}

// Accept a full detail even when a draft, keyboard focus or a POST holds the rest of the page.
function jobDecisionBusy(job, request) {
  const section = document.getElementById("act");
  const current = decisions.get(section);
  if (current && request !== undefined) {
    current.detail = { job, request };
    current.board = undefined;
    if (!sameDecisionRound(job, current.job)) {
      return false;
    }
    if (sameActRequest(current.request, request)) {
      setDecisionChecking(section, false);
      const cancel = section.querySelector(".act-cancel");
      if (cancel && document.activeElement === cancel) {
        return true;
      }
    } else if (activeAction && (current.request || !actNote()?.value)) {
      section.replaceWith(renderJobDecision({ job, request }));
    } else {
      setDecisionChecking(section, false);
    }
  }
  const note = actNote();
  return activeAction !== null ||
    (note !== null && (document.activeElement === note || /\P{White_Space}/u.test(note.value)));
}

// A draft can stay on screen after detail has identified another tab's request. A reread of that
// request cannot supersede Send. Keep Cancel under verification when the read and POST disagree.
function applyDecisionResult(section, current, submitted, request) {
  const observed = current.detail.request;
  const concurrentDetail = current.detail !== submitted.detail && !sameActRequest(observed, request);
  const replacement = concurrentDetail && observed !== null && !sameActRequest(observed, submitted.detail.request);
  const boardConflict = current.board !== undefined && !boardMatchesDecision(current.board, current.job, request);
  const preserveObserved = replacement || (request === null && boardConflict &&
    !boardMatchesDecision(current.board, submitted.job, submitted.request));
  const pending = preserveObserved ? observed : request;
  if (!sameActRequest(current.request, pending)) {
    const next = renderJobDecision({ job: current.job, request: pending });
    section.replaceWith(next);
    section = next;
  }
  const displayed = decisions.get(section);
  displayed.detail = { job: current.job, request: pending };
  if (submitted.needsCheck || current.checking || concurrentDetail || boardConflict) {
    displayed.board = current.board;
    setDecisionChecking(section, true, boardConflict && !pending && !actNote()?.value);
  }
  return !preserveObserved;
}

function actLocalTime(text) {
  const date = new Date(text);
  if (Number.isNaN(date.getTime())) {
    return "";
  }
  return date.toLocaleString(undefined, { dateStyle: "medium", timeStyle: "short" });
}

function showActStatus(text, error, status = document.getElementById("act-status")) {
  if (!status) {
    return;
  }
  status.textContent = text;
  status.className = error ? "error act-status" : "status act-status";
  status.hidden = text === "";
}

function setActButtons() {
  const note = actNote();
  if (note) {
    note.readOnly = activeAction !== null;
  }
  for (const button of document.querySelectorAll("#act button")) {
    button.disabled = button.id === "act-send" ? actNoteState(note.value).disabled : activeAction !== null;
  }
}

// Sends one action for the open job, then reloads the page so the section shows what the server holds now.
async function sendJobAction(job, body) {
  if (activeAction) {
    return;
  }
  const section = document.getElementById("act");
  const initial = decisions.get(section);
  const submitted = { job, request: initial.request, detail: initial.detail, needsCheck: false };
  if (!initial.checking) {
    initial.board = undefined;
  }
  actNotice = null;
  activeAction = submitted;
  setActButtons();
  showActStatus("", false);
  let response;
  let data = null;
  try {
    response = await authFetch(ActRoute, {
      method: "POST",
      cache: "no-store",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(Object.assign({ id: asText(job.id), round: asNumber(job.round) || 0 }, body)),
    });
    data = await response.json();
  } catch {
    // A missing response means the server was unreachable. An unreadable response keeps the generic error.
  }
  const opening = openingJob;
  if (!section.isConnected && openJobId === job.id && openJobEntry === null) {
    await opening?.loaded;
  }
  activeAction = null;
  if (openJobId !== job.id || openingJob !== opening) {
    setActButtons();
    pollBoard();
    return;
  }
  const active = document.getElementById("act");
  const current = decisions.get(active);
  const detailJob = current?.detail.job;
  if (detailJob?.id !== job.id || detailJob.round !== job.round || detailJob.stage !== "needs-human") {
    if (current || openJobEntry === null) {
      loadJob(asText(job.id), true);
    }
    setActButtons();
    pollBoard();
    return;
  }
  const notPending = body.action === "cancel" && response?.status === 409 && data?.error === "not_pending";
  const confirmed = response?.ok && (body.action === "cancel" ? data?.request === null :
    data?.request?.action === body.action);
  if (notPending || confirmed) {
    const applied = applyDecisionResult(active, current, submitted, notPending ? null : data.request);
    if (notPending || (applied && body.action === "cancel")) {
      const text = notPending ? ActErrors.not_pending : ActTexts.cancelled;
      showActStatus(text, notPending);
      actNotice = { job, text, error: notPending };
    }
    setActButtons();
    loadJob(asText(job.id), true);
    pollBoard();
    return;
  }
  // A failed Cancel response can follow a successful deletion. Verify the file before allowing another Cancel.
  const refused = response?.status === 409 || response?.status === 404;
  if (body.action === "cancel" || (current.request && (refused || !section.isConnected))) {
    setDecisionChecking(active, true);
  }
  setActButtons();
  if (current.checking || !section.isConnected) {
    loadJob(asText(job.id), true);
  }
  if (!section.isConnected) {
    pollBoard();
  }
  if (!response && tokenViewShown()) {
    return;
  }
  const code = response ? data?.error : "unreachable";
  const text = typeof code === "string" &&
    Object.prototype.hasOwnProperty.call(ActErrors, code) ? ActErrors[code] : ActErrors.failed;
  showActStatus(text, true);
  if (current.checking || !section.isConnected) {
    actNotice = { job, text, error: true };
  }
}

function actButton(text, className, onClick) {
  const button = element("button", "edit-button " + className, text);
  button.type = "button";
  button.addEventListener("click", () => {
    if (button.isConnected && !button.disabled && button.getAttribute("aria-disabled") !== "true") {
      onClick();
    }
  });
  return button;
}

// The pending request: the fixed waiting text, the note as text, and Cancel request.
function renderActPending(section, job, request) {
  const project = asText(job.project) || ActTexts.thisProject;
  const action = asText(request.action);
  if (action === "approve" || action === "rework") {
    const label = action === "approve" ? ActTexts.approveSent : ActTexts.reworkSent;
    const when = actLocalTime(asText(request.requested));
    section.appendChild(element("p", "act-waiting",
      ActTexts.waitingStart + project + ": " + label + (when ? ActTexts.sent + when : "")));
    const note = asText(request.note);
    if (action === "rework" && note) {
      section.appendChild(element("p", "act-note-text", note));
    }
  } else {
    section.appendChild(element("p", "error act-waiting", ActTexts.unreadable));
  }
  const row = element("div", "act-row");
  const cancel = actButton(ActTexts.cancel, "edit-danger act-cancel", () => sendJobAction(job, { action: "cancel" }));
  cancel.disabled = activeAction !== null;
  row.appendChild(cancel);
  section.appendChild(row);
}

// Approve, and Rework with inline validation that leaves the draft in the note box.
function renderActChoices(section, job) {
  const project = asText(job.project) || ActTexts.thisProject;
  section.appendChild(element("p", "act-hint", ActTexts.hintStart + project + ActTexts.hintEnd));
  const approveRow = element("div", "act-row");
  const approve = actButton(ActTexts.approve, "edit-primary act-approve", () => {
    if (window.confirm(ActTexts.confirmApprove)) {
      sendJobAction(job, { action: "approve" });
    }
  });
  approve.disabled = activeAction !== null;
  approveRow.appendChild(approve);
  section.appendChild(approveRow);

  const label = element("label", "act-label", ActTexts.noteLabel);
  label.htmlFor = "act-note";
  const note = element("textarea", "act-note");
  note.id = "act-note";
  note.readOnly = activeAction !== null;
  note.rows = 3;
  note.spellcheck = true;
  note.setAttribute("autocapitalize", "sentences");
  const hint = element("p", "act-hint", ActTexts.noteHint);
  hint.id = "act-note-hint";
  hint.setAttribute("role", "status");
  hint.setAttribute("aria-live", "polite");
  hint.setAttribute("aria-atomic", "true");
  note.setAttribute("aria-describedby", hint.id);
  const send = actButton(ActTexts.send, "act-send", () => {
    const state = actNoteState(note.value);
    if (!state.disabled) {
      sendJobAction(job, { action: "rework", note: state.text });
    }
  });
  send.id = "act-send";
  send.disabled = true;
  note.addEventListener("input", () => {
    const state = actNoteState(note.value);
    send.disabled = state.disabled;
    if (state.tooLong !== (note.getAttribute("aria-invalid") === "true")) {
      note.setAttribute("aria-invalid", String(state.tooLong));
      hint.className = state.tooLong ? "error act-status" : "act-hint";
      hint.textContent = state.tooLong ? ActTexts.noteTooLong : ActTexts.noteHint;
    }
  });
  const sendRow = element("div", "act-row");
  sendRow.appendChild(send);
  section.append(label, note, hint, sendRow);
}

// The section for a needs-human job, or null for any other stage.
function renderJobDecision(data) {
  const job = data && data.job && typeof data.job === "object" ? data.job : {};
  if (asText(job.stage) !== "needs-human") {
    return null;
  }
  const request = data.request && typeof data.request === "object" ? data.request : null;
  const existing = document.getElementById("act");
  const current = decisions.get(existing);
  // A matching detail can refresh the rest of the page without replacing its pending Cancel control.
  if (request && current && current.job.id === job.id && current.job.round === job.round &&
      current.job.project === job.project && sameActRequest(current.request, request)) {
    actNotice = null;
    return existing;
  }
  const section = element("section", "act");
  section.id = "act";
  section.setAttribute("aria-labelledby", "act-heading");
  const heading = element("h2", "act-heading", ActTexts.heading);
  heading.id = "act-heading";
  section.appendChild(heading);
  decisions.set(section, { job, request, detail: { job, request }, checking: false });
  if (request) {
    renderActPending(section, job, request);
  } else {
    renderActChoices(section, job);
  }
  const status = element("p", "status act-status");
  status.id = "act-status";
  status.setAttribute("role", "status");
  status.setAttribute("aria-live", "polite");
  status.hidden = true;
  section.appendChild(status);
  if (actNotice && actNotice.job.id === job.id && actNotice.job.round === job.round && (!request || actNotice.error)) {
    showActStatus(actNotice.text, actNotice.error, status);
  }
  actNotice = null;
  // Leaving the decision section releases a refresh held for keyboard focus or a draft.
  section.addEventListener("focusout", (event) => {
    if (section.contains(event.relatedTarget)) {
      return;
    }
    releaseHeldJobRefresh();
  });
  return section;
}
