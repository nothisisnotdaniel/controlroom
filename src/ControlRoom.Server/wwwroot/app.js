"use strict";

// Fixed texts: never show server or exception text.
const Texts = {
  unreachable: "Can't reach the server.",
  unreadable: "Can't read backlog.md.",
  noLines: "No backlog lines.",
  noProjects: "No projects found.",
};

const Groups = [
  { key: "open", label: "Open" },
  { key: "approval", label: "Waiting for approval" },
  { key: "user", label: "For you" },
  { key: "done", label: "Done", collapsed: true },
];

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

function renderLines(entries) {
  const list = element("ul", "lines");
  for (const entry of entries) {
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

function renderGroup(group, entries) {
  const heading = group.label + " (" + entries.length + ")";
  if (group.collapsed) {
    const details = element("details", "group");
    details.appendChild(element("summary", null, heading));
    details.appendChild(renderLines(entries));
    return details;
  }
  const section = element("section", "group");
  section.appendChild(element("h3", null, heading));
  section.appendChild(renderLines(entries));
  return section;
}

function renderProject(project) {
  const section = element("section", "project");
  section.appendChild(element("h2", null, asText(project && project.name)));
  if (!project || project.readable !== true) {
    section.appendChild(element("p", "error", Texts.unreadable));
    return section;
  }
  let shown = 0;
  for (const group of Groups) {
    const entries = asList(project[group.key]);
    if (entries.length > 0) {
      section.appendChild(renderGroup(group, entries));
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

load();
