"use strict";

// Approval inbox: every [?] item of every project from /api/inbox, oldest proposal first, with Approve and Drop
// (sent through sendChange in edit.js with the version and line the inbox returned), and the waiting count in the
// nav. Loaded after edit.js and before live.js and app.js; their functions (element, titleText, openStory,
// jobHash, showCapture) are only called at run time. Only fixed texts and file text through textContent are shown,
// never server error text.

const InboxTexts = {
  loading: "Loading…",
  empty: "Nothing waiting for approval.",
  failed: "Can't load the inbox.",
  unreadable: ": backlog can't be read.",
  proposedBy: "Proposed by ",
  link: "Inbox",
};

const InboxRoute = "#inbox";
const InboxApi = "/api/inbox";
// The same rule the server applies; anything else is not linked.
const InboxJobId = /^[0-9]{1,6}-[a-z0-9]+(-[a-z0-9]+)*$/;

// null while the count is unknown (not loaded yet, or the last load failed): the badge is hidden then.
let inboxCount = null;
let inboxRequest = 0;
let inboxScroll = 0;

function inboxLink() {
  return document.querySelector(".nav a[data-view=\"inbox\"]");
}

// Shows the count; hidden when it is 0 or unknown. A load in progress keeps the last value, so it never flashes 0.
function showInboxCount() {
  const badge = document.getElementById("inbox-count");
  const link = inboxLink();
  const shown = inboxCount !== null && inboxCount > 0;
  badge.textContent = shown ? String(inboxCount) : "";
  badge.hidden = !shown;
  link.setAttribute("aria-label", shown ? InboxTexts.link + ", " + inboxCount + " waiting" : InboxTexts.link);
}

// The response as { items, unreadable } with only well-formed items, or null when it has another shape.
function readInbox(data) {
  if (!data || !Array.isArray(data.items) || !Array.isArray(data.unreadable)) {
    return null;
  }
  const items = [];
  for (const item of data.items) {
    const project = asText(item && item.project);
    const version = asText(item && item.version);
    const line = asNumber(item && item.line);
    if (project === "" || version === "" || line === null) {
      continue;
    }
    const job = asText(item.job);
    items.push({
      project: project,
      version: version,
      line: line,
      number: asNumber(item.number),
      title: asText(item.title),
      rest: asText(item.rest),
      job: InboxJobId.test(job) ? job : null,
    });
  }
  const unreadable = data.unreadable.filter((name) => typeof name === "string" && name !== "");
  return { items: items, unreadable: unreadable };
}

function inboxShown() {
  return !document.getElementById("inbox").hidden;
}

// The Inbox view in two parts, built once and kept: the capture form at the top (capture.js fills the section) and
// the list of proposed items under it. Only the list is ever replaced, so a typed idea survives every refresh.
function inboxParts() {
  const main = document.getElementById("inbox");
  let list = document.getElementById("inbox-list");
  if (!list) {
    const capture = element("section", "capture");
    capture.id = "inbox-capture";
    capture.setAttribute("aria-labelledby", "capture-title");
    list = element("div", "inbox-list-part");
    list.id = "inbox-list";
    main.replaceChildren(capture, list);
  }
  return list;
}

function showInboxContent(nodes) {
  inboxParts().replaceChildren(...nodes);
}

// Fetches the inbox once: the count always, the list only while the inbox is shown and no change is being sent
// (changeBusy). Returns false when the list should be fetched again (failed, or left alone while busy).
async function loadInbox() {
  inboxRequest += 1;
  const request = inboxRequest;
  let inbox;
  try {
    const response = await authFetch(InboxApi, { cache: "no-store" });
    if (!response.ok) {
      throw new Error("status");
    }
    inbox = readInbox(await response.json());
    if (inbox === null) {
      throw new Error("data");
    }
  } catch {
    if (request === inboxRequest) {
      inboxCount = null;
      showInboxCount();
      if (inboxShown() && !changeBusy) {
        showInboxContent([element("p", "error", InboxTexts.failed)]);
      }
    }
    return false;
  }
  if (request !== inboxRequest) {
    return true;
  }
  inboxCount = inbox.items.length;
  showInboxCount();
  if (!inboxShown()) {
    return true;
  }
  if (changeBusy) {
    return false;
  }
  const scroll = window.scrollY;
  renderInbox(inbox);
  window.scrollTo(0, scroll);
  return true;
}

