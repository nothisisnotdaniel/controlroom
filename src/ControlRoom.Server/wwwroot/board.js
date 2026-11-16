"use strict";

// Jobs board and job page. Loaded before app.js, which owns routing and the shared helpers (element, asText,
// asList, asNumber, renderBlock, storyHash); those are only called after both scripts have run.
// Everything is built with createElement and textContent; fixed texts only, never server or exception text.

const BoardTexts = {
  live: "Live · ",
  unreachable: "Can't reach the server.",
  noJobs: "No jobs yet.",
  loading: "Loading…",
  jobFailed: "Can't load this job.",
  recordUnreadable: "Can't read this record.",
  noRecords: "No records yet.",
  needsYou: "Needs you",
  allProjects: "All projects",
  task: "[h]",
  back: "Back",
  records: "Records",
};

const Stages = [
  { key: "spike", label: "Spike" },
  { key: "spec", label: "Spec" },
  { key: "build", label: "Build" },
  { key: "review", label: "Review" },
  { key: "approve", label: "Approve" },
  { key: "done", label: "Merged", collapsed: true },
];

const Reviewers = ["security", "ux", "ui", "code"];

const BoardRoute = "#jobs";
const JobRoute = "#job/";
const JobIdPattern = /^[0-9]{1,6}-[a-z0-9-]{1,80}$/;
const ProjectKey = "controlroom.project";
const PollInterval = 5000;
const Verdict = /^(VERDICT: )?(PASS|CHANGES|APPROVE|ESCALATE)$/;

let boardText = null;
let boardJobs = [];
let boardTasks = [];
let boardScroll = 0;
let boardPolling = false;
let mergedOpen = false;
let selectedProject = readSavedProject();

// Set when a card on the board opened the job page, so Back can go back in history.
let openedFromBoard = false;
let openJobId = null;
let openJobEntry = null;
let openJobRecord = null;
let jobRequest = 0;

function readSavedProject() {
  try {
    return localStorage.getItem(ProjectKey) || "";
  } catch {
    return "";
  }
}

function saveProject(name) {
  try {
    localStorage.setItem(ProjectKey, name);
  } catch {
    // Private mode or storage off: the filter still works for this page.
  }
}

function jobHash(id) {
  return JobRoute + encodeURIComponent(id);
}

// Returns null when the hash is not a job route, "" when it is one with a bad id.
function parseJobHash(hash) {
  if (!hash.startsWith(JobRoute)) {
    return null;
  }
  let id;
  try {
    id = decodeURIComponent(hash.slice(JobRoute.length));
  } catch {
    return "";
  }
  return JobIdPattern.test(id) ? id : "";
}

function formatTokens(count) {
  return count >= 1e6 ? (count / 1e6).toFixed(1) + "M" : Math.round(count / 1e3) + "k";
}

// Chip colours come from a fixed map, never from the value itself.
function chipClass(value) {
  if (value === "PASS" || value === "APPROVE") {
    return "chip chip-pass";
  }
  if (value === "CHANGES" || value === "ESCALATE") {
    return "chip chip-changes";
  }
  if (value === "pending") {
    return "chip chip-pending";
  }
  return "chip";
}

function asMap(value) {
  return value && typeof value === "object" && !Array.isArray(value) ? value : {};
}

function roundRow(label, reviews, past) {
  const row = element("div", past ? "round past" : "round");
  row.appendChild(element("span", "round-label", label));
  const map = asMap(reviews);
  for (const key of Reviewers) {
    const value = asText(map[key]) || "pending";
    if (value === "skipped") {
      continue;
    }
    const chip = element("span", chipClass(value), key);
    chip.title = key + ": " + value;
    row.appendChild(chip);
  }
  return row;
}

function modeText(job) {
  let text = asText(job.mode);
  if (job.path === "light") {
    text += " · light";
  } else if (job.path === "spike") {
    text += " · spike";
  }
  if (job.risk === "high") {
    text += " · high risk";
  }
  return text;
}

function rightText(job) {
  const round = asNumber(job.round);
  if (asText(job.decision)) {
    return asText(job.decision);
  }
  if (job.stage === "build" && round !== null && round > 1) {
    return "rework " + (round - 1);
  }
  return round ? "round " + round : "";
}

