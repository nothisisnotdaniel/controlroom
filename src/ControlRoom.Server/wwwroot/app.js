"use strict";

// Fixed texts: never show server or exception text.
const Texts = {
  unreachable: "Can't reach the server.",
  unreadable: "Can't read backlog.md. Reload to try again.",
  noTitle: "(no title)",
  noLines: "No backlog lines.",
  noProjects: "No projects found.",
  storyFailed: "Can't load this story.",
  loading: "Loading…",
  back: "Back",
  waiting: "Waiting",
  after: "After:",
  next: "Next:",
  nothingReady: "Nothing ready to run.",
  stopped: "The factory can't pick a next item until the missing items below are fixed.",
  spike: "Spike",
  waitsFor: "Waits for",
  missing: "No such item",
  pending: "Edits wait for the running job and go into develop after it.",
  project: "Project",
};

const Groups = [
  { key: "open", label: "Open" },
  { key: "approval", label: "Waiting for approval" },
  { key: "user", label: "For you" },
  { key: "done", label: "Done", collapsed: true },
];

const Marks = { " ": "[ ]", "?": "[?]", h: "[h]", x: "[x]" };

const StoryRoute = "#story/";

// The project shown on Backlogs, kept on this device: only a project name, never the token.
const BacklogProjectKey = "controlroom-backlog-project";

// Each group's open or closed state per project, kept on this device as {"<project>":{"<group>":true|false}}
// (true is open): only project names and group keys, never the token or backlog text.
const BacklogGroupsKey = "controlroom-backlog-groups";

// Readable project names of the last backlogs answer, in response order.
let readableProjects = [];

// Set when the app itself opened the story view, so Back can go back in history.
let openedFromList = false;
let listScroll = 0;
let storyRequest = 0;

function element(tag, className, text) {
  const node = document.createElement(tag);
  if (className) {
    node.className = className;
  }
  if (text !== undefined) {
    node.textContent = text;
  }
  return node;
}

function asText(value) {
  return typeof value === "string" ? value : "";
}

function titleText(value) {
  return asText(value).trim() !== "" ? asText(value) : Texts.noTitle;
}

function titleElement(value) {
  const title = element("strong", "title", titleText(value));
  if (asText(value).trim() === "") {
    title.classList.add("title-empty");
  }
  return title;
}

function asList(value) {
  return Array.isArray(value) ? value : [];
}

function asNumber(value) {
  return Number.isInteger(value) && value >= 0 ? value : null;
}

function storyHash(project, number) {
  return StoryRoute + encodeURIComponent(project) + "/" + number;
}

function parseStoryHash(hash) {
  if (!hash.startsWith(StoryRoute)) {
    return null;
  }
  const parts = hash.slice(StoryRoute.length).split("/");
  if (parts.length !== 2 || !/^[0-9]{1,9}$/.test(parts[1])) {
    return null;
  }
  let project;
  try {
    project = decodeURIComponent(parts[0]);
  } catch {
    return null;
  }
  if (project === "") {
    return null;
  }
  return { project: project, number: parts[1] };
}

function openStory(project, number) {
  openedFromList = true;
  location.hash = storyHash(project, number);
}

function renderDependencies(projectName, after) {
  const line = element("span", "after");
  line.appendChild(element("span", "after-label", Texts.after));
  for (const dependency of after) {
    const number = asNumber(dependency && dependency.number);
    if (number === null) {
      continue;
    }
    const target = asText(dependency.project) || projectName;
    const label = (asText(dependency.project) ? dependency.project : "") + "#" + number;
    const link = element("a", "dependency", label);
    link.href = storyHash(target, number);
    line.appendChild(link);
  }
  return line;
}

