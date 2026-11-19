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
  noteHint: "Sent back to the builder as one line, at most 1000 characters.",
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

let actBusy = false;
// A text shown once in the next section that is built (after a cancel).
let actNotice = "";

function actNote() {
  return document.getElementById("act-note");
}

// True while a request is being sent, or while the note box has text or focus: a live refresh must not rebuild the
// section then.
function jobDecisionBusy() {
  if (actBusy) {
    return true;
  }
  const note = actNote();
  return note !== null && (note.value.trim() !== "" || document.activeElement === note);
}

function actLocalTime(text) {
  const date = new Date(text);
  if (Number.isNaN(date.getTime())) {
    return "";
  }
  return date.toLocaleString(undefined, { dateStyle: "medium", timeStyle: "short" });
}

function showActStatus(text, error) {
  const status = document.getElementById("act-status");
  if (!status) {
    return;
  }
  status.textContent = text;
  status.className = error ? "error act-status" : "status act-status";
  status.hidden = text === "";
}

function setActButtons(disabled) {
  for (const button of document.querySelectorAll("#act button")) {
    button.disabled = disabled || (button.id === "act-send" && actNote() !== null && actNote().value.trim() === "");
  }
}

// Sends one action for the open job, then reloads the page so the section shows what the server holds now.
async function sendJobAction(job, body) {
  if (actBusy) {
    return;
  }
  actBusy = true;
  setActButtons(true);
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
    try {
      data = await response.json();
    } catch {
      data = null;
    }
  } catch {
    actBusy = false;
    setActButtons(false);
    if (!tokenViewShown()) {
      showActStatus(ActErrors.unreachable, true);
    }
    return;
  }
  actBusy = false;
  if (response.ok && data && typeof data === "object" && "request" in data) {
    actNotice = data.request === null ? ActTexts.cancelled : "";
    const note = actNote();
    if (note) {
      note.value = "";
      note.blur();
    }
    loadJob(asText(job.id), true);
    pollBoard();
    return;
  }
  setActButtons(false);
  const code = data && typeof data.error === "string" ? data.error : "";
  showActStatus(Object.prototype.hasOwnProperty.call(ActErrors, code) ? ActErrors[code] : ActErrors.failed, true);
}

function actButton(text, className, onClick) {
  const button = element("button", "edit-button " + className, text);
  button.type = "button";
  button.addEventListener("click", onClick);
  return button;
}

// The pending request: the fixed waiting text, the note as text, and Cancel request.
function renderActPending(section, job, request) {
  const project = asText(job.project) || ActTexts.thisProject;
  const action = asText(request.action);
  if (action === "approve" || action === "rework") {
    const label = action === "approve" ? ActTexts.approveSent : ActTexts.reworkSent;
    section.appendChild(element("p", "act-waiting",
      ActTexts.waitingStart + project + ": " + label + ActTexts.sent + actLocalTime(asText(request.requested))));
    const note = asText(request.note);
    if (action === "rework" && note) {
      section.appendChild(element("p", "act-note-text", note));
    }
  } else {
    section.appendChild(element("p", "error act-waiting", ActTexts.unreadable));
  }
  const row = element("div", "act-row");
  row.appendChild(actButton(ActTexts.cancel, "edit-danger act-cancel", () => sendJobAction(job, { action: "cancel" })));
  section.appendChild(row);
}

// Approve, and Rework with its note box and Send (off while the box is blank).
function renderActChoices(section, job) {
  const project = asText(job.project) || ActTexts.thisProject;
  section.appendChild(element("p", "act-hint", ActTexts.hintStart + project + ActTexts.hintEnd));
  const approveRow = element("div", "act-row");
  approveRow.appendChild(actButton(ActTexts.approve, "edit-primary act-approve", () => {
    if (window.confirm(ActTexts.confirmApprove)) {
      sendJobAction(job, { action: "approve" });
    }
  }));
  section.appendChild(approveRow);

  const label = element("label", "act-label", ActTexts.noteLabel);
  label.htmlFor = "act-note";
  const note = element("textarea", "act-note");
  note.id = "act-note";
  note.rows = 3;
  note.maxLength = ActNoteMaxLength;
  note.setAttribute("maxlength", "1000");
  note.spellcheck = true;
  note.setAttribute("autocapitalize", "sentences");
  const hint = element("p", "act-hint", ActTexts.noteHint);
  hint.id = "act-note-hint";
  note.setAttribute("aria-describedby", hint.id);
  const send = actButton(ActTexts.send, "act-send", () => {
    const text = note.value.trim();
    if (text !== "") {
      sendJobAction(job, { action: "rework", note: text });
    }
  });
  send.id = "act-send";
  send.disabled = true;
  note.addEventListener("input", () => {
    send.disabled = actBusy || note.value.trim() === "";
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
  const section = element("section", "act");
  section.id = "act";
  section.setAttribute("aria-labelledby", "act-heading");
  const heading = element("h2", "act-heading", ActTexts.heading);
  heading.id = "act-heading";
  section.appendChild(heading);
  const request = data.request && typeof data.request === "object" ? data.request : null;
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
  if (actNotice !== "") {
    status.textContent = actNotice;
    status.hidden = false;
    actNotice = "";
  }
  // A refresh held back while the note box had text or focus runs once focus leaves the section with the box blank.
  section.addEventListener("focusout", (event) => {
    if (section.contains(event.relatedTarget)) {
      return;
    }
    releaseHeldJobRefresh();
  });
  return section;
}