function renderCard(job) {
  const id = asText(job.id);
  const card = element("a", job.stage === "needs-human" ? "card card-human" : "card");
  card.href = jobHash(id);
  card.addEventListener("click", () => {
    openedFromBoard = true;
  });

  const head = element("div", "card-head");
  const name = element("span", "card-id");
  if (asText(job.project)) {
    name.appendChild(element("span", "card-project", asText(job.project)));
    name.appendChild(element("span", null, " · "));
  }
  name.appendChild(element("span", null, id));
  head.appendChild(name);
  if (asText(job.mode)) {
    head.appendChild(element("span", "card-mode", modeText(job)));
  }
  card.appendChild(head);
  card.appendChild(element("span", "card-title", asText(job.title) || id));

  const rounds = element("div", "rounds");
  const specReview = asText(job.specReview);
  if (specReview) {
    const row = element("div", job.stage === "spec" ? "round" : "round past");
    row.appendChild(element("span", "round-label", job.specRevised === true ? "Spec·2" : "Spec"));
    const chip = element("span", chipClass(specReview), "spec check");
    chip.title = "spec check: " + specReview;
    row.appendChild(chip);
    rounds.appendChild(row);
  }
  asList(job.pastRounds).forEach((reviews, index) => {
    rounds.appendChild(roundRow("R" + (index + 1), reviews, true));
  });
  const round = asNumber(job.round);
  if (round && job.stage !== "spec" && job.stage !== "build") {
    rounds.appendChild(roundRow("R" + round, job.reviews, false));
  }
  if (rounds.childElementCount > 0) {
    card.appendChild(rounds);
  }

  if (asText(job.note)) {
    card.appendChild(element("span", "card-note", asText(job.note)));
  }

  const foot = element("div", "card-foot");
  foot.appendChild(element("span", "card-branch", asText(job.branch)));
  const facts = [];
  const tokens = asNumber(job.tokens);
  if (tokens !== null) {
    facts.push(formatTokens(tokens) + " tok");
  }
  const ci = asText(job.ciStatus);
  if (ci && ci !== "skipped") {
    facts.push("CI " + ci);
  }
  const right = rightText(job);
  if (right) {
    facts.push(right);
  }
  foot.appendChild(element("span", "card-facts", facts.join(" · ")));
  card.appendChild(foot);
  return card;
}

function renderTask(task) {
  const project = asText(task.project);
  const number = asNumber(task.number);
  let card;
  if (number !== null) {
    card = element("a", "card card-task");
    card.href = storyHash(project, number);
    card.addEventListener("click", () => {
      openedFromList = true;
    });
  } else {
    card = element("div", "card card-task");
  }
  const head = element("div", "card-head");
  const name = element("span", "card-id");
  name.appendChild(element("span", "card-project", project));
  name.appendChild(element("span", null, " · " + BoardTexts.task + (number !== null ? " #" + number : "")));
  head.appendChild(name);
  card.appendChild(head);
  card.appendChild(element("span", "card-title", asText(task.title)));
  if (asText(task.rest)) {
    card.appendChild(element("span", "card-rest", asText(task.rest)));
  }
  return card;
}

function sectionHeading(label, count) {
  return label + " (" + count + ")";
}

function renderNeedsYou(jobs, tasks) {
  const section = element("section", "needs-you");
  const count = jobs.length + tasks.length;
  section.hidden = count === 0;
  section.appendChild(element("h2", null, sectionHeading(BoardTexts.needsYou, count)));
  const cards = element("div", "cards");
  for (const job of jobs) {
    cards.appendChild(renderCard(job));
  }
  for (const task of tasks) {
    cards.appendChild(renderTask(task));
  }
  section.appendChild(cards);
  return section;
}

function renderStage(stage, jobs) {
  const heading = sectionHeading(stage.label, jobs.length);
  const cards = element("div", "cards");
  for (const job of jobs) {
    cards.appendChild(renderCard(job));
  }
  if (stage.collapsed) {
    const details = element("details", "stage");
    details.open = mergedOpen;
    details.addEventListener("toggle", () => {
      mergedOpen = details.open;
    });
    details.appendChild(element("summary", null, heading));
    details.appendChild(cards);
    return details;
  }
  const section = element("section", "stage");
  section.appendChild(element("h2", null, heading));
  section.appendChild(cards);
  return section;
}

function projectNames() {
  const names = new Set();
  for (const job of boardJobs) {
    if (asText(job.project)) {
      names.add(job.project);
    }
  }
  return [...names].sort();
}