function renderNumberedLine(projectName, version, entry, number) {
  const item = element("li", "numbered");
  const head = element("a", "head");
  head.href = storyHash(projectName, number);
  head.appendChild(element("span", "mark", Marks[asText(entry.mark)] || Marks[" "]));
  head.appendChild(element("span", "number", "#" + number));
  head.appendChild(titleElement(entry.title));
  if (entry.waiting === true) {
    head.appendChild(element("span", "badge badge-waiting", Texts.waiting));
  }
  item.appendChild(head);
  const after = asList(entry.after);
  if (after.length > 0) {
    item.appendChild(renderDependencies(projectName, after));
  }
  const rest = asText(entry.rest);
  if (rest !== "") {
    item.appendChild(element("span", "rest", rest));
  }
  appendEditRow(item, projectName, version, entry);
  // The whole row opens the story; the head and dependency links navigate on their own.
  item.addEventListener("click", (event) => {
    if (event.target instanceof Element && event.target.closest("a")) {
      openedFromList = true;
      return;
    }
    openStory(projectName, number);
  });
  return item;
}

// The edit row (edit.js) of a line that is not done; shown only while the project is in edit mode.
function appendEditRow(item, projectName, version, entry) {
  const row = editRow(projectName, version, entry);
  if (row !== null) {
    item.appendChild(row);
  }
}

function renderLines(projectName, version, entries) {
  const list = element("ul", "lines");
  for (const entry of entries) {
    const number = asNumber(entry && entry.number);
    if (number !== null) {
      list.appendChild(renderNumberedLine(projectName, version, entry, number));
      continue;
    }
    const item = element("li");
    item.appendChild(titleElement(entry && entry.title));
    const rest = asText(entry && entry.rest);
    if (rest !== "") {
      item.appendChild(element("span", "rest", rest));
    }
    appendEditRow(item, projectName, version, entry);
    list.appendChild(item);
  }
  return list;
}

function renderGroup(projectName, version, group, entries) {
  const heading = group.label + " (" + entries.length + ")";
  const details = element("details", "group");
  details.appendChild(element("summary", null, heading));
  details.appendChild(renderLines(projectName, version, entries));
  foldGroup(details, projectName, group.key);
  return details;
}

// Without a stored state only Done (collapsed in Groups) starts closed; the plan's Waiting group starts open.
function groupDefaultOpen(key) {
  const group = Groups.find((entry) => entry.key === key);
  return !(group && group.collapsed === true);
}

