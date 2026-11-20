"use strict";

// Backlog editing: per-project edit mode (approve, drop, move, edit title, add), the story editor and Add note. Every change
// is one POST to /api/backlog/change through authFetch with the version the view loaded. Only fixed texts are shown;
// server text never is. Uses element() and the views' reload functions from app.js at call time.

const EditTexts = {
  edit: "Edit",
  doneEditing: "Done",
  approve: "Approve",
  drop: "Drop",
  up: "Up",
  down: "Down",
  toTop: "To top",
  editTitle: "Edit title",
  save: "Save",
  cancel: "Cancel",
  add: "Add item",
  addButton: "Add",
  titleLabel: "Title",
  whatLabel: "What",
  whyLabel: "Why",
  whereLabel: "Where",
  top: "Top",
  bottom: "Bottom",
  editStory: "Edit story",
  storyLabel: "Story (Markdown)",
  addNote: "Add note",
  noteLabel: "Note",
  noteHint: "Saved as one line under Notes, with today's date.",
  dismiss: "Close",
  confirmDrop: "Drop this proposal? Its line and story file are removed in one commit.",
  saved: "Saved.",
  savedPushed: "Saved and pushed.",
};

// One fixed text per error code; anything else gets the generic text.
const ChangeErrors = {
  not_develop: "Not on develop. Nothing was changed.",
  dirty: "Backlog has uncommitted changes.",
  changed: "Backlog changed since you loaded it. Reload and try again.",
  invalid: "This change isn't allowed. Nothing was changed.",
  commit_failed: "The commit failed. Nothing was changed.",
  write_failed: "Can't write the file. Nothing was changed.",
  push_failed: "Committed, but the push failed.",
  worktree_failed: "Could not prepare the backlog branch. Nothing was changed.",
  unreachable: "Can't reach the server. Reload to see whether the change was saved.",
  notEditable: "This story can't be edited.",
  failed: "The change failed. Reload to see whether it was saved.",
};

// Projects in edit mode survive a reload of the list.
const editingProjects = new Set();
let changeBusy = false;

function showEditNotice(text, isError) {
  const notice = document.getElementById("edit-notice");
  const message = document.getElementById("edit-notice-text");
  message.textContent = text;
  notice.classList.toggle("edit-notice-error", isError === true);
  notice.hidden = false;
}

function hideEditNotice() {
  document.getElementById("edit-notice").hidden = true;
}

function editButton(label, className, onClick) {
  const button = element("button", "edit-button" + (className ? " " + className : ""), label);
  button.type = "button";
  button.addEventListener("click", (event) => {
    event.stopPropagation();
    onClick(event);
  });
  return button;
}

// Sends one change. Returns true when it was committed. Shows the fixed text for the outcome.
async function sendChange(body) {
  if (changeBusy) {
    return false;
  }
  changeBusy = true;
  document.body.classList.add("edit-busy");
  hideEditNotice();
  let response;
  let data = null;
  try {
    response = await authFetch("/api/backlog/change", {
      method: "POST",
      cache: "no-store",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(body),
    });
    try {
      data = await response.json();
    } catch {
      data = null;
    }
  } catch {
    changeBusy = false;
    document.body.classList.remove("edit-busy");
    if (!tokenViewShown()) {
      showEditNotice(ChangeErrors.unreachable, true);
    }
    return false;
  }
  changeBusy = false;
  document.body.classList.remove("edit-busy");
  if (response.ok && data && data.committed === true) {
    if (data.warning === "push_failed") {
      showEditNotice(ChangeErrors.push_failed, true);
    } else {
      showEditNotice(data.pushed === true ? EditTexts.savedPushed : EditTexts.saved, false);
    }
    return true;
  }
  const code = data && typeof data.error === "string" ? data.error : "";
  showEditNotice(Object.prototype.hasOwnProperty.call(ChangeErrors, code) ? ChangeErrors[code] : ChangeErrors.failed,
    true);
  return false;
}

async function changeAndReload(body) {
  if (await sendChange(body)) {
    load();
  }
}

// The Edit / Done toggle in a project's heading row.
function editToggle(projectName, section) {
  const button = editButton(EditTexts.edit, "edit-toggle", () => {
    const on = !editingProjects.has(projectName);
    if (on) {
      editingProjects.add(projectName);
    } else {
      editingProjects.delete(projectName);
    }
    applyEditMode(section, button, on);
  });
  applyEditMode(section, button, editingProjects.has(projectName));
  return button;
}

