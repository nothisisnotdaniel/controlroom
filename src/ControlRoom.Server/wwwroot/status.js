"use strict";

// Project status on the Backlogs view: per readable project one line (div.project-status, between the head and the
// plan) with the last CI result the factory recorded and how many commits develop has that main does not, from
// /api/status. Read-only. Fetched when the Backlogs view is shown and, while shown, when live.js sees a changed jobs
// or backlog fingerprint (never on a timer of its own, never while hidden). A refresh replaces only the status lines;
// a line with focus inside is left alone and tried again on the next check. Loaded after doctor.js and before
// live.js and app.js; their functions (element, asText, authFetch) are only called at run time. Every text is fixed
// and set with textContent; the CI link is used only when the server kept it as an https://github.com/ URL.

const StatusTexts = {
  failed: "Status unavailable.",
  success: "CI passed",
  failure: "CI failed",
  timeout: "CI timed out",
  skipped: "CI skipped",
  none: "No CI recorded",
  aheadOne: "1 commit on develop not on main",
  aheadMany: " commits on develop not on main",
  level: "develop is level with main",
  gitUnknown: "Git status unavailable",
};

const StatusApi = "/api/status";
const StatusCiStates = ["success", "failure", "timeout", "skipped"];
const StatusBadCi = ["failure", "timeout"];

let statusRequest = 0;
// null before the first answer, "failed" after a failed call, else a Map of project name -> { ci, ahead }.
let statusData = null;

// The fixed CI text for a status, or the No CI text for anything else.
function statusCiText(ci) {
  if (!ci || !StatusCiStates.includes(ci.status)) {
    return StatusTexts.none;
  }
  return StatusTexts[ci.status];
}

// The fixed develop/main text: singular for 1, level for 0, unavailable for null.
function statusAheadText(ahead) {
  if (typeof ahead !== "number" || !Number.isInteger(ahead) || ahead < 0) {
    return StatusTexts.gitUnknown;
  }
  if (ahead === 0) {
    return StatusTexts.level;
  }
  if (ahead === 1) {
    return StatusTexts.aheadOne;
  }
  return String(ahead) + StatusTexts.aheadMany;
}

function statusLocalTime(text) {
  if (typeof text !== "string" || text === "") {
    return "";
  }
  const date = new Date(text);
  if (Number.isNaN(date.getTime())) {
    return "";
  }
  return date.toLocaleString(undefined, { dateStyle: "medium", timeStyle: "short" });
}

// Only an https://github.com/ link is used; anything else shows the CI text without a link.
function statusSafeUrl(value) {
  return typeof value === "string" && value.startsWith("https://github.com/") ? value : "";
}

// The response as a Map of name -> { ci, ahead }, or null when it has another shape.
function readStatus(data) {
  if (!data || !Array.isArray(data.projects)) {
    return null;
  }
  const projects = new Map();
  for (const item of data.projects) {
    if (!item || typeof item.name !== "string") {
      continue;
    }
    let ci = null;
    if (item.ci && StatusCiStates.includes(item.ci.status)) {
      ci = { status: item.ci.status, url: statusSafeUrl(item.ci.url), time: asText(item.ci.time) };
    }
    projects.set(item.name, { ci: ci, ahead: typeof item.ahead === "number" ? item.ahead : null });
  }
  return projects;
}

// Fills one line from the current data: nothing yet (hidden), the failed text, or the CI part and the git part.
function fillStatusLine(line) {
  if (statusData === null) {
    line.replaceChildren();
    line.hidden = true;
    return;
  }
  line.hidden = false;
  if (statusData === "failed") {
    line.replaceChildren(element("span", "status-failed", StatusTexts.failed));
    return;
  }
  const status = statusData.get(line.dataset.project) || { ci: null, ahead: null };
  const ci = status.ci;
  const ciPart = element("span", "status-ci");
  if (ci && StatusBadCi.includes(ci.status)) {
    ciPart.classList.add("ci-bad");
  }
  if (ci && ci.url !== "") {
    const link = element("a", "status-ci-link", statusCiText(ci));
    link.href = ci.url;
    link.target = "_blank";
    link.rel = "noopener noreferrer";
    ciPart.appendChild(link);
  } else {
    ciPart.textContent = statusCiText(ci);
  }
  const parts = [ciPart];
  const time = ci ? statusLocalTime(ci.time) : "";
  if (time !== "") {
    parts.push(element("span", "status-time", time));
  }
  parts.push(element("span", "status-ahead", statusAheadText(status.ahead)));
  line.replaceChildren(...parts);
}

// A new status line for a project section (app.js renderProject), filled from the last answer.
function statusLine(projectName) {
  const line = element("div", "project-status");
  line.dataset.project = projectName;
  fillStatusLine(line);
  return line;
}

// Replaces the status lines on the Backlogs view only; false when a line with focus inside was left alone.
function applyStatusLines() {
  let all = true;
  for (const line of document.querySelectorAll("#backlogs section.project > div.project-status")) {
    if (line.contains(document.activeElement)) {
      all = false;
      continue;
    }
    fillStatusLine(line);
  }
  return all;
}

// One /api/status call; an older answer is dropped. True when it succeeded and every line was replaced.
async function loadStatus() {
  statusRequest += 1;
  const request = statusRequest;
  let data = null;
  try {
    const response = await authFetch(StatusApi, { cache: "no-store" });
    if (response.ok) {
      data = readStatus(await response.json());
    }
  } catch {
    data = null;
  }
  if (request !== statusRequest) {
    return true;
  }
  statusData = data === null ? "failed" : data;
  const applied = applyStatusLines();
  return data !== null && applied;
}

// Called by app.js each time the Backlogs view is shown.
function showStatus() {
  loadStatus();
}
