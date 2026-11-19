"use strict";

// My tasks: every [h] item of every project from /api/tasks, split into Ready now and Waiting, with each item's story
// one tap away, a Done button on Ready items (sent through sendChange in edit.js with the version and line the list
// returned) and the ready count in the nav. Loaded after inbox.js and before live.js and app.js; their functions
// (element, titleText, storyHash, openStory, editButton) are only called at run time. Only fixed texts and file
// text through textContent are shown, never server error text.

const TasksTexts = {
  loading: "Loading…",
  failed: "Can't load the tasks.",
  unreadable: ": backlog can't be read.",
  ready: "Ready now",
  waiting: "Waiting",
  readyEmpty: "Nothing to do right now.",
  waitingEmpty: "Nothing waiting.",
  waitsFor: "Waits for",
  done: "Done",
  confirmDone: "Mark this task done? Its line becomes [x] in one commit.",
  link: "Tasks",
};

const TasksRoute = "#tasks";
const TasksApi = "/api/tasks";

// null while the count is unknown (not loaded yet, or the last load failed): the badge is hidden then.
let tasksCount = null;
let tasksRequest = 0;
let tasksScroll = 0;

function tasksLink() {
  return document.querySelector(".nav a[data-view=\"tasks\"]");
}

// Shows the count; hidden when it is 0 or unknown. A load in progress keeps the last value, so it never flashes 0.
function showTasksCount() {
  const badge = document.getElementById("tasks-count");
  const link = tasksLink();
  const shown = tasksCount !== null && tasksCount > 0;
  badge.textContent = shown ? String(tasksCount) : "";
  badge.hidden = !shown;
  link.setAttribute("aria-label", shown ? TasksTexts.link + ", " + tasksCount + " ready" : TasksTexts.link);
}

// One task with only well-formed fields, or null when it lacks what Done and the story link need.
function readTask(item) {
  const project = asText(item && item.project);
  const version = asText(item && item.version);
  const line = asNumber(item && item.line);
  if (project === "" || version === "" || line === null) {
    return null;
  }
  const after = [];
  for (const dependency of asList(item.after)) {
    const name = asText(dependency && dependency.project);
    const number = asNumber(dependency && dependency.number);
    if (name !== "" && number !== null) {
      after.push({ project: name, number: number });
    }
  }
  return {
    project: project,
    version: version,
    line: line,
    number: asNumber(item.number),
    title: asText(item.title),
    rest: asText(item.rest),
    story: item.story === true,
    after: after,
  };
}

// The response as { ready, waiting, unreadable } with only well-formed items, or null when it has another shape.
function readTasks(data) {
  if (!data || !Array.isArray(data.ready) || !Array.isArray(data.waiting) || !Array.isArray(data.unreadable)) {
    return null;
  }
  const pick = (items) => items.map(readTask).filter((item) => item !== null);
  return {
    ready: pick(data.ready),
    waiting: pick(data.waiting),
    unreadable: data.unreadable.filter((name) => typeof name === "string" && name !== ""),
  };
}

function tasksShown() {
  return !document.getElementById("tasks").hidden;
}

function showTasksContent(nodes) {
  document.getElementById("tasks").replaceChildren(...nodes);
}

// Fetches the tasks once: the count always, the list only while the view is shown and no change is being sent
// (changeBusy). Returns false when the list should be fetched again (failed, or left alone while busy).
async function loadTasks() {
  tasksRequest += 1;
  const request = tasksRequest;
  let tasks;
  try {
    const response = await authFetch(TasksApi, { cache: "no-store" });
    if (!response.ok) {
      throw new Error("status");
    }
    tasks = readTasks(await response.json());
    if (tasks === null) {
      throw new Error("data");
    }
  } catch {
    if (request === tasksRequest) {
      tasksCount = null;
      showTasksCount();
      if (tasksShown() && !changeBusy) {
        showTasksContent([element("p", "error", TasksTexts.failed)]);
      }
    }
    return false;
  }
  if (request !== tasksRequest) {
    return true;
  }
  tasksCount = tasks.ready.length;
  showTasksCount();
  if (!tasksShown()) {
    return true;
  }
  if (changeBusy) {
    return false;
  }
  const scroll = window.scrollY;
  renderTasks(tasks);
  window.scrollTo(0, scroll);
  return true;
}