function applyEditMode(section, button, on) {
  section.classList.toggle("editing", on);
  button.textContent = on ? EditTexts.doneEditing : EditTexts.edit;
  button.setAttribute("aria-pressed", on ? "true" : "false");
}

// The action row of one line (shown in edit mode only), or null for a done line.
function editRow(projectName, version, entry) {
  const mark = asText(entry && entry.mark);
  const line = asNumber(entry && entry.line);
  if (mark === "x" || line === null || version === "") {
    return null;
  }
  const number = asNumber(entry.number);
  // Screen readers hear which item each button acts on, not the same names on every line.
  const subject = number !== null ? "#" + number : titleText(entry.title);
  const named = (button) => {
    button.setAttribute("aria-label", button.textContent + " " + subject);
    return button;
  };
  const row = element("div", "edit-row edit-only");
  const send = (fields) => changeAndReload(Object.assign({ project: projectName, version: version, line: line }, fields));
  if (mark === "?") {
    row.appendChild(named(editButton(EditTexts.approve, "edit-primary", () => send({ action: "approve" }))));
    row.appendChild(named(editButton(EditTexts.drop, "edit-danger", () => {
      if (confirm(EditTexts.confirmDrop)) {
        send({ action: "drop" });
      }
    })));
  }
  row.appendChild(named(editButton(EditTexts.up, null, () => send({ action: "up" }))));
  row.appendChild(named(editButton(EditTexts.down, null, () => send({ action: "down" }))));
  row.appendChild(named(editButton(EditTexts.toTop, null, () => send({ action: "top" }))));
  if (number !== null) {
    row.appendChild(named(editButton(EditTexts.editTitle, null, () => openTitleForm(row, entry, send))));
  }
  row.addEventListener("click", (event) => event.stopPropagation());
  return row;
}

function openTitleForm(row, entry, send) {
  if (row.querySelector(".edit-title-form")) {
    return;
  }
  const form = element("form", "edit-form edit-title-form");
  form.noValidate = true;
  const input = element("input", "edit-input");
  input.type = "text";
  input.maxLength = 200;
  input.value = asText(entry.title);
  input.setAttribute("aria-label", EditTexts.titleLabel);
  const actions = element("div", "edit-actions");
  const save = element("button", "edit-button edit-primary", EditTexts.save);
  save.type = "submit";
  const cancel = editButton(EditTexts.cancel, null, () => form.remove());
  actions.append(save, cancel);
  form.append(input, actions);
  form.addEventListener("submit", (event) => {
    event.preventDefault();
    send({ action: "title", title: input.value });
  });
  row.appendChild(form);
  input.focus();
}

function labelled(labelText, control) {
  const label = element("label", "edit-label");
  label.appendChild(element("span", null, labelText));
  label.appendChild(control);
  return label;
}

// The Add form of a project (edit mode only).
function addForm(projectName, version) {
  if (version === "") {
    return null;
  }
  const details = element("details", "edit-add edit-only");
  details.appendChild(element("summary", null, EditTexts.add));
  const form = element("form", "edit-form");
  form.noValidate = true;
  const title = element("input", "edit-input");
  title.type = "text";
  title.maxLength = 200;
  const what = element("textarea", "edit-input");
  what.rows = 3;
  const why = element("textarea", "edit-input");
  why.rows = 3;
  const where = element("select", "edit-input");
  for (const [value, text] of [["top", EditTexts.top], ["bottom", EditTexts.bottom]]) {
    const option = element("option", null, text);
    option.value = value;
    where.appendChild(option);
  }
  const add = element("button", "edit-button edit-primary", EditTexts.addButton);
  add.type = "submit";
  form.append(
    labelled(EditTexts.titleLabel, title),
    labelled(EditTexts.whatLabel, what),
    labelled(EditTexts.whyLabel, why),
    labelled(EditTexts.whereLabel, where),
    add);
  form.addEventListener("submit", (event) => {
    event.preventDefault();
    changeAndReload({
      project: projectName,
      version: version,
      action: "add",
      where: where.value,
      title: title.value,
      what: what.value,
      why: why.value,
    });
  });
  details.appendChild(form);
  return details;
}

// Finds the line, the backlog version and the group (open, approval or user) of a numbered item, as the server sees it now.
async function findStoryLine(project, number) {
  const response = await authFetch("/api/backlogs", { cache: "no-store" });
  if (!response.ok) {
    throw new Error("status");
  }
  const data = await response.json();
  const match = asList(data && data.projects).find((item) => asText(item && item.name) === project);
  if (!match || typeof match.version !== "string") {
    return null;
  }
  for (const group of ["open", "approval", "user"]) {
    for (const entry of asList(match[group])) {
      if (asNumber(entry && entry.number) === number && asNumber(entry.line) !== null) {
        return { version: match.version, line: entry.line, group: group };
      }
    }
  }
  return null;
}