// The count alone, at page load and after a token is saved; the inbox view fetches it with the list itself.
function loadInboxCount() {
  if (currentView !== "inbox") {
    loadInbox();
  }
}

function renderInbox(inbox) {
  const nodes = [];
  for (const name of inbox.unreadable) {
    nodes.push(element("p", "error inbox-unreadable", name + InboxTexts.unreadable));
  }
  if (inbox.items.length === 0) {
    nodes.push(element("p", "empty", InboxTexts.empty));
  } else {
    const list = element("ul", "lines inbox-list");
    for (const item of inbox.items) {
      list.appendChild(renderInboxItem(item));
    }
    nodes.push(list);
  }
  showInboxContent(nodes);
}

function renderInboxItem(item) {
  const row = element("li", "inbox-item");
  row.appendChild(element("span", "inbox-project", item.project));
  const label = (item.number !== null ? "#" + item.number + " " : "") + titleText(item.title);
  if (item.number !== null) {
    row.classList.add("numbered");
    const head = element("a", "head inbox-head", label);
    head.href = storyHash(item.project, item.number);
    head.addEventListener("click", (event) => {
      event.preventDefault();
      event.stopPropagation();
      openStory(item.project, item.number);
    });
    row.appendChild(head);
    // The whole row opens the story; the job link and the buttons act on their own.
    row.addEventListener("click", (event) => {
      if (event.target instanceof Element && event.target.closest("a, button")) {
        return;
      }
      openStory(item.project, item.number);
    });
  } else {
    row.appendChild(element("strong", "title inbox-head", label));
  }
  if (item.rest !== "") {
    row.appendChild(element("span", "rest", item.rest));
  }
  if (item.job !== null) {
    const job = element("a", "inbox-job", InboxTexts.proposedBy + item.job);
    job.href = jobHash(item.job);
    row.appendChild(job);
  }
  row.appendChild(inboxActions(item));
  return row;
}

// Approve and Drop for one item, named for screen readers by number (or title for an unnumbered line).
function inboxActions(item) {
  const subject = item.number !== null ? "#" + item.number : titleText(item.title);
  const actions = element("div", "edit-row inbox-actions");
  const approve = editButton(EditTexts.approve, "edit-primary", () => {
    sendInboxChange({ project: item.project, version: item.version, line: item.line, action: "approve" });
  });
  approve.setAttribute("aria-label", EditTexts.approve + " " + subject);
  const drop = editButton(EditTexts.drop, "edit-danger", () => {
    if (confirm(EditTexts.confirmDrop)) {
      sendInboxChange({ project: item.project, version: item.version, line: item.line, action: "drop" });
    }
  });
  drop.setAttribute("aria-label", EditTexts.drop + " " + subject);
  actions.append(approve, drop);
  return actions;
}

// sendChange shows the fixed text for the outcome; a committed change fetches the inbox and the count again.
async function sendInboxChange(body) {
  if (await sendChange(body)) {
    loadInbox();
  }
}

// Called by app.js when the inbox view is shown (its heading is the header title): the last list stays while it is
// fetched again, so nothing flashes, and the capture form at the top fetches its project list again.
function showInbox() {
  if (inboxParts().childElementCount === 0) {
    showInboxContent([element("p", "status", InboxTexts.loading)]);
  }
  showCapture();
  window.scrollTo(0, inboxScroll);
  loadInbox();
}

// Called by app.js when the inbox view is left, so Back from a story returns to the same place.
function rememberInboxScroll() {
  inboxScroll = window.scrollY;
}