// The count alone, at page load and after a token is saved; the tasks view fetches it with the list itself.
function loadTasksCount() {
  if (currentView !== "tasks") {
    loadTasks();
  }
}

function renderTasks(tasks) {
  const nodes = [];
  for (const name of tasks.unreadable) {
    nodes.push(element("p", "error tasks-unreadable", name + TasksTexts.unreadable));
  }
  nodes.push(renderTaskSection("tasks-ready", TasksTexts.ready, TasksTexts.readyEmpty, tasks.ready, true));
  nodes.push(renderTaskSection("tasks-waiting", TasksTexts.waiting, TasksTexts.waitingEmpty, tasks.waiting, false));
  showTasksContent(nodes);
}

function renderTaskSection(id, title, emptyText, items, ready) {
  const section = element("section", "tasks-section");
  section.setAttribute("aria-labelledby", id);
  const heading = element("h2", "tasks-heading", title);
  heading.id = id;
  section.appendChild(heading);
  if (items.length === 0) {
    section.appendChild(element("p", "empty", emptyText));
    return section;
  }
  const list = element("ul", "lines tasks-list");
  for (const item of items) {
    list.appendChild(renderTaskItem(item, ready));
  }
  section.appendChild(list);
  return section;
}

function renderTaskItem(item, ready) {
  const row = element("li", "tasks-item");
  row.appendChild(element("span", "tasks-project", item.project));
  const label = (item.number !== null ? "#" + item.number + " " : "") + titleText(item.title);
  if (item.number !== null) {
    row.classList.add("numbered");
    const head = element("a", "head tasks-head", label);
    head.href = storyHash(item.project, item.number);
    head.addEventListener("click", (event) => {
      event.preventDefault();
      event.stopPropagation();
      openStory(item.project, item.number);
    });
    row.appendChild(head);
  } else {
    row.appendChild(element("strong", "title tasks-head", label));
  }
  if (item.rest !== "") {
    row.appendChild(element("span", "rest", item.rest));
  }
  if (item.after.length > 0) {
    row.appendChild(renderTaskWaits(item));
  }
  if (ready) {
    row.appendChild(taskActions(item));
  }
  return row;
}

// "Waits for" and one link per unmet dependency: Project#N for another project, #N for the same one.
function renderTaskWaits(item) {
  const line = element("span", "after tasks-after");
  line.appendChild(element("span", "after-label", TasksTexts.waitsFor));
  for (const dependency of item.after) {
    const own = dependency.project === item.project;
    const label = (own ? "" : dependency.project) + "#" + dependency.number;
    const link = element("a", "dependency", label);
    link.href = storyHash(dependency.project, dependency.number);
    link.addEventListener("click", (event) => {
      event.preventDefault();
      openStory(dependency.project, dependency.number);
    });
    line.appendChild(link);
  }
  return line;
}

// Done for one Ready item, named for screen readers by number (or title for an unnumbered line).
function taskActions(item) {
  const subject = item.number !== null ? "#" + item.number : titleText(item.title);
  const actions = element("div", "edit-row tasks-actions");
  const done = editButton(TasksTexts.done, "edit-primary", () => {
    if (confirm(TasksTexts.confirmDone)) {
      sendTaskChange({ project: item.project, version: item.version, line: item.line, action: "done" });
    }
  });
  done.setAttribute("aria-label", TasksTexts.done + " " + subject);
  actions.appendChild(done);
  return actions;
}

// sendChange shows the fixed text for the outcome; a committed change fetches the tasks and the count again.
async function sendTaskChange(body) {
  if (await sendChange(body)) {
    loadTasks();
  }
}

// Called by app.js when the tasks view is shown (its heading is the header title): the last list stays while it is
// fetched again, so nothing flashes.
function showTasks() {
  const main = document.getElementById("tasks");
  if (main.childElementCount === 0) {
    showTasksContent([element("p", "status", TasksTexts.loading)]);
  }
  window.scrollTo(0, tasksScroll);
  loadTasks();
}

// Called by app.js when the tasks view is left, so Back from a story returns to the same place.
function rememberTasksScroll() {
  tasksScroll = window.scrollY;
}