// The Edit story button of the story view: opens a textarea with the raw Markdown, saved with storyVersion.
function storyEditButton(target, data, reload) {
  const markdown = asText(data && data.markdown);
  const storyVersion = asText(data && data.storyVersion);
  const number = asNumber(data && data.number);
  const button = editButton(EditTexts.editStory, "edit-story-button", async () => {
    const article = document.querySelector("#story .story");
    if (!article || storyFormOpen()) {
      return;
    }
    let found;
    try {
      found = await findStoryLine(target.project, number);
    } catch {
      if (!tokenViewShown()) {
        showEditNotice(ChangeErrors.unreachable, true);
      }
      return;
    }
    if (found === null || storyVersion === "") {
      showEditNotice(ChangeErrors.notEditable, true);
      return;
    }
    // A live refresh (live.js) may have replaced the story while the line was looked up.
    const shown = document.querySelector("#story .story");
    if (!shown || storyFormOpen()) {
      return;
    }
    const form = element("form", "edit-form edit-story-form");
    form.noValidate = true;
    const area = element("textarea", "edit-input edit-story-text");
    area.rows = 18;
    area.value = markdown;
    area.spellcheck = true;
    const actions = element("div", "edit-actions");
    const save = element("button", "edit-button edit-primary", EditTexts.save);
    save.type = "submit";
    const cancel = editButton(EditTexts.cancel, null, () => {
      form.replaceWith(shown);
      showStoryButtons(true);
    });
    actions.append(save, cancel);
    form.append(labelled(EditTexts.storyLabel, area), actions);
    form.addEventListener("submit", async (event) => {
      event.preventDefault();
      const ok = await sendChange({
        project: target.project,
        version: found.version,
        action: "story",
        line: found.line,
        storyVersion: storyVersion,
        markdown: area.value,
      });
      if (ok) {
        reload();
      }
    });
    shown.replaceWith(form);
    showStoryButtons(false);
    area.focus({ preventScroll: true });
  });
  if (number === null) {
    button.hidden = true;
  }
  return button;
}

// True while Edit story or Add note is open in the story view.
function storyFormOpen() {
  return document.querySelector("#story .edit-story-form, #story .add-note-form") !== null;
}

// Shows or hides the story bar buttons, so Edit story and Add note are never open at once and Approve and Drop are
// never tapped while one of them is open.
function showStoryButtons(visible) {
  for (const button of document.querySelectorAll(
    "#story .edit-story-button, #story .add-note-button, #story .story-approve-button, #story .story-drop-button")) {
    button.hidden = !visible;
  }
}

// The Add note button of the story view: a one-line note (Return is joined into the line by the server), dated by
// the server and appended under the story's Notes, saved with storyVersion.
function storyNoteButton(target, data, reload) {
  const storyVersion = asText(data && data.storyVersion);
  const number = asNumber(data && data.number);
  const button = editButton(EditTexts.addNote, "add-note-button", async () => {
    if (!document.querySelector("#story .story") || storyFormOpen()) {
      return;
    }
    let found;
    try {
      found = await findStoryLine(target.project, number);
    } catch {
      if (!tokenViewShown()) {
        showEditNotice(ChangeErrors.unreachable, true);
      }
      return;
    }
    if (found === null || storyVersion === "") {
      showEditNotice(ChangeErrors.notEditable, true);
      return;
    }
    // A live refresh (live.js) may have replaced the story while the line was looked up.
    const shown = document.querySelector("#story .story");
    if (!shown || storyFormOpen()) {
      return;
    }
    const form = element("form", "edit-form add-note-form");
    form.noValidate = true;
    const area = element("textarea", "edit-input add-note-text");
    area.rows = 4;
    area.maxLength = 1000;
    area.setAttribute("maxlength", "1000");
    area.spellcheck = true;
    const hint = element("p", "edit-hint", EditTexts.noteHint);
    hint.id = "add-note-hint";
    area.setAttribute("aria-describedby", hint.id);
    const actions = element("div", "edit-actions");
    const save = element("button", "edit-button edit-primary", EditTexts.save);
    save.type = "submit";
    const cancel = editButton(EditTexts.cancel, null, () => {
      form.replaceWith(shown);
      showStoryButtons(true);
    });
    // An empty note would only be refused, so Save waits for some text.
    const blank = () => area.value.trim() === "";
    save.disabled = true;
    area.addEventListener("input", () => {
      save.disabled = blank();
    });
    actions.append(save, cancel);
    form.append(labelled(EditTexts.noteLabel, area), hint, actions);
    form.addEventListener("submit", async (event) => {
      event.preventDefault();
      if (changeBusy || blank()) {
        return;
      }
      save.disabled = true;
      const ok = await sendChange({
        project: target.project,
        version: found.version,
        action: "note",
        line: found.line,
        storyVersion: storyVersion,
        note: area.value,
      });
      save.disabled = blank();
      if (ok) {
        reload();
      }
    });
    shown.replaceWith(form);
    showStoryButtons(false);
    area.focus({ preventScroll: true });
  });
  if (number === null) {
    button.hidden = true;
  }
  return button;
}

