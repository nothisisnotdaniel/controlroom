"use strict";

// The two counts on the Backlogs tab icon (#135): the user's [h] tasks and the unapproved [?] items across every
// readable project, from /api/backlogs (the same answer the Backlogs view shows). Each badge is hidden at 0 or while
// the count is unknown (not loaded yet, or the last load failed); a shown one has a visually hidden text, so the link
// reads for example "Backlogs, 2 tasks, 5 unapproved". Loaded after edit.js and before live.js and app.js; their
// functions (element, asList, fetchBacklogs) are only called at run time. Only numbers and fixed texts are shown,
// through textContent.

const CountTexts = {
  task: " task",
  tasks: " tasks",
  unapproved: " unapproved",
};

// null while unknown: both badges are hidden then. A load in progress keeps the last values, so nothing flashes 0.
let backlogCounts = null;
let backlogCountsRequest = 0;

// One badge: the number for the eye (aria-hidden) and ", N <noun>" for screen readers; hidden at 0 or unknown.
function showCountBadge(id, count, noun) {
  const badge = document.getElementById(id);
  const shown = count !== null && count > 0;
  if (!shown) {
    badge.replaceChildren();
    badge.hidden = true;
    return;
  }
  const number = element("span", null, String(count));
  number.setAttribute("aria-hidden", "true");
  badge.replaceChildren(number, element("span", "visually-hidden", ", " + count + noun));
  badge.hidden = false;
}

function showBacklogCounts() {
  const tasks = backlogCounts === null ? null : backlogCounts.tasks;
  const approval = backlogCounts === null ? null : backlogCounts.approval;
  showCountBadge("tasks-count", tasks, tasks === 1 ? CountTexts.task : CountTexts.tasks);
  showCountBadge("approval-count", approval, CountTexts.unapproved);
}

// The [h] (user) and [?] (approval) entries of every readable project, or null when the answer has another shape.
function countBacklogs(data) {
  if (!data || !Array.isArray(data.projects)) {
    return null;
  }
  let tasks = 0;
  let approval = 0;
  for (const project of data.projects) {
    if (!project || project.readable !== true) {
      continue;
    }
    tasks += asList(project.user).length;
    approval += asList(project.approval).length;
  }
  return { tasks: tasks, approval: approval };
}

// Fetches the counts once; an older answer than the last request is dropped. Returns false when it failed, so the
// live check tries again.
async function loadBacklogCounts() {
  backlogCountsRequest += 1;
  const request = backlogCountsRequest;
  let counts;
  try {
    counts = countBacklogs(await fetchBacklogs());
    if (counts === null) {
      throw new Error("data");
    }
  } catch {
    if (request === backlogCountsRequest) {
      backlogCounts = null;
      showBacklogCounts();
    }
    return false;
  }
  if (request === backlogCountsRequest) {
    backlogCounts = counts;
    showBacklogCounts();
  }
  return true;
}
