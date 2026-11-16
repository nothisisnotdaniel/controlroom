"use strict";

// Backlog editing: per-project edit mode (approve, drop, move, edit title, add) and the story editor. Every change
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
  const row = element("div", "edit-row edit-only");
  const send = (fields) => changeAndReload(Object.assign({ project: projectName, version: version, line: line }, fields));
  if (mark === "?") {
    row.appendChild(editButton(EditTexts.approve, "edit-primary", () => send({ action: "approve" })));
    row.appendChild(editButton(EditTexts.drop, "edit-danger", () => {
      if (confirm(EditTexts.confirmDrop)) {
        send({ action: "drop" });
      }
    }));
  }
  row.appendChild(editButton(EditTexts.up, null, () => send({ action: "up" })));
  row.appendChild(editButton(EditTexts.down, null, () => send({ action: "down" })));
  row.appendChild(editButton(EditTexts.toTop, null, () => send({ action: "top" })));
  if (number !== null) {
    row.appendChild(editButton(EditTexts.editTitle, null, () => openTitleForm(row, entry, send)));
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

// Finds the line and the backlog version of a numbered item, as the server sees it now.
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
        return { version: match.version, line: entry.line };
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
    if (!article || document.querySelector("#story .edit-story-form")) {
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
      form.replaceWith(article);
      button.hidden = false;
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
    article.replaceWith(form);
    button.hidden = true;
    area.focus({ preventScroll: true });
  });
  if (number === null) {
    button.hidden = true;
  }
  return button;
}

document.getElementById("edit-notice-close").addEventListener("click", hideEditNotice);