let storyDecisionRequest = 0;

// Approve and Drop in the story bar, only while the item's backlog line is a [?] line. Called after the story is
// shown and after a live refresh: buttons that should stay are kept as they are (focus stays on them).
async function showStoryDecision(target, data) {
  const number = asNumber(data && data.number);
  storyDecisionRequest += 1;
  const request = storyDecisionRequest;
  const story = storyRequest;
  let found = null;
  if (number !== null) {
    try {
      found = await findStoryLine(target.project, number);
    } catch {
      found = null;
    }
  }
  if (request !== storyDecisionRequest || story !== storyRequest || currentView !== "story") {
    return;
  }
  const bar = document.querySelector("#story .story-bar");
  if (bar === null) {
    return;
  }
  const shown = bar.querySelectorAll(".story-approve-button, .story-drop-button");
  if (found === null || found.group !== "approval") {
    for (const button of shown) {
      button.remove();
    }
    return;
  }
  if (shown.length === 0) {
    bar.append(...storyDecisionButtons(target, number));
  }
}

// The two buttons, named for screen readers by number; hidden while Edit story or Add note is open.
function storyDecisionButtons(target, number) {
  const subject = "#" + number;
  const approve = editButton(EditTexts.approve, "edit-primary story-approve-button", () => {
    decideStory(target, number, "approve");
  });
  approve.setAttribute("aria-label", EditTexts.approve + " " + subject);
  const drop = editButton(EditTexts.drop, "edit-danger story-drop-button", () => {
    if (confirm(EditTexts.confirmDrop)) {
      decideStory(target, number, "drop");
    }
  });
  drop.setAttribute("aria-label", EditTexts.drop + " " + subject);
  const hidden = storyFormOpen();
  approve.hidden = hidden;
  drop.hidden = hidden;
  return [approve, drop];
}

// Sends approve or drop with the version and line of the item as the server sees it at the tap, through sendChange.
// Approve stays on the story (now open, so without the buttons); Drop leaves it, as its story file is gone.
async function decideStory(target, number, action) {
  if (changeBusy || storyFormOpen()) {
    return;
  }
  let found;
  try {
    found = await findStoryLine(target.project, number);
  } catch {
    if (!tokenViewShown()) {
      showEditNotice(ChangeErrors.unreachable, true);
    }
    return;
  }
  if (found === null || found.group !== "approval") {
    showEditNotice(ChangeErrors.changed, true);
    loadStory(target);
    return;
  }
  const ok = await sendChange({ project: target.project, version: found.version, line: found.line, action: action });
  if (!ok) {
    return;
  }
  loadInbox();
  if (action === "approve") {
    loadStory(target);
    return;
  }
  leaveDroppedStory();
}

// Back to where the story was opened from, else to the inbox; the outcome notice stays for that one route.
function leaveDroppedStory() {
  keepEditNotice();
  if (openedFromList) {
    history.back();
    return;
  }
  history.replaceState(null, "", InboxRoute);
  route();
}

// A notice kept across the next route (route() calls routeEditNotice instead of hiding it).
let keptEditNotice = null;

function keepEditNotice() {
  const notice = document.getElementById("edit-notice");
  keptEditNotice = notice.hidden ? null : {
    text: document.getElementById("edit-notice-text").textContent,
    isError: notice.classList.contains("edit-notice-error"),
  };
}

function routeEditNotice() {
  const kept = keptEditNotice;
  keptEditNotice = null;
  if (kept !== null) {
    showEditNotice(kept.text, kept.isError);
  } else {
    hideEditNotice();
  }
}

document.getElementById("edit-notice-close").addEventListener("click", hideEditNotice);