function renderFilter(names) {
  const filter = document.getElementById("board-filter");
  filter.hidden = names.length < 2;
  const buttons = [];
  for (const name of ["", ...names]) {
    const button = element("button", "filter-button", name || BoardTexts.allProjects);
    button.type = "button";
    button.setAttribute("aria-pressed", name === selectedProject ? "true" : "false");
    button.addEventListener("click", () => {
      selectedProject = name;
      saveProject(name);
      renderBoard();
    });
    buttons.push(button);
  }
  filter.replaceChildren(...buttons);
}

function renderBoard() {
  const names = projectNames();
  if (selectedProject && !names.includes(selectedProject)) {
    selectedProject = "";
  }
  renderFilter(names);
  const shown = selectedProject ? boardJobs.filter((job) => job.project === selectedProject) : boardJobs;
  const tasks = selectedProject ? boardTasks.filter((task) => task.project === selectedProject) : boardTasks;
  const nodes = [renderNeedsYou(shown.filter((job) => job.stage === "needs-human"), tasks)];
  if (boardJobs.length === 0) {
    nodes.push(element("p", "empty", BoardTexts.noJobs));
  } else {
    const stages = element("div", "stages");
    for (const stage of Stages) {
      stages.appendChild(renderStage(stage, shown.filter((job) => job.stage === stage.key)));
    }
    nodes.push(stages);
  }
  const content = document.getElementById("board-content");
  const scroll = window.scrollY;
  content.replaceChildren(...nodes);
  if (!document.getElementById("board").hidden) {
    window.scrollTo(0, scroll);
  }
}

function setBoardStatus(ok) {
  const status = document.getElementById("board-status");
  if (ok) {
    status.className = "live-status";
    status.textContent = BoardTexts.live + new Date().toLocaleTimeString();
  } else {
    status.className = "live-status error";
    status.textContent = BoardTexts.unreachable;
  }
}

function boardViewsVisible() {
  return !document.getElementById("board").hidden || !document.getElementById("job").hidden;
}

async function pollBoard() {
  if (document.visibilityState !== "visible" || !boardViewsVisible() || boardPolling) {
    return;
  }
  boardPolling = true;
  try {
    let text;
    let data;
    try {
      const response = await fetch("/api/board", { cache: "no-store" });
      if (!response.ok) {
        throw new Error("status");
      }
      text = await response.text();
      data = JSON.parse(text);
    } catch {
      setBoardStatus(false);
      return;
    }
    setBoardStatus(true);
    if (text === boardText) {
      return;
    }
    boardText = text;
    boardJobs = asList(data && data.jobs).filter((job) => job && typeof job === "object");
    boardTasks = asList(data && data.tasks).filter((task) => task && typeof task === "object");
    renderBoard();
    refreshOpenJob();
  } finally {
    boardPolling = false;
  }
}

// Reloads the open job page when its entry on the board has changed.
function refreshOpenJob() {
  if (openJobId === null || document.getElementById("job").hidden) {
    return;
  }
  const entry = boardJobs.find((job) => job.id === openJobId);
  if (entry && openJobEntry !== null && JSON.stringify(entry) !== openJobEntry) {
    loadJob(openJobId, true);
  }
}

function startBoardPolling() {
  setInterval(pollBoard, PollInterval);
  document.addEventListener("visibilitychange", () => {
    if (document.visibilityState === "visible") {
      pollBoard();
    }
  });
}

function showBoard() {
  openedFromBoard = false;
  leaveJob();
  window.scrollTo(0, boardScroll);
  pollBoard();
}

function rememberBoardScroll() {
  boardScroll = window.scrollY;
}

// Job page.

function leaveJob() {
  jobRequest += 1;
  openJobId = null;
  openJobEntry = null;
  openJobRecord = null;
  document.getElementById("job").replaceChildren();
}

function goBackToBoard() {
  if (openedFromBoard) {
    history.back();
    return;
  }
  history.replaceState(null, "", BoardRoute);
  route();
}

function jobBackButton() {
  const button = element("button", "back", BoardTexts.back);
  button.type = "button";
  button.addEventListener("click", goBackToBoard);
  return button;
}

function showJobContent(nodes) {
  const main = document.getElementById("job");
  const bar = element("div", "story-bar");
  bar.appendChild(jobBackButton());
  const footer = element("div", "story-footer");
  footer.appendChild(jobBackButton());
  main.replaceChildren(bar, ...nodes, footer);
}

function blockText(block) {
  return asList(block && block.inlines).map((inline) => asText(inline && inline.text)).join("").trim();
}