function isObject(value) {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

// The stored states as an object without a prototype; a failed read, invalid JSON or a non-object counts as
// nothing stored.
function storedGroupStates() {
  let states;
  try {
    states = JSON.parse(localStorage.getItem(BacklogGroupsKey));
  } catch {
    return Object.create(null);
  }
  return Object.assign(Object.create(null), isObject(states) ? states : {});
}

// The stored state of one project's group (own properties only, booleans only), else the default.
function groupOpen(projectName, key) {
  const states = storedGroupStates();
  const project = Object.hasOwn(states, projectName) ? states[projectName] : null;
  const open = isObject(project) && Object.hasOwn(project, key) ? project[key] : null;
  return typeof open === "boolean" ? open : groupDefaultOpen(key);
}

// Stores one project's group state and keeps every other stored entry.
function storeGroupOpen(projectName, key, open) {
  const states = storedGroupStates();
  const old = Object.hasOwn(states, projectName) ? states[projectName] : null;
  const project = Object.assign(Object.create(null), isObject(old) ? old : {});
  project[key] = open;
  states[projectName] = project;
  try {
    localStorage.setItem(BacklogGroupsKey, JSON.stringify(states));
  } catch {
    // Private mode or storage off: the group still folds for this page.
  }
}

// Names the group, opens or closes it from the stored state (else the default) and stores each change by the user.
function foldGroup(details, projectName, key) {
  details.dataset.group = key;
  details.open = groupOpen(projectName, key);
  details.addEventListener("toggle", () => {
    if (details.open !== groupOpen(projectName, key)) {
      storeGroupOpen(projectName, key, details.open);
    }
  });
}

// A link "#N Title" to a story; the title is set as text.
function planItemLink(projectName, number, title) {
  const link = element("a", "plan-item");
  link.href = storyHash(projectName, number);
  link.appendChild(element("span", "number", "#" + number));
  link.appendChild(titleElement(title));
  return link;
}

// "Waits for" and one link per unmet dependency; a missing one is plain text with the fixed warning.
function renderWaitsFor(projectName, after) {
  const line = element("span", "after");
  line.appendChild(element("span", "after-label", Texts.waitsFor));
  for (const dependency of after) {
    const number = asNumber(dependency && dependency.number);
    if (number === null) {
      continue;
    }
    const target = asText(dependency.project) || projectName;
    const label = (target === projectName ? "" : target) + "#" + number;
    if (dependency.missing === true) {
      line.appendChild(element("span", "dependency-missing", label));
      line.appendChild(element("span", "missing", Texts.missing));
      continue;
    }
    const link = element("a", "dependency", label);
    link.href = storyHash(target, number);
    line.appendChild(link);
  }
  return line;
}

// What /factory next would take now, and the open items that wait (BacklogPlan on the server).
function renderPlan(projectName, project) {
  const plan = element("div", "plan");
  const next = element("p", "next");
  const item = project.next;
  const number = asNumber(item && item.number);
  if (project.stopped === true) {
    next.classList.add("next-stopped");
    next.textContent = Texts.stopped;
  } else if (number !== null) {
    next.appendChild(element("span", "next-label", Texts.next));
    next.appendChild(planItemLink(projectName, number, item.title));
    if (item.spike === true) {
      next.appendChild(element("span", "badge badge-spike", Texts.spike));
    }
  } else {
    next.textContent = Texts.nothingReady;
  }
  plan.appendChild(next);
  const blocked = asList(project.blocked).filter((entry) => asNumber(entry && entry.number) !== null);
  if (blocked.length > 0) {
    const group = element("details", "group waiting-group");
    group.appendChild(element("summary", null, Texts.waiting + " (" + blocked.length + ")"));
    const list = element("ul", "lines");
    for (const entry of blocked) {
      const row = element("li");
      row.appendChild(planItemLink(projectName, entry.number, entry.title));
      row.appendChild(renderWaitsFor(projectName, asList(entry.after)));
      list.appendChild(row);
    }
    group.appendChild(list);
    foldGroup(group, projectName, "waiting");
    plan.appendChild(group);
  }
  return plan;
}

// The plan as one string: a section whose signature changed is replaced on a live refresh, even when only another
// project's backlog changed.
function planSignature(project) {
  if (!project || project.readable !== true) {
    return "";
  }
  return JSON.stringify([project.next || null, project.stopped === true, asList(project.blocked)]);
}

function renderProject(project) {
  const section = element("section", "project");
  const name = asText(project && project.name);
  section.dataset.project = name;
  section.dataset.plan = planSignature(project);
  section.hidden = backlogSectionHidden(name);
  const version = asText(project && project.version);
  const head = element("div", "project-head");
  head.appendChild(element("h2", null, asText(project && project.name)));
  section.appendChild(head);
  // Edits go to the backlog branch while a job runs; a fixed text, never git output.
  if (project && project.pending === true) {
    section.appendChild(element("p", "backlog-pending", Texts.pending));
  }
  if (!project || project.readable !== true) {
    section.appendChild(element("p", "error", Texts.unreadable));
    return section;
  }
  if (version !== "") {
    head.appendChild(editToggle(name, section));
  }
  section.appendChild(statusLine(name));
  section.appendChild(renderPlan(name, project));
  let shown = 0;
  for (const group of Groups) {
    const entries = asList(project[group.key]);
    if (entries.length > 0) {
      section.appendChild(renderGroup(name, version, group, entries));
      shown += 1;
    }
  }
  if (shown === 0) {
    section.appendChild(element("p", "empty", Texts.noLines));
  }
  const add = addForm(name, version);
  if (add !== null) {
    section.appendChild(add);
  }
  return section;
}

function show(nodes) {
  const main = document.getElementById("backlogs");
  main.replaceChildren(...nodes);
}

// The stored choice; a failed read counts as nothing stored.
function storedBacklogProject() {
  try {
    return localStorage.getItem(BacklogProjectKey);
  } catch {
    return null;
  }
}

function storeBacklogProject(name) {
  try {
    localStorage.setItem(BacklogProjectKey, name);
  } catch {
    // Private mode or storage off: the picker still works for this page.
  }
}

function readableNames(projects) {
  return projects.filter((project) => project && project.readable === true).map((project) => asText(project.name));
}

// The stored project when it is readable, else the first readable one (not stored); null when none is readable.
function chosenBacklogProject() {
  const stored = storedBacklogProject();
  if (stored !== null && readableProjects.includes(stored)) {
    return stored;
  }
  return readableProjects.length > 0 ? readableProjects[0] : null;
}

// Only the chosen project's section is shown; with no readable project every section is.
function backlogSectionHidden(name) {
  const chosen = chosenBacklogProject();
  return chosen !== null && name !== chosen;
}

function fillBacklogPicker(select) {
  const options = readableProjects.map((name) => {
    const option = document.createElement("option");
    option.value = name;
    option.textContent = name;
    return option;
  });
  select.replaceChildren(...options);
  select.value = chosenBacklogProject();
}

function renderBacklogPicker() {
  const picker = element("div", "backlog-picker");
  const label = element("label", null, Texts.project);
  label.htmlFor = "backlog-project";
  const select = document.createElement("select");
  select.id = "backlog-project";
  fillBacklogPicker(select);
  select.addEventListener("change", () => {
    storeBacklogProject(select.value);
    applyBacklogChoice();
  });
  picker.append(label, select);
  return picker;
}

// Sets the select and the hidden state of the existing sections from the choice, without a new fetch.
function applyBacklogChoice() {
  const chosen = chosenBacklogProject();
  const select = document.getElementById("backlog-project");
  if (select !== null && chosen !== null && select.value !== chosen && document.activeElement !== select) {
    select.value = chosen;
  }
  for (const section of document.querySelectorAll("#backlogs > section.project")) {
    section.hidden = backlogSectionHidden(section.dataset.project);
  }
}

// A story link into another readable project makes it the choice (route()); unknown names change nothing.
function chooseBacklogProject(name) {
  if (!readableProjects.includes(name)) {
    return;
  }
  storeBacklogProject(name);
  applyBacklogChoice();
}

// After a live refresh: the picker follows the readable list. Its options are rebuilt only when the names changed,
// and never while the select has focus (the next refresh tries again).
function updateBacklogPicker(main) {
  const picker = main.querySelector(":scope > div.backlog-picker");
  if (readableProjects.length === 0) {
    if (picker !== null) {
      picker.remove();
    }
    return;
  }
  if (picker === null) {
    main.prepend(renderBacklogPicker());
    return;
  }
  const select = picker.querySelector("select");
  if (document.activeElement === select) {
    return;
  }
  const names = Array.from(select.options, (option) => option.value);
  if (JSON.stringify(names) !== JSON.stringify(readableProjects)) {
    fillBacklogPicker(select);
  }
}

// The picker (when a project is readable) and every section; renderProject hides all but the chosen one.
function showProjects(projects) {
  readableProjects = readableNames(projects);
  const sections = projects.map(renderProject);
  show(readableProjects.length > 0 ? [renderBacklogPicker(), ...sections] : sections);
}

// The backlogs response; throws on a network error or an error status.
async function fetchBacklogs() {
  const response = await authFetch("/api/backlogs", { cache: "no-store" });
  if (!response.ok) {
    throw new Error("status");
  }
  return response.json();
}

async function load() {
  let data;
  try {
    data = await fetchBacklogs();
  } catch {
    show([element("p", "error", Texts.unreachable)]);
    return;
  }
  const projects = asList(data && data.projects);
  if (projects.length === 0) {
    show([element("p", "empty", Texts.noProjects)]);
    return;
  }
  showProjects(projects);
}

// Live refresh (live.js): only the sections of the named projects, and those whose plan signature changed, are
// replaced, each in place, so scroll, edit mode and every group's open or closed state stay. A section with an open
// Add or title form, or with focus inside, is left alone; its name is returned so the next check tries again. A
// failed request returns every name.
async function refreshBacklogs(names) {
  let data;
  try {
    data = await fetchBacklogs();
  } catch {
    return names;
  }
  const main = document.getElementById("backlogs");
  const projects = asList(data && data.projects);
  readableProjects = readableNames(projects);
  const sections = new Map();
  for (const section of main.querySelectorAll(":scope > section.project")) {
    sections.set(section.dataset.project, section);
  }
  const scroll = window.scrollY;
  if (sections.size === 0) {
    // The list shows a fixed text (loading, error or no projects): show it whole.
    if (projects.length === 0) {
      show([element("p", "empty", Texts.noProjects)]);
    } else {
      showProjects(projects);
    }
    restoreListScroll(main, scroll);
    return [];
  }
  const picker = main.querySelector(":scope > div.backlog-picker");
  const wanted = new Set(names);
  const left = [];
  const listed = new Set();
  let previous = null;
  for (const project of projects) {
    const name = asText(project && project.name);
    listed.add(name);
    const old = sections.get(name);
    const changed = wanted.has(name) || (old !== undefined && old.dataset.plan !== planSignature(project));
    if (old && (!changed || sectionBusy(old))) {
      if (changed) {
        left.push(name);
      }
      previous = old;
      continue;
    }
    const next = renderProject(project);
    if (old) {
      keepOpenGroups(old, next);
      old.replaceWith(next);
    } else if (previous) {
      previous.after(next);
    } else if (picker !== null) {
      picker.after(next);
    } else {
      main.prepend(next);
    }
    previous = next;
  }
  for (const [name, old] of sections) {
    if (!listed.has(name) && wanted.has(name)) {
      if (sectionBusy(old)) {
        left.push(name);
      } else {
        old.remove();
      }
    }
  }
  if (main.querySelector(":scope > section.project") === null) {
    show([element("p", "empty", Texts.noProjects)]);
  } else {
    updateBacklogPicker(main);
    applyBacklogChoice();
  }
  restoreListScroll(main, scroll);
  return left;
}

function restoreListScroll(main, scroll) {
  if (!main.hidden) {
    window.scrollTo(0, scroll);
  }
}

// True while the user works in the section: an open title or Add form, or focus inside it.
function sectionBusy(section) {
  return section.querySelector(".edit-title-form") !== null
    || section.querySelector("details.edit-add[open]") !== null
    || section.contains(document.activeElement);
}

// Each group of the new section keeps the open state of the old section's group with the same name, even when
// storage fails; a group that newly appears keeps its stored state or default.
function keepOpenGroups(old, next) {
  const before = new Map();
  for (const group of old.querySelectorAll("details.group")) {
    before.set(group.dataset.group, group.open);
  }
  for (const group of next.querySelectorAll("details.group")) {
    if (before.has(group.dataset.group)) {
      group.open = before.get(group.dataset.group);
    }
  }
}

// Story view: blocks come as plain texts and are built with createElement/textContent only.

function renderInlines(parent, inlines) {
  for (const inline of asList(inlines)) {
    const text = asText(inline && inline.text);
    const kind = asText(inline && inline.kind);
    if (kind === "code") {
      parent.appendChild(element("code", null, text));
    } else if (kind === "strong") {
      parent.appendChild(element("strong", null, text));
    } else if (kind === "em") {
      parent.appendChild(element("em", null, text));
    } else {
      parent.appendChild(element("span", null, text));
    }
  }
  return parent;
}

// A table scrolls sideways inside its own box so a wide one never widens the page.
function renderTable(block) {
  const wrap = element("div", "story-table");
  const table = element("table");
  const head = element("thead");
  const headRow = element("tr");
  for (const cell of asList(block.header)) {
    headRow.appendChild(renderInlines(element("th"), cell));
  }
  head.appendChild(headRow);
  table.appendChild(head);
  const body = element("tbody");
  for (const row of asList(block.rows)) {
    const line = element("tr");
    for (const cell of asList(row)) {
      line.appendChild(renderInlines(element("td"), cell));
    }
    body.appendChild(line);
  }
  table.appendChild(body);
  wrap.appendChild(table);
  return wrap;
}

function renderBlock(block) {
  const kind = asText(block && block.kind);
  if (kind === "heading") {
    const level = Math.min(Math.max(asNumber(block.level) || 1, 1), 3);
    return renderInlines(element("h" + (level + 1), "story-heading"), block.inlines);
  }
  if (kind === "list") {
    const list = element(block.ordered === true ? "ol" : "ul", "story-list");
    for (const item of asList(block.items)) {
      list.appendChild(renderInlines(element("li"), item));
    }
    return list;
  }
  if (kind === "code") {
    const pre = element("pre", "story-code");
    pre.appendChild(element("code", null, asText(block.text)));
    return pre;
  }
  if (kind === "table") {
    return renderTable(block);
  }
  return renderInlines(element("p"), block && block.inlines);
}

function backButton() {
  const button = element("button", "back", Texts.back);
  button.type = "button";
  button.addEventListener("click", goBack);
  return button;
}

function goBack() {
  if (openedFromList) {
    history.back();
    return;
  }
  history.replaceState(null, "", location.pathname + location.search);
  route();
}

function showStoryContent(nodes, extra) {
  const main = document.getElementById("story");
  const bar = element("div", "story-bar");
  bar.appendChild(backButton());
  for (const node of extra || []) {
    bar.appendChild(node);
  }
  const footer = element("div", "story-footer");
  footer.appendChild(backButton());
  main.replaceChildren(bar, ...nodes, footer);
}

// The story response; throws on a network error or an error status.
async function fetchStory(target) {
  const response = await authFetch(
    "/api/story?project=" + encodeURIComponent(target.project) + "&number=" + encodeURIComponent(target.number),
    { cache: "no-store" });
  if (!response.ok) {
    throw new Error("status");
  }
  return response.json();
}

async function loadStory(target) {
  storyRequest += 1;
  const request = storyRequest;
  showStoryContent([element("p", "status", Texts.loading)]);
  let data;
  try {
    data = await fetchStory(target);
  } catch {
    if (request === storyRequest) {
      showStoryContent([element("p", "error", Texts.storyFailed)]);
      document.querySelector("#story .back").focus({ preventScroll: true });
    }
    return;
  }
  if (request !== storyRequest) {
    return;
  }
  const article = renderStoryArticle(data);
  const title = article.querySelector(".story-title");
  showStoryContent([article], [
    storyEditButton(target, data, () => loadStory(target)),
    storyNoteButton(target, data, () => loadStory(target)),
  ]);
  // Move focus into the story so keyboard and screen-reader users don't stay on the hidden list.
  title.focus({ preventScroll: true });
  showStoryDecision(target, data);
}

function renderStoryArticle(data) {
  const heading = asText(data && data.project) + " #" + asNumber(data && data.number) + " " + asText(data && data.title);
  const article = element("article", "story");
  const title = element("h1", "story-title", heading);
  title.tabIndex = -1;
  article.appendChild(title);
  for (const block of asList(data && data.blocks)) {
    article.appendChild(renderBlock(block));
  }
  return article;
}

// The project of the story on screen, or null when no story is shown.
function openStoryProject() {
  const target = parseStoryHash(location.hash);
  return currentView === "story" && target !== null ? target.project : null;
}

// Live refresh (live.js): the open story is rendered again in place, with no Loading… text, the same scroll and
// focus kept on the same element. Left alone while the Edit story form is open. Returns false when it should be
// tried again (form open or request failed).
async function refreshStory() {
  const target = parseStoryHash(location.hash);
  const editing = () => document.querySelector("#story .edit-story-form") !== null
    || document.querySelector("#story .add-note-form") !== null;
  if (currentView !== "story" || target === null) {
    return true;
  }
  if (editing()) {
    return false;
  }
  const old = document.querySelector("#story .story");
  if (old === null) {
    return true;
  }
  const request = storyRequest;
  let data;
  try {
    data = await fetchStory(target);
  } catch {
    return false;
  }
  if (request !== storyRequest || !old.isConnected) {
    return true;
  }
  if (editing()) {
    return false;
  }
  const scroll = window.scrollY;
  const article = renderStoryArticle(data);
  const focusedTitle = old.contains(document.activeElement);
  old.replaceWith(article);
  const reload = () => loadStory(target);
  for (const [selector, make] of [[".edit-story-button", storyEditButton], [".add-note-button", storyNoteButton]]) {
    const button = document.querySelector("#story " + selector);
    if (button !== null) {
      const focusedButton = button === document.activeElement;
      const next = make(target, data, reload);
      button.replaceWith(next);
      if (focusedButton) {
        next.focus({ preventScroll: true });
      }
    }
  }
  if (focusedTitle) {
    article.querySelector(".story-title").focus({ preventScroll: true });
  }
  showStoryDecision(target, data);
  window.scrollTo(0, scroll);
  return true;
}

const Views = ["backlogs", "board", "inbox", "tasks", "search", "capture", "history", "doctor", "job", "story"];
const ViewTitles = { backlogs: "Backlogs", board: "Jobs", inbox: "Inbox", tasks: "Tasks", search: "Search",
  capture: "Capture", history: "History", doctor: "Health" };
let currentView = null;

function viewFor(hash) {
  if (parseStoryHash(hash)) {
    return "story";
  }
  if (parseJobHash(hash) !== null) {
    return "job";
  }
  if (hash === InboxRoute) {
    return "inbox";
  }
  if (hash === TasksRoute) {
    return "tasks";
  }
  if (hash === SearchRoute) {
    return "search";
  }
  if (hash === CaptureRoute) {
    return "capture";
  }
  if (hash === HistoryRoute) {
    return "history";
  }
  if (hash === DoctorRoute) {
    return "doctor";
  }
  return hash === BoardRoute ? "board" : "backlogs";
}

function showView(name) {
  // While the token view is shown, no data view or header is.
  const blocked = tokenViewShown();
  for (const view of Views) {
    document.getElementById(view).hidden = blocked || view !== name;
  }
  // The wide board view widens the header too, so the nav lines up with the board's left edge.
  document.body.classList.toggle("view-board", !blocked && name === "board");
  const top = document.getElementById("top");
  top.hidden = blocked || !(name in ViewTitles);
  if (!top.hidden) {
    document.getElementById("top-title").textContent = ViewTitles[name];
    for (const link of document.querySelectorAll(".nav a")) {
      if (link.dataset.view === name) {
        link.setAttribute("aria-current", "page");
      } else {
        link.removeAttribute("aria-current");
      }
    }
  }
}

function route() {
  const next = viewFor(location.hash);
  // Keep the scroll position of the list being left, so Back returns to it.
  if (currentView === "backlogs" && next !== "backlogs") {
    listScroll = window.scrollY;
  }
  if (currentView === "board" && next !== "board") {
    rememberBoardScroll();
  }
  if (currentView === "inbox" && next !== "inbox") {
    rememberInboxScroll();
  }
  if (currentView === "tasks" && next !== "tasks") {
    rememberTasksScroll();
  }
  if (currentView === "search" && next !== "search") {
    rememberSearchScroll();
  }
  currentView = next;
  routeEditNotice();
  if (next !== "story") {
    storyRequest += 1;
    openedFromList = false;
    document.getElementById("story").replaceChildren();
  }
  if (next !== "job") {
    leaveJob();
  }
  showView(next);
  liveRouteChanged();
  if (next === "story") {
    window.scrollTo(0, 0);
    const target = parseStoryHash(location.hash);
    if (target !== null) {
      chooseBacklogProject(target.project);
    }
    loadStory(target);
    return;
  }
  if (next === "job") {
    showJob(parseJobHash(location.hash));
    return;
  }
  if (next === "board") {
    showBoard();
    return;
  }
  if (next === "inbox") {
    showInbox();
    return;
  }
  if (next === "tasks") {
    showTasks();
    return;
  }
  if (next === "search") {
    showSearch();
    return;
  }
  if (next === "capture") {
    showCapture();
    return;
  }
  if (next === "history") {
    showHistory();
    return;
  }
  if (next === "doctor") {
    showDoctor();
    return;
  }
  showStatus();
  window.scrollTo(0, listScroll);
}

function registerServiceWorker() {
  if (!("serviceWorker" in navigator)) {
    return;
  }
  // A failed registration only means no fast start; show nothing.
  navigator.serviceWorker.register("/sw.js").catch(() => undefined);
}

// After Save: show the current view again and reload its data, the backlogs and the inbox and tasks counts.
onTokenSaved(() => {
  route();
  load();
  loadInboxCount();
  loadTasksCount();
});
if (!hasToken()) {
  showTokenView(false);
}
window.addEventListener("hashchange", route);
route();
load();
loadInboxCount();
loadTasksCount();
startLiveChecks();
registerServiceWorker();
