"use strict";

// Fixed texts: never show server or exception text.
const Texts = {
  unreachable: "Can't reach the server.",
  unreadable: "Can't read backlog.md.",
  noLines: "No backlog lines.",
  noProjects: "No projects found.",
  storyFailed: "Can't load this story.",
  loading: "Loading…",
  back: "Back",
  waiting: "Waiting",
  after: "After:",
};

const Groups = [
  { key: "open", label: "Open" },
  { key: "approval", label: "Waiting for approval" },
  { key: "user", label: "For you" },
  { key: "done", label: "Done", collapsed: true },
];

const Marks = { " ": "[ ]", "?": "[?]", h: "[h]", x: "[x]" };

const StoryRoute = "#story/";

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

function renderNumberedLine(projectName, entry, number) {
  const item = element("li", "numbered");
  const head = element("a", "head");
  head.href = storyHash(projectName, number);
  head.appendChild(element("span", "mark", Marks[asText(entry.mark)] || Marks[" "]));
  head.appendChild(element("span", "number", "#" + number));
  head.appendChild(element("strong", "title", asText(entry.title)));
  if (entry.waiting === true) {
    head.appendChild(element("span", "badge waiting", Texts.waiting));
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

function renderLines(projectName, entries) {
  const list = element("ul", "lines");
  for (const entry of entries) {
    const number = asNumber(entry && entry.number);
    if (number !== null) {
      list.appendChild(renderNumberedLine(projectName, entry, number));
      continue;
    }
    const item = element("li");
    item.appendChild(element("strong", "title", asText(entry && entry.title)));
    const rest = asText(entry && entry.rest);
    if (rest !== "") {
      item.appendChild(element("span", "rest", rest));
    }
    list.appendChild(item);
  }
  return list;
}

function renderGroup(projectName, group, entries) {
  const heading = group.label + " (" + entries.length + ")";
  if (group.collapsed) {
    const details = element("details", "group");
    details.appendChild(element("summary", null, heading));
    details.appendChild(renderLines(projectName, entries));
    return details;
  }
  const section = element("section", "group");
  section.appendChild(element("h3", null, heading));
  section.appendChild(renderLines(projectName, entries));
  return section;
}

function renderProject(project) {
  const section = element("section", "project");
  const name = asText(project && project.name);
  section.appendChild(element("h2", null, asText(project && project.name)));
  if (!project || project.readable !== true) {
    section.appendChild(element("p", "error", Texts.unreadable));
    return section;
  }
  let shown = 0;
  for (const group of Groups) {
    const entries = asList(project[group.key]);
    if (entries.length > 0) {
      section.appendChild(renderGroup(name, group, entries));
      shown += 1;
    }
  }
  if (shown === 0) {
    section.appendChild(element("p", "empty", Texts.noLines));
  }
  return section;
}

function show(nodes) {
  const main = document.getElementById("backlogs");
  main.replaceChildren(...nodes);
}

async function load() {
  let data;
  try {
    const response = await fetch("/api/backlogs", { cache: "no-store" });
    if (!response.ok) {
      throw new Error("status");
    }
    data = await response.json();
  } catch {
    show([element("p", "error", Texts.unreachable)]);
    return;
  }
  const projects = asList(data && data.projects);
  if (projects.length === 0) {
    show([element("p", "empty", Texts.noProjects)]);
    return;
  }
  show(projects.map(renderProject));
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

function showStoryContent(nodes) {
  const main = document.getElementById("story");
  const bar = element("div", "story-bar");
  bar.appendChild(backButton());
  const footer = element("div", "story-footer");
  footer.appendChild(backButton());
  main.replaceChildren(bar, ...nodes, footer);
}

async function loadStory(target) {
  storyRequest += 1;
  const request = storyRequest;
  showStoryContent([element("p", "status", Texts.loading)]);
  let data;
  try {
    const response = await fetch(
      "/api/story?project=" + encodeURIComponent(target.project) + "&number=" + encodeURIComponent(target.number),
      { cache: "no-store" });
    if (!response.ok) {
      throw new Error("status");
    }
    data = await response.json();
  } catch {
    if (request === storyRequest) {
      showStoryContent([element("p", "error", Texts.storyFailed)]);
    }
    return;
  }
  if (request !== storyRequest) {
    return;
  }
  const heading = asText(data && data.project) + " #" + asNumber(data && data.number) + " " + asText(data && data.title);
  const article = element("article", "story");
  article.appendChild(element("h1", "story-title", heading));
  for (const block of asList(data && data.blocks)) {
    article.appendChild(renderBlock(block));
  }
  showStoryContent([article]);
}

function route() {
  const target = parseStoryHash(location.hash);
  const top = document.getElementById("top");
  const list = document.getElementById("backlogs");
  const story = document.getElementById("story");
  if (target) {
    if (story.hidden) {
      listScroll = window.scrollY;
    }
    top.hidden = true;
    list.hidden = true;
    story.hidden = false;
    window.scrollTo(0, 0);
    loadStory(target);
    return;
  }
  storyRequest += 1;
  openedFromList = false;
  story.hidden = true;
  story.replaceChildren();
  top.hidden = false;
  list.hidden = false;
  window.scrollTo(0, listScroll);
}

window.addEventListener("hashchange", route);
route();
load();