function renderRecord(record) {
  const panel = element("div", "record");
  if (!record || record.readable !== true) {
    panel.appendChild(element("p", "error", BoardTexts.recordUnreadable));
    return panel;
  }
  const blocks = asList(record.blocks);
  let start = 0;
  if (blocks.length > 0 && asText(blocks[0] && blocks[0].kind) === "paragraph") {
    const match = Verdict.exec(blockText(blocks[0]));
    if (match) {
      panel.appendChild(element("span", chipClass(match[2]) + " verdict", blockText(blocks[0])));
      start = 1;
    }
  }
  for (const block of blocks.slice(start)) {
    panel.appendChild(renderBlock(block));
  }
  return panel;
}

function renderTabs(records, onSelect) {
  const tabs = element("div", "tabs");
  tabs.setAttribute("role", "tablist");
  tabs.setAttribute("aria-label", BoardTexts.records);
  const buttons = records.map((record, index) => {
    const name = asText(record && record.name);
    const tab = element("button", "tab", asText(record && record.label));
    tab.type = "button";
    tab.id = "tab-" + index;
    tab.setAttribute("role", "tab");
    tab.setAttribute("aria-controls", "record-panel");
    const selected = name === openJobRecord;
    tab.setAttribute("aria-selected", selected ? "true" : "false");
    tab.tabIndex = selected ? 0 : -1;
    tab.addEventListener("click", () => onSelect(index));
    tab.addEventListener("keydown", (event) => {
      if (event.key === "ArrowRight" || event.key === "ArrowLeft") {
        event.preventDefault();
        const step = event.key === "ArrowRight" ? 1 : -1;
        onSelect((index + step + records.length) % records.length, true);
      }
    });
    return tab;
  });
  tabs.replaceChildren(...buttons);
  return tabs;
}

function renderJobPage(data) {
  const job = asMap(data && data.job);
  const records = asList(data && data.records);
  const names = records.map((record) => asText(record && record.name));
  if (!names.includes(openJobRecord)) {
    openJobRecord = names.length > 0 ? names[names.length - 1] : null;
  }

  const article = element("article", "job");
  const title = element("h1", "story-title", asText(job.title) || asText(job.id));
  title.tabIndex = -1;
  article.appendChild(title);
  const facts = [
    asText(job.id),
    asText(job.branch),
    asText(job.mode) ? job.mode + " mode" : "",
    asText(job.path) ? job.path + " path" : "",
    asText(job.risk) ? job.risk + " risk" : "",
  ].filter((fact) => fact !== "");
  article.appendChild(element("p", "job-facts", facts.join(" · ")));

  const panel = element("div", "record-panel");
  panel.id = "record-panel";
  panel.setAttribute("role", "tabpanel");
  let tabs = null;

  const select = (index, focus) => {
    openJobRecord = names[index];
    const next = renderTabs(records, select);
    tabs.replaceWith(next);
    tabs = next;
    showRecord(index);
    if (focus) {
      tabs.children[index].focus();
    }
  };
  const showRecord = (index) => {
    panel.setAttribute("aria-labelledby", "tab-" + index);
    panel.replaceChildren(renderRecord(records[index]));
  };

  if (records.length === 0) {
    article.appendChild(element("p", "empty", BoardTexts.noRecords));
  } else {
    tabs = renderTabs(records, select);
    article.appendChild(tabs);
    article.appendChild(panel);
    showRecord(names.indexOf(openJobRecord));
  }
  showJobContent([article]);
  return title;
}

async function loadJob(id, refresh) {
  jobRequest += 1;
  const request = jobRequest;
  if (!refresh) {
    openJobId = id;
    openJobEntry = null;
    openJobRecord = null;
    showJobContent([element("p", "status", BoardTexts.loading)]);
  }
  if (id === "") {
    showJobContent([element("p", "error", BoardTexts.jobFailed)]);
    return;
  }
  let data;
  try {
    const response = await fetch("/api/job?id=" + encodeURIComponent(id), { cache: "no-store" });
    if (!response.ok) {
      throw new Error("status");
    }
    data = await response.json();
  } catch {
    if (request === jobRequest && !refresh) {
      showJobContent([element("p", "error", BoardTexts.jobFailed)]);
      document.querySelector("#job .back").focus({ preventScroll: true });
    }
    return;
  }
  if (request !== jobRequest) {
    return;
  }
  openJobEntry = JSON.stringify(data && data.job);
  const scroll = window.scrollY;
  const title = renderJobPage(data);
  if (refresh) {
    window.scrollTo(0, scroll);
  } else {
    title.focus({ preventScroll: true });
  }
}

function showJob(id) {
  window.scrollTo(0, 0);
  loadJob(id, false);
  pollBoard();
}
