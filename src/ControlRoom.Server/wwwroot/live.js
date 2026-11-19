"use strict";

// Live updates: one timer asks /api/changes (metadata fingerprints) every PollInterval while the page is visible,
// and only what changed is fetched again: the board (pollBoard in board.js), the backlog sections of changed
// projects, the open story (app.js), the inbox with its nav count (inbox.js) and the tasks with theirs (tasks.js). A
// failed check shows one fixed connection notice. Loaded after tasks.js and before app.js; the functions of the
// other scripts are only called at run time. Fingerprints are compared, never shown; only fixed texts are rendered, with textContent.

const LiveTexts = {
  lost: "Connection lost. Reconnecting…",
};

const ChangesRoute = "/api/changes";
// A check that gets no answer in this time counts as failed, so a dropped connection can't stop the checks.
const CheckTimeout = 10000;

let liveTimer = null;
let livePending = false;
let liveLost = false;
let liveLast = null;
// What still has to be fetched again: the board, backlog sections (project names), the open story's project and,
// after a lost connection, the open job page.
let boardStale = false;
let jobStale = false;
// Any project's backlog or stories changed: the inbox count (and the list, while shown) is fetched again.
let inboxStale = false;
// The same for the tasks and their ready count.
let tasksStale = false;
const staleBacklogs = new Set();
const staleStories = new Set();
let backlogsRefreshing = false;

function connectionLost() {
  return liveLost;
}

// The notice under the header; on the board the board-status line shows the same text instead.
function showConnectionState() {
  const notice = document.getElementById("live-notice");
  const onBoard = !document.getElementById("board").hidden;
  const shown = liveLost && !onBoard;
  if (shown !== !notice.hidden) {
    notice.textContent = shown ? LiveTexts.lost : "";
    notice.hidden = !shown;
  }
  if (liveLost) {
    const status = document.getElementById("board-status");
    status.className = "live-status error";
    status.textContent = LiveTexts.lost;
  }
}

function setConnectionLost(lost) {
  const was = liveLost;
  liveLost = lost;
  showConnectionState();
  if (was && !lost) {
    setBoardStatus(true);
  }
}

// The response as { jobs, projects: Map(name -> { backlog, stories }) }, or null when it has another shape.
function readChanges(data) {
  if (!data || typeof data.jobs !== "string" || !data.projects || typeof data.projects !== "object"
    || Array.isArray(data.projects)) {
    return null;
  }
  const projects = new Map();
  for (const [name, value] of Object.entries(data.projects)) {
    if (!value || typeof value.backlog !== "string" || typeof value.stories !== "string") {
      return null;
    }
    projects.set(name, { backlog: value.backlog, stories: value.stories });
  }
  return { jobs: data.jobs, projects: projects };
}

// Marks what changed since the last check. The first check is the baseline; after a lost connection everything
// shown is fetched once.
function noteChanges(next, recovered) {
  const previous = liveLast;
  liveLast = next;
  const names = new Set([...next.projects.keys(), ...(previous ? previous.projects.keys() : [])]);
  if (recovered) {
    boardStale = true;
    jobStale = true;
    inboxStale = true;
    tasksStale = true;
    for (const name of names) {
      staleBacklogs.add(name);
      staleStories.add(name);
    }
    return;
  }
  if (previous === null) {
    return;
  }
  if (previous.jobs !== next.jobs) {
    boardStale = true;
  }
  for (const name of names) {
    const before = previous.projects.get(name);
    const after = next.projects.get(name);
    const backlogChanged = !before || !after || before.backlog !== after.backlog;
    if (backlogChanged) {
      boardStale = true;
      inboxStale = true;
      tasksStale = true;
      staleBacklogs.add(name);
      staleStories.add(name);
    } else if (before.stories !== after.stories) {
      staleStories.add(name);
      inboxStale = true;
      tasksStale = true;
    }
  }
}

async function refreshStaleBacklogs() {
  if (backlogsRefreshing || staleBacklogs.size === 0 || document.getElementById("backlogs").hidden) {
    return;
  }
  backlogsRefreshing = true;
  const names = [...staleBacklogs];
  staleBacklogs.clear();
  try {
    for (const name of await refreshBacklogs(names)) {
      staleBacklogs.add(name);
    }
  } finally {
    backlogsRefreshing = false;
  }
}

async function refreshStaleStory() {
  const project = openStoryProject();
  const wanted = project !== null && staleStories.has(project);
  staleStories.clear();
  if (wanted && !(await refreshStory())) {
    staleStories.add(project);
  }
}

async function refreshShownViews() {
  if (boardViewsVisible()) {
    // A board fetch that failed earlier (the line shows an error) is tried again too.
    if (boardStale || document.getElementById("board-status").classList.contains("error")) {
      if (await pollBoard()) {
        boardStale = false;
      }
    } else {
      setBoardStatus(true);
    }
    if (jobStale && openJobId !== null && !document.getElementById("job").hidden) {
      loadJob(openJobId, true);
    }
  } else {
    setBoardStatus(true);
  }
  jobStale = false;
  await refreshStaleBacklogs();
  await refreshStaleStory();
  await refreshStaleTasks();
  await refreshStaleInbox();
}

// The count follows every change; loadInbox leaves the list alone while a change is sent (changeBusy) and
// reports it, so it is tried again on the next check. A Drop confirm blocks the page, so it never overlaps a check.
async function refreshStaleInbox() {
  if (!inboxStale) {
    return;
  }
  inboxStale = false;
  if (!(await loadInbox())) {
    inboxStale = true;
  }
}

// The same for the tasks: the ready count follows every change; loadTasks leaves the list alone while a change is
// sent (changeBusy) and reports it, so it is tried again on the next check.
async function refreshStaleTasks() {
  if (!tasksStale) {
    return;
  }
  tasksStale = false;
  if (!(await loadTasks())) {
    tasksStale = true;
  }
}

// One check: only while visible, with no token view and no earlier check pending. A 401 shows the token view
// (authFetch) and stops checks until a token is saved.
async function checkChanges() {
  if (document.visibilityState !== "visible" || tokenViewShown() || livePending) {
    return;
  }
  livePending = true;
  try {
    let next;
    try {
      const response = await authFetch(ChangesRoute, { cache: "no-store", signal: AbortSignal.timeout(CheckTimeout) });
      if (!response.ok) {
        throw new Error("status");
      }
      next = readChanges(await response.json());
      if (next === null) {
        throw new Error("data");
      }
    } catch {
      if (!tokenViewShown()) {
        setConnectionLost(true);
      }
      return;
    }
    const recovered = liveLost;
    setConnectionLost(false);
    noteChanges(next, recovered);
    await refreshShownViews();
  } finally {
    livePending = false;
  }
}

// Called by app.js after every route change: the notice follows the view, and a list shown again gets the
// sections that changed while it was hidden.
function liveRouteChanged() {
  showConnectionState();
  staleStories.clear();
  refreshStaleBacklogs();
}

// Called once by app.js: the one timer, a check at once, and a check when the page becomes visible again.
function startLiveChecks() {
  if (liveTimer !== null) {
    return;
  }
  liveTimer = setInterval(checkChanges, PollInterval);
  document.addEventListener("visibilitychange", () => {
    if (document.visibilityState === "visible") {
      checkChanges();
    }
  });
  checkChanges();
}
