using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Hosting;

namespace ControlRoom.Server.Tests;

/// <summary>The web files split between ControlRoom's and RoomKit's <c>wwwroot</c> (386): every path develop
/// served is still served at its URL with the same bytes and headers, in Development and Production, and the only
/// changed files differ by the lines moved to <c>room.js</c>.</summary>
public sealed class WebAssetSplitTests
{
    private static readonly string[] AppFiles =
    [
        "index.html", "manifest.webmanifest", "icons/apple-touch-icon.png", "icons/icon-192.png", "icons/icon-512.png",
        "app.js", "board.js", "act.js", "status.js", "releases.js", "room.js", "room.css",
    ];

    private static readonly string[] KitFiles =
    [
        "app.css", "sw.js", "token.js", "push.js", "edit.js", "counts.js", "search.js", "capture.js", "history.js",
        "doctor.js", "live.js",
    ];

    // Each moved line, by the RoomKit file it came from; it is in room.js verbatim.
    private static readonly Dictionary<string, string[]> MovedLines = new(StringComparer.Ordinal)
    {
        ["token.js"] = ["const TokenKey = \"controlroom-token\";"],
        ["capture.js"] = ["const CaptureProjectKey = \"controlroom-capture-project\";"],
        ["history.js"] = ["const HistoryProjectKey = \"controlroom-history-project\";"],
        ["push.js"] =
        [
            "  homeScreen: \"To get notifications on iPhone, add Control Room to the Home Screen and open it from there.\",",
            "  denied: \"Notifications are blocked for Control Room. Allow them in Settings, then check again.\",",
        ],
        ["sw.js"] = ["const CacheName = \"controlroom-shell-\" + CacheVersion;", "  updated: \"ControlRoom updated\","],
    };

    // Edits made after the split, each as (text now, text on develop then): the factory backlog section (402).
    // #407: job ids may end in "." plus 32 lower-case hex characters.
    private const string JobPatternNow = "{1,80}(\\.[0-9a-f]{32})?$/;";

    private const string JobPatternDevelop = "{1,80}$/;";

    private static readonly Dictionary<string, (string Now, string Develop)[]> LaterEdits = new(StringComparer.Ordinal)
    {
        ["app.js"] =
        [
            ("  noWorktree: \"The factory's develop worktree is missing. Run any job.py backlog command once to create it.\",\n  notDevelop: \"The factory's develop folder is not its develop worktree. Move it away or set FACTORY_DEVELOP_DIR, then reload.\",\n",
                ""),
            ("// Readable project names of the last backlogs answer, in response order (plus the factory when it has a reason, so\n// its fixed text can be chosen and read).\n",
                "// Readable project names of the last backlogs answer, in response order.\n"),
            ("// The factory's own backlog (#147): its section has no status or release line.\nconst FactoryName = \"factory\";\n\n// The fixed text for the factory entry's reason, or null for any other value; never server text.\nfunction reasonText(project) {\n  if (!project || asText(project.name) !== FactoryName) {\n    return null;\n  }\n  if (project.reason === \"no_worktree\") {\n    return Texts.noWorktree;\n  }\n  if (project.reason === \"not_develop\") {\n    return Texts.notDevelop;\n  }\n  return null;\n}\n\n",
                ""),
            ("    const reason = reasonText(project);\n    if (reason !== null) {\n      section.appendChild(element(\"p\", \"error\", reason));\n      return section;\n    }\n",
                ""),
            ("  if (name !== FactoryName) {\n    section.appendChild(statusLine(name));\n    section.appendChild(releaseBlock(name));\n  }\n",
                "  section.appendChild(statusLine(name));\n  section.appendChild(releaseBlock(name));\n"),
            ("  return projects\n    .filter((project) => (project && project.readable === true) || reasonText(project) !== null)\n    .map((project) => asText(project.name));\n",
                "  return projects.filter((project) => project && project.readable === true).map((project) => asText(project.name));\n"),
            // #409: other machines' item claims.
            ("  claimOn: \"In progress on \",\n  claimAnother: \"another machine\",\n  claimSince: \" since \",\n  claimChecked: \" · checked \",\n  claimMinAgo: \" min ago\",\n",
                ""),
            ("// Other machines' item claims per project (from /api/backlogs, set by renderProject): { items: Map(number -> claim),\n// stale, checked (ms or null) }. Only numbers and text values; never used as a path or HTML.\nconst projectClaims = new Map();\n\nfunction readClaims(project) {\n  const items = new Map();\n  for (const claim of asList(project && project.claims)) {\n    const item = asNumber(claim && claim.item);\n    if (item !== null && !items.has(item)) {\n      items.set(item, claim);\n    }\n  }\n  const checked = typeof (project && project.claimsChecked) === \"string\" ? Date.parse(project.claimsChecked) : NaN;\n  return { items: items, stale: project && project.claimsStale === true, checked: Number.isNaN(checked) ? null : checked };\n}\n\n// \"In progress on <machine> since <local time>\" (another machine when it is unknown, no since part without a valid\n// time), plus \" · checked <n> min ago\" while the claims are stale; built only with textContent.\nfunction claimBadge(projectName, number) {\n  const claims = projectClaims.get(projectName);\n  const claim = claims ? claims.items.get(number) : undefined;\n  if (!claim) {\n    return null;\n  }\n  const machine = asText(claim.machine).trim();\n  let text = Texts.claimOn + (machine !== \"\" ? machine : Texts.claimAnother);\n  const claimed = typeof claim.claimed === \"string\" ? new Date(claim.claimed) : null;\n  if (claimed !== null && !Number.isNaN(claimed.getTime())) {\n    text += Texts.claimSince + claimed.toLocaleString([], {\n      month: \"short\", day: \"numeric\", hour: \"2-digit\", minute: \"2-digit\",\n    });\n  }\n  if (claims.stale && claims.checked !== null) {\n    text += Texts.claimChecked + Math.max(0, Math.floor((Date.now() - claims.checked) / 60000)) + Texts.claimMinAgo;\n  }\n  const badge = document.createElement(\"span\");\n  badge.className = \"claim-badge\";\n  badge.textContent = text;\n  return badge;\n}\n\n",
                ""),
            ("  const claim = claimBadge(projectName, number);\n  if (claim !== null) {\n    head.appendChild(claim);\n  }\n",
                ""),
            ("  // Other machines' claims too; the checked time only while stale (the badge shows its age then).\n  const stale = project.claimsStale === true;\n  const claims = [asList(project.claims), stale, stale ? project.claimsChecked || null : null];\n  return JSON.stringify([project.next || null, project.stopped === true, asList(project.blocked), stop, claims]);\n",
                "  return JSON.stringify([project.next || null, project.stopped === true, asList(project.blocked), stop]);\n"),
            ("  projectClaims.set(name, readClaims(project));\n",
                ""),
            // #472: the RoomKit 0.2.0 host hooks for the open job page and the decision check.
            ("\n// RoomKit host hooks, registered at load time (after counts.js and live.js): every counts read of the board is checked\n// against the open job page (board.js), and each live check verifies an open decision (act.js) before the shown\n// views refresh; its promise holds that refresh until the check has started.\nroomKitHooks.on(\"jobCounts\", (jobs) => {\n  refreshOpenJob(jobs);\n});\nroomKitHooks.on(\"liveChecked\", ({ jobsChanged, recovered, firstCheck }) =>\n  recheckPendingCancel(jobsChanged || recovered || firstCheck, { includeForm: firstCheck }));\n\n// After Save: show the current view again and reload its data, the backlogs and the counts on the Backlogs and Jobs\n",
                "\n// After Save: show the current view again and reload its data, the backlogs and the counts on the Backlogs and Jobs\n"),
            // #475: History and Health behind the More button, and the #health alias.
            ("const TabForView = { job: \"board\", story: \"backlogs\" };\n// History and Health are behind the More button (#178): while one is shown, the button and its menu link are marked.\nconst MoreViews = new Set([\"history\", \"doctor\"]);\n// Another name for Health: it opens the Health view and becomes #doctor in place.\nconst HealthRoute = \"#health\";\n",
                "const TabForView = { job: \"board\", story: \"backlogs\" };\n"),
            ("  if (hash === HealthRoute) {\n    return \"doctor\";\n  }\n",
                ""),
            ("// The More menu (#178): plain links above the tab bar that #tab-more shows and hides (a disclosure, not role=\"menu\").\nfunction moreMenuOpen() {\n  return !document.getElementById(\"tab-more-menu\").hidden;\n}\n\nfunction openMoreMenu() {\n  document.getElementById(\"tab-more-menu\").hidden = false;\n  document.getElementById(\"tab-more\").setAttribute(\"aria-expanded\", \"true\");\n}\n\nfunction closeMoreMenu() {\n  document.getElementById(\"tab-more-menu\").hidden = true;\n  document.getElementById(\"tab-more\").setAttribute(\"aria-expanded\", \"false\");\n}\n\nfunction toggleMoreMenu() {\n  if (moreMenuOpen()) {\n    closeMoreMenu();\n  } else {\n    openMoreMenu();\n  }\n}\n\n// The button toggles the menu; choosing a link, a tap or click outside the button and the menu, and Escape close it.\n// Escape moves focus back to the button.\nfunction setUpMoreMenu() {\n  const button = document.getElementById(\"tab-more\");\n  const menu = document.getElementById(\"tab-more-menu\");\n  button.addEventListener(\"click\", toggleMoreMenu);\n  for (const link of menu.querySelectorAll(\"a\")) {\n    link.addEventListener(\"click\", closeMoreMenu);\n  }\n  document.addEventListener(\"pointerdown\", (event) => {\n    if (moreMenuOpen() && !button.contains(event.target) && !menu.contains(event.target)) {\n      closeMoreMenu();\n    }\n  });\n  document.addEventListener(\"keydown\", (event) => {\n    if (event.key === \"Escape\" && moreMenuOpen()) {\n      closeMoreMenu();\n      button.focus();\n    }\n  });\n}\n\n",
                ""),
            ("function showView(name) {\n  // Every view change closes the More menu, so it never stays open over a new view or the token view.\n  closeMoreMenu();\n",
                "function showView(name) {\n"),
            ("  // History and Health: the More button and the matching menu link carry the mark, and no other link does.\n  const more = MoreViews.has(current);\n  const button = document.getElementById(\"tab-more\");\n  if (more) {\n    button.setAttribute(\"aria-current\", \"page\");\n  } else {\n    button.removeAttribute(\"aria-current\");\n  }\n  for (const link of document.querySelectorAll(\"#tab-more-menu a\")) {\n    if (more && link.dataset.view === current) {\n      link.setAttribute(\"aria-current\", \"page\");\n    } else {\n      link.removeAttribute(\"aria-current\");\n    }\n  }\n",
                ""),
            ("  // #health becomes #doctor in place (no new history entry).\n  if (location.hash === HealthRoute) {\n    history.replaceState(null, \"\", DoctorRoute);\n  }\n",
                ""),
            ("setUpMoreMenu();\nwindow.addEventListener(\"hashchange\", route);\n",
                "window.addEventListener(\"hashchange\", route);\n"),
        ],
        ["edit.js"] =
        [
            ("  no_worktree: \"The factory's develop worktree is missing. Nothing was changed.\",\n",
                ""),
        ],
        ["board.js"] =
        [
            // #472: the open job page follows every board answer, with one bounded detail GET per board entry.
            ("  pollBoard();\n  await opening.loaded;\n  opening.loaded = null;\n}\n",
                "  pollBoard();\n}\n"),
            ("  window.scrollTo(0, 0);\n  const opening = { needsCheck: false };\n  openingJob = opening;\n  opening.loaded = loadJob(id, false);\n",
                "  window.scrollTo(0, 0);\n  loadJob(id, false);\n"),
            ("\nasync function showJob(id) {\n",
                "\nfunction showJob(id) {\n"),
            ("  const title = renderJobPage(data);\n  if (conflict !== undefined) {\n    observeDecisionBoard(conflict);\n  }\n  if ((conflict !== undefined || (!refresh && openingJob?.needsCheck)) && typeof recheckPendingCancel === \"function\") {\n    recheckPendingCancel(true, { includeForm: true });\n  }\n",
                "  const title = renderJobPage(data);\n"),
            ("  }\n  jobRequest = null;\n  // These snapshots overlapped this GET. A disagreement in either delivery order needs a new read.\n  const conflict = typeof boardMatchesDecision === \"function\" ?\n    request.boards.find(entry => !boardMatchesDecision(entry, data?.job, data?.request)) : undefined;\n  if (refresh && conflict !== undefined && document.getElementById(\"act\")) {\n    recheckPendingCancel(true, { includeForm: true });\n    return;\n  }\n  if (refresh && jobDecisionHeld(data?.job, data?.request ?? null)) {\n",
                "  }\n  if (refresh && jobDecisionHeld()) {\n"),
            ("  } catch {\n    if (request === jobRequest) {\n      jobRequest = null;\n      if (typeof decisionDetailFailed === \"function\") {\n        decisionDetailFailed();\n      }\n      if (!refresh) {\n        showJobContent([element(\"p\", \"error\", BoardTexts.jobFailed)]);\n        document.querySelector(\"#job .back\").focus({ preventScroll: true });\n      }\n",
                "  } catch {\n    if (request === jobRequest && !refresh) {\n      showJobContent([element(\"p\", \"error\", BoardTexts.jobFailed)]);\n      document.querySelector(\"#job .back\").focus({ preventScroll: true });\n"),
            ("  try {\n    const response = await authFetch(\"/api/job?id=\" + encodeURIComponent(id),\n      { cache: \"no-store\", signal: AbortSignal.timeout(JobDetailTimeout) });\n",
                "  try {\n    const response = await authFetch(\"/api/job?id=\" + encodeURIComponent(id), { cache: \"no-store\" });\n"),
            ("  if (id === \"\") {\n    jobRequest = null;\n",
                "  if (id === \"\") {\n"),
            ("    openJobRecord = null;\n    jobRefreshHeld = false;\n",
                "    openJobRecord = null;\n"),
            ("\nasync function loadJob(id, refresh, entryText = null) {\n  if (refresh && openingJob?.loaded && openJobEntry === null) {\n    openingJob.needsCheck = true;\n    return;\n  }\n  if (refresh && openingJob) {\n    openingJob.needsCheck = false;\n  }\n  const request = { boards: [], entry: entryText };\n  jobRequest = request;\n",
                "\nasync function loadJob(id, refresh) {\n  jobRequest += 1;\n  const request = jobRequest;\n"),
            ("  openJobId = null;\n  openingJob = null;\n",
                "  openJobId = null;\n"),
            ("function leaveJob() {\n  jobRequest = null;\n",
                "function leaveJob() {\n  jobRequest += 1;\n"),
            ("// refresh never rebuilds the page then.\nfunction jobDecisionHeld(job, request) {\n  return typeof jobDecisionBusy === \"function\" && jobDecisionBusy(job, request);\n",
                "// refresh never rebuilds the page then.\nfunction jobDecisionHeld() {\n  return typeof jobDecisionBusy === \"function\" && jobDecisionBusy();\n"),
            ("    }\n    // A detail GET slower than the poll still applies: no new one for the board entry it was started for.\n    const entryText = JSON.stringify(entry);\n    if (jobRequest !== null && jobRequest.entry === entryText) {\n      return;\n    }\n    loadJob(openJobId, true, entryText);\n",
                "    }\n    loadJob(openJobId, true);\n"),
            ("    if (jobDecisionHeld()) {\n      if (conflict) {\n        // The held refresh must also supersede a detail that was already in flight.\n        jobRequest = null;\n      }\n",
                "    if (jobDecisionHeld()) {\n"),
            ("  }\n  const entry = jobs.find(job => job?.id === openJobId) ?? null;\n  jobRequest?.boards.push(entry);\n  if (openJobEntry === null) {\n    return;\n  }\n  const conflict = typeof observeDecisionBoard === \"function\" && observeDecisionBoard(entry);\n  if (conflict || (entry && JSON.stringify(entry) !== openJobEntry)) {\n",
                "  }\n  const entry = boardJobs.find((job) => job.id === openJobId);\n  if (entry && openJobEntry !== null && JSON.stringify(entry) !== openJobEntry) {\n"),
            ("\n// Every board read can contradict a detail or POST, even when the board itself has identical bytes.\nfunction refreshOpenJob(jobs) {\n  if (openJobId === null || document.getElementById(\"job\").hidden || !Array.isArray(jobs)) {\n",
                "\n// Reloads the open job page when its entry on the board has changed.\nfunction refreshOpenJob() {\n  if (openJobId === null || document.getElementById(\"job\").hidden) {\n"),
            ("    renderBoard();\n    return true;\n",
                "    renderBoard();\n    refreshOpenJob();\n    return true;\n"),
            ("    loadUpdate();\n    refreshOpenJob(data?.jobs);\n",
                "    loadUpdate();\n"),
            ("let openJobRecord = null;\nlet jobRequest = null;\n",
                "let openJobRecord = null;\nlet jobRequest = 0;\n"),
            ("let openJobId = null;\nlet openingJob = null;\n",
                "let openJobId = null;\n"),
            ("const PollInterval = 5000;\nconst JobDetailTimeout = 10000;\n",
                "const PollInterval = 5000;\n"),
            ("const JobIdPattern = /^[0-9]{1,6}-[a-z0-9-]" + JobPatternNow,
                "const JobIdPattern = /^[0-9]{1,6}-[a-z0-9-]" + JobPatternDevelop),
        ],
        ["releases.js"] = [("const ReleaseJobPattern = /^[0-9]{1,6}-[a-z0-9-]" + JobPatternNow,
            "const ReleaseJobPattern = /^[0-9]{1,6}-[a-z0-9-]" + JobPatternDevelop)],
        // #409: other machines' item claims.
        ["app.css"] =
        [
            // #405: the unused backlog heading selector.
            ("\n.group summary {\n  font-size: 0.85rem;\n",
                "\n.group h3,\n.group summary {\n  font-size: 0.85rem;\n"),
            ("  /* Another machine's claim on a backlog item (factory#54). */\n  --claim-bg: #ccfbf1;\n  --claim-fg: #115e59;\n",
                ""),
            ("    --claim-bg: #134e4a;\n    --claim-fg: #99f6e4;\n",
                ""),
            ("\n/* Another machine's claim on a numbered row, after the category chip. Its text is long, so it wraps inside the row\n   on a narrow screen instead of pushing the row wider. */\n.claim-badge {\n  display: inline-block;\n  max-width: 100%;\n  font-size: 0.8rem;\n  font-weight: 600;\n  line-height: 1.4;\n  padding: 0 6px;\n  border-radius: 6px;\n  margin: 2px 0 0 6px;\n  vertical-align: 1px;\n  white-space: normal;\n  overflow-wrap: anywhere;\n  background: var(--claim-bg);\n  color: var(--claim-fg);\n}\n",
                ""),
        ],
        // #409: other machines' item claims.
        ["live.js"] =
        [
            ("// The response as { jobs, projects: Map(name -> { backlog, stories, claims }) }, or null when it has another shape.\n// claims (other machines' item claims) is \"\" when the server sends none.\n",
                "// The response as { jobs, projects: Map(name -> { backlog, stories }) }, or null when it has another shape.\n"),
            ("    if (!value || typeof value.backlog !== \"string\" || typeof value.stories !== \"string\"\n      || (value.claims !== undefined && typeof value.claims !== \"string\")) {\n      return null;\n    }\n    projects.set(name, { backlog: value.backlog, stories: value.stories, claims: value.claims || \"\" });\n",
                "    if (!value || typeof value.backlog !== \"string\" || typeof value.stories !== \"string\") {\n      return null;\n    }\n    projects.set(name, { backlog: value.backlog, stories: value.stories });\n"),
            ("    // Another machine's claims changed: the section is fetched again, like a changed backlog.\n    if (!backlogChanged && before.claims !== after.claims) {\n      staleBacklogs.add(name);\n    }\n",
                ""),
            // RoomKit 0.2.0: the "liveChecked" host hooks.
            ("    noteChanges(next, recovered);\n    // The \"liveChecked\" host hooks (counts.js) run while the board views are shown, before the views refresh.\n    if (boardViewsVisible()) {\n      await runRoomKitHooks(\"liveChecked\", {\n        jobsChanged: previous !== null && previous.jobs !== next.jobs,\n        recovered: recovered,\n        firstCheck: previous === null,\n      });\n    }\n",
                "    noteChanges(next, recovered);\n"),
            ("      return;\n    }\n    const previous = liveLast;\n",
                "      return;\n    }\n"),
        ],
        // RoomKit 0.2.0: the host hook registry and the "jobCounts" hooks.
        ["counts.js"] =
        [
            ("    jobCounts = counts;\n    showJobCounts();\n    await runRoomKitHooks(\"jobCounts\", jobs);\n",
                "    jobCounts = counts;\n    showJobCounts();\n"),
            ("    const data = JSON.parse(await fetchBoard());\n    jobs = data && data.jobs;\n    counts = countJobs(jobs);\n",
                "    const data = JSON.parse(await fetchBoard());\n    counts = countJobs(data && data.jobs);\n"),
            ("  const request = jobCountsRequest;\n  let counts;\n  let jobs;\n",
                "  const request = jobCountsRequest;\n  let counts;\n"),
            ("// Fetches the board once for the counts; an older answer than the last request is dropped. Returns false when it\n// failed, so the live check tries again. After new counts are set, the \"jobCounts\" host hooks get the answer's jobs.\n",
                "// Fetches the board once for the counts; an older answer than the last request is dropped. Returns false when it\n// failed, so the live check tries again.\n"),
            ("// every board answer it reads.\n\n// Host hooks (RoomKit #5): a host app's script, loaded after this one, registers its own code with\n// roomKitHooks.on(name, fn) for one of HostHookNames: \"jobCounts\" runs after loadJobCounts has set new counts (given\n// the board answer's jobs), \"liveChecked\" after each live check while the board views are shown (live.js, given\n// { jobsChanged, recovered, firstCheck }). A name's hooks run in registration order, each awaited; one that throws\n// or rejects is written to console.error and the next one runs. The registry is frozen and its list is closed over,\n// so no script can replace or reach it; runRoomKitHooks is only for counts.js and live.js.\nconst HostHookNames = Object.freeze([\"jobCounts\", \"liveChecked\"]);\n\nconst [roomKitHooks, runRoomKitHooks] = (() => {\n  const hooks = new Map(HostHookNames.map((name) => [name, []]));\n\n  function on(name, fn) {\n    if (typeof name !== \"string\" || !hooks.has(name)) {\n      throw new TypeError(\"roomKitHooks.on: unknown hook name\");\n    }\n    if (typeof fn !== \"function\") {\n      throw new TypeError(\"roomKitHooks.on: the hook is not a function\");\n    }\n    hooks.get(name).push(fn);\n  }\n\n  async function run(name, argument) {\n    for (const fn of [...hooks.get(name)]) {\n      try {\n        await fn(argument);\n      } catch (error) {\n        console.error(\"roomKitHooks: a \" + name + \" hook failed\", error);\n      }\n    }\n  }\n\n  return [Object.freeze({ on: on }), run];\n})();\n",
                "// every board answer it reads.\n"),
        ],
        // RoomKit 0.2.0: setRoomKitBadge; push.js is checked against its develop copy, so Push_ReplacesItsTwoTextsWithTheAppTexts applies it.
        ["push.js"] =
        [
            ("    await pushJson(\"/api/push/settings\", { notifyUpdate: on });\n  });\n}\n\n// Sets the app icon's badge to count (a whole number 1..999) or clears it (0) through the Badging API, for a host app\n// while it is open. Any other value does nothing. Resolves to true when a Badging call was made and settled, else\n// false (invalid value, no Badging API, or the call failed); never throws or rejects.\nasync function setRoomKitBadge(count) {\n  if (!Number.isInteger(count) || count < 0 || count > 999) {\n    return false;\n  }\n  try {\n    if (count === 0) {\n      if (typeof navigator.clearAppBadge !== \"function\") {\n        return false;\n      }\n      await navigator.clearAppBadge();\n    } else {\n      if (typeof navigator.setAppBadge !== \"function\") {\n        return false;\n      }\n      await navigator.setAppBadge(count);\n    }\n    return true;\n  } catch {\n    return false;\n  }\n}\n\n",
                "    await pushJson(\"/api/push/settings\", { notifyUpdate: on });\n  });\n}\n\n"),
        ],
    };

    // #411: the service worker's edits after the split, each as (text now, text on develop then).
    private static readonly (string Now, string Develop)[] ServiceWorkerEdits =
    [
        ("// app's fixed text instead of old data. Shell files load network-first: the cache answers only when the network\n// fails, so an update reaches the app on its next load. A new worker takes over at once and drops every older\n// cache. Bump CacheVersion in room.js whenever a shell file changes.\n",
            "// app's fixed text instead of old data. Bump the version when the shell list changes.\n"),
        ("  event.waitUntil(caches.open(CacheName)\n    .then((cache) => cache.addAll(Shell.map((path) => new Request(path, { cache: \"no-cache\" }))))\n    .then(() => self.skipWaiting()));\n",
            "  event.waitUntil(caches.open(CacheName).then((cache) => cache.addAll(Shell)));\n"),
        ("      Promise.all(names.filter((name) => name !== CacheName).map((name) => caches.delete(name))))\n      .then(() => self.clients.claim()));\n",
            "      Promise.all(names.filter((name) => name !== CacheName).map((name) => caches.delete(name)))));\n"),
        ("async function networkFirst(event) {\n  // One entry per shell path: the key is the bare pathname, so a query string never adds an entry.\n  const key = new URL(event.request.url).pathname;\n  const cache = await caches.open(CacheName);\n  let response;\n  try {\n    response = await fetch(event.request, { cache: \"no-cache\" });\n  } catch {\n    // No network answer (offline or unreachable): the cached copy, else a network error.\n    const cached = await cache.match(key);\n    return cached || Response.error();\n  }\n  if (response.ok) {\n    try {\n      await cache.put(key, response.clone());\n    } catch {\n      // A full or unavailable cache keeps the old copy; the network answer is still returned.\n    }\n  }\n  return response;\n}\n",
            "async function staleWhileRevalidate(event) {\n  // One entry per shell path: the key is the bare pathname, so a query string never adds an entry.\n  const key = new URL(event.request.url).pathname;\n  const cache = await caches.open(CacheName);\n  const cached = await cache.match(key);\n  const network = fetch(event.request).then(async (response) => {\n    if (response.ok) {\n      await cache.put(key, response.clone());\n    }\n    return response;\n  });\n  if (cached) {\n    event.waitUntil(network.catch(() => undefined));\n    return cached;\n  }\n  return network;\n}\n"),
        ("  event.respondWith(networkFirst(event));\n", "  event.respondWith(staleWhileRevalidate(event));\n"),
        // RoomKit 0.2.0: the push payload's badge.
        ("self.addEventListener(\"push\", (event) => {\n  const data = readPushData(event);\n  const note = pushNotification(data);\n  event.waitUntil(Promise.all([\n    updatePushBadge(pushBadge(data)),\n    self.registration.showNotification(note.title, note.options),\n  ]));\n",
            "self.addEventListener(\"push\", (event) => {\n  const note = pushNotification(readPushData(event));\n  event.waitUntil(self.registration.showNotification(note.title, note.options));\n"),
        ("  };\n}\n\n// The payload's badge when it is a whole number 0..999, else null (the badge stays as it is).\nfunction pushBadge(data) {\n  const badge = data.badge;\n  return Number.isInteger(badge) && badge >= 0 && badge <= 999 ? badge : null;\n}\n\n// Sets (1..999) or clears (0) the app icon's badge through the Badging API. Never rejects: a missing or failing API\n// leaves the badge as it is and the notification is shown either way.\nasync function updatePushBadge(badge) {\n  if (badge === null) {\n    return;\n  }\n  try {\n    if (badge === 0) {\n      if (typeof navigator.clearAppBadge === \"function\") {\n        await navigator.clearAppBadge();\n      }\n    } else if (typeof navigator.setAppBadge === \"function\") {\n      await navigator.setAppBadge(badge);\n    }\n  } catch {\n    // No badge on this device: the notification still counts.\n  }\n}\n\n",
            "  };\n}\n\n"),
        ("\n// Push: the payload holds only kind, project, job, stage, version, previous, reason, count and badge. The title comes from this fixed table,\n// never from the payload; the body is built from the payload's names. badge (a whole number 0..999, separate from\n// count) only sets the app icon's badge, 0 clears it, and any other value leaves it as it is. Nothing is fetched here.\n",
            "\n// Push: the payload holds only kind, project, job, stage, version, previous, reason and count. The title comes from this fixed table,\n// never from the payload; the body is built from the payload's names. Nothing is fetched here.\n"),
    ];

    private static readonly string[] KitGlobals =
        ["TokenKey", "CaptureProjectKey", "HistoryProjectKey", "CacheVersion", "CacheName", "Shell"];

    [Fact]
    public void Folders_HoldEveryDevelopPathInExactlyOneOfThem()
    {
        string[] app = Listing(AppFolder());
        string[] kit = Listing(KitFolder());

        Assert.Equal(AppFiles.Order(StringComparer.Ordinal), app);
        Assert.Equal(KitFiles.Order(StringComparer.Ordinal), kit);
        Assert.Empty(app.Intersect(kit, StringComparer.Ordinal));
        string[] develop = [.. DevelopHashes().Keys.Order(StringComparer.Ordinal)];
        // room.js and room.css are app files that develop never had, so they have no develop hash.
        Assert.Equal(develop, app.Concat(kit).Where(path => path is not ("room.js" or "room.css")).Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public async Task EveryPath_IsServedWithItsBytesTypeAndCacheHeader(string environment)
    {
        using TempFactoryRoot root = new();
        using ServerFactory server = new(root.Path);
        using Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> factory =
            server.WithWebHostBuilder(builder => builder.UseEnvironment(environment));
        HttpClient client = factory.CreateClient();
        client.BaseAddress = new Uri("http://127.0.0.1:8100/");

        foreach ((string folder, string[] files) in new[] { (AppFolder(), AppFiles), (KitFolder(), KitFiles) })
        {
            foreach (string path in files)
            {
                using HttpResponseMessage response = await client.GetAsync("/" + path);
                Assert.True(response.StatusCode == System.Net.HttpStatusCode.OK, path + " gave " + response.StatusCode);
                Assert.Equal(ContentType(path), response.Content.Headers.ContentType?.ToString());
                Assert.Equal(path == "sw.js" ? "no-cache" : null, response.Headers.CacheControl?.ToString());
                byte[] body = await response.Content.ReadAsByteArrayAsync();
                Assert.Equal(File.ReadAllBytes(Path.Combine(folder, path)), body);
            }
        }
    }

    [Fact]
    public void UnchangedFiles_AreByteIdenticalToDevelop()
    {
        string[] changed = ["sw.js", "token.js", "push.js", "capture.js", "history.js", "index.html"];
        foreach ((string path, string hash) in DevelopHashes())
        {
            if (changed.Contains(path))
            {
                Assert.Equal(hash, Sha256(File.ReadAllBytes(Path.Combine(DevelopFolder(), path + ".develop"))));
                continue;
            }

            string folder = AppFiles.Contains(path) ? AppFolder() : KitFolder();
            byte[] bytes = File.ReadAllBytes(Path.Combine(folder, path));
            if (path == "board.js")
            {
                // The Windows install hint is the only addition since the asset split.
                const string WindowsHint = """
                      if (command.startsWith(".\\launch-agent.ps1 ")) {
                        text.append(" Run this command in PowerShell from the extracted release folder.");
                      }
                    """;
                string script = Encoding.UTF8.GetString(bytes);
                Assert.Contains(WindowsHint, script, StringComparison.Ordinal);
                bytes = Encoding.UTF8.GetBytes(script.Replace(WindowsHint + "\n", "", StringComparison.Ordinal));
            }

            if (LaterEdits.TryGetValue(path, out (string Now, string Develop)[]? edits))
            {
                string script = Encoding.UTF8.GetString(bytes);
                foreach ((string now, string develop) in edits)
                {
                    Assert.Contains(now, script, StringComparison.Ordinal);
                    script = script.Replace(now, develop, StringComparison.Ordinal);
                }

                bytes = Encoding.UTF8.GetBytes(script);
            }

            Assert.True(hash == Sha256(bytes), path + " changed");
        }
    }

    [Theory]
    [InlineData("token.js")]
    [InlineData("capture.js")]
    [InlineData("history.js")]
    public void SimpleFiles_LoseOnlyTheirMovedLine(string file)
    {
        List<string> develop = Lines(Path.Combine(DevelopFolder(), file + ".develop"));
        Assert.True(develop.Remove(MovedLines[file][0]));

        Assert.Equal(develop, Lines(Path.Combine(KitFolder(), file)));
    }

    [Fact]
    public void Push_ReplacesItsTwoTextsWithTheAppTexts()
    {
        List<string> develop = Lines(Path.Combine(DevelopFolder(), "push.js.develop"));
        int home = develop.IndexOf(MovedLines["push.js"][0]);
        Assert.True(home >= 0);
        develop[home] = "  ...PushAppTexts,";
        Assert.True(develop.Remove(MovedLines["push.js"][1]));
        string script = string.Join('\n', develop);
        foreach ((string now, string before) in LaterEdits["push.js"])
        {
            Assert.Contains(before, script, StringComparison.Ordinal);
            script = script.Replace(before, now, StringComparison.Ordinal);
        }

        Assert.Equal(script.Split('\n').ToList(), Lines(Path.Combine(KitFolder(), "push.js")));
    }

    [Fact]
    public void ServiceWorker_LosesOnlyTheMovedLinesAndReadsTheirNames()
    {
        List<string> develop = Lines(Path.Combine(DevelopFolder(), "sw.js.develop"));
        List<string> room = Lines(Path.Combine(AppFolder(), "room.js"));
        int start = develop.FindIndex(line => line.StartsWith("const CacheVersion = ", StringComparison.Ordinal));
        int end = develop.IndexOf("];", start);
        Assert.True(start >= 0 && end > start);
        // The shell list block moved, with "/room.js" added and the version raised.
        List<string> block = develop.GetRange(start, end - start + 1);
        block[0] = "const CacheVersion = \"v27\";";
        block.Insert(block.IndexOf("  \"/token.js\",") + 1, "  \"/room.js\",");
        block.Insert(block.IndexOf("  \"/app.css\",") + 1, "  \"/room.css\",");
        int roomStart = room.IndexOf("const CacheVersion = \"v27\";");
        Assert.Equal(block, room.GetRange(roomStart, block.Count));
        develop.RemoveRange(start, end - start + 1);

        int updated = develop.IndexOf(MovedLines["sw.js"][1]);
        develop[updated] = "  ...PushAppTitles,";
        int fallback = develop.FindIndex(line => line.StartsWith("const PushFallbackTitle = ", StringComparison.Ordinal));
        Assert.Contains(develop[fallback], room);
        develop.RemoveAt(fallback);

        for (int i = 0; i < develop.Count; i++)
        {
            develop[i] = develop[i]
                .Replace("let tag = \"controlroom\";", "let tag = PushDefaultTag;", StringComparison.Ordinal)
                .Replace("const PushJobPattern = /^[0-9]{1,6}-[a-z0-9-]" + JobPatternDevelop,
                    "const PushJobPattern = /^[0-9]{1,6}-[a-z0-9-]" + JobPatternNow, StringComparison.Ordinal)
                .Replace("\"ControlRoom \" + version", "PushUpdatePrefix + version", StringComparison.Ordinal)
                .Replace("The old version didn't come back. Check the Mac.",
                    "The old version didn't restart. Check the computer running the server.", StringComparison.Ordinal);
        }

        develop.Insert(0, "importScripts(\"/room.js\");");
        // #411: network-first fetch, a no-cache install that takes over at once, and claiming clients on activate.
        string script = string.Join('\n', develop);
        foreach ((string now, string before) in ServiceWorkerEdits)
        {
            Assert.Contains(before, script, StringComparison.Ordinal);
            script = script.Replace(before, now, StringComparison.Ordinal);
        }

        Assert.Equal(script.Split('\n').ToList(), Lines(Path.Combine(KitFolder(), "sw.js")));
    }

    // #475: the nav's last item and the More menu now, and the two tabs develop had in their place.
    private const string MoreTabs = """
            <button type="button" class="tab-more" id="tab-more" aria-expanded="false" aria-controls="tab-more-menu">
              <svg class="tab-icon" aria-hidden="true" focusable="false" viewBox="0 0 24 24"><path d="M5 12h.01M12 12h.01M19 12h.01"/></svg>
              <span class="tab-label">More</span>
            </button>
          </nav>
          <div class="tab-more-menu" id="tab-more-menu" hidden>
            <a href="#history" data-view="history">History</a>
            <a href="#doctor" data-view="doctor">Health</a>
          </div>
        """;

    private const string HistoryAndHealthTabs = """
            <a href="#history" data-view="history">
              <svg class="tab-icon" aria-hidden="true" focusable="false" viewBox="0 0 24 24"><path d="M12 3a9 9 0 1 0 0 18a9 9 0 1 0 0-18zM12 7v5l3 2"/></svg>
              <span class="tab-label">History</span>
            </a>
            <a href="#doctor" data-view="doctor">
              <svg class="tab-icon" aria-hidden="true" focusable="false" viewBox="0 0 24 24"><path d="M22 12h-4l-3 9L9 3l-3 9H2"/></svg>
              <span class="tab-label">Health</span>
            </a>
          </nav>
        """;

    [Fact]
    public void Index_GainsOnlyTheRoomScriptAsItsFirstScript()
    {
        List<string> develop = Lines(Path.Combine(DevelopFolder(), "index.html.develop"));
        List<string> index = Lines(Path.Combine(AppFolder(), "index.html"));
        const string Room = "  <script src=\"/room.js\" defer></script>";

        Assert.Equal(index.FindIndex(line => line.Contains("<script", StringComparison.Ordinal)), index.IndexOf(Room));
        Assert.Equal(index.IndexOf(Room) + 1, index.IndexOf("  <script src=\"/token.js\" defer></script>"));
        index.Remove(Room);
        Assert.True(index.Remove("  <link rel=\"stylesheet\" href=\"/room.css\">"));
        Assert.True(index.Remove("""      <p class="token-help">On Windows, open PowerShell in the server folder and run <code>.\controlroom.exe token</code>.</p>"""));
        // #475: the History and Health tabs moved behind the More button, into the menu after the nav.
        string page = string.Join('\n', index);
        Assert.Contains(MoreTabs, page, StringComparison.Ordinal);
        index = [.. page.Replace(MoreTabs, HistoryAndHealthTabs, StringComparison.Ordinal).Split('\n')];
        Assert.Equal(develop, index);
    }

    [Fact]
    public void RoomScript_HoldsEveryMovedLineAndNoKitFileDeclaresTheGlobals()
    {
        List<string> room = Lines(Path.Combine(AppFolder(), "room.js"));
        foreach (string line in MovedLines.Values.SelectMany(lines => lines))
        {
            Assert.Contains(line, room);
        }

        Assert.Contains("const PushDefaultTag = \"controlroom\";", room);
        Assert.Contains("const PushUpdatePrefix = \"ControlRoom \";", room);
        foreach (string file in KitFiles)
        {
            string text = File.ReadAllText(Path.Combine(KitFolder(), file));
            foreach (string name in KitGlobals)
            {
                Assert.DoesNotContain("const " + name + " ", text, StringComparison.Ordinal);
                Assert.DoesNotContain("let " + name + " ", text, StringComparison.Ordinal);
            }
        }

        string sw = File.ReadAllText(Path.Combine(KitFolder(), "sw.js"));
        Assert.StartsWith("importScripts(\"/room.js\");\n", sw, StringComparison.Ordinal);
        Assert.DoesNotContain("\"/index.html\"", sw, StringComparison.Ordinal);
    }

    private static string ContentType(string path)
    {
        return Path.GetExtension(path) switch
        {
            ".html" => "text/html",
            ".css" => "text/css",
            ".js" => "text/javascript",
            ".png" => "image/png",
            ".webmanifest" => "application/manifest+json",
            _ => throw new ArgumentException(path),
        };
    }

    private static List<string> Lines(string path)
    {
        return [.. File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n')];
    }

    private static string[] Listing(string folder)
    {
        return [.. Directory.GetFiles(folder, "*", SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(folder, file).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)];
    }

    private static Dictionary<string, string> DevelopHashes()
    {
        Dictionary<string, string> hashes = new(StringComparer.Ordinal);
        foreach (string line in File.ReadAllLines(Path.Combine(DevelopFolder(), "wwwroot.sha256")))
        {
            hashes.Add(line[66..], line[..64]);
        }

        return hashes;
    }

    private static string Sha256(byte[] bytes)
    {
        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }

    private static string AppFolder() => Path.Combine(AccessTokenTests.SourceFolder(), "wwwroot");

    // RoomKit's web files, from the restored package (they are no longer in this repo).
    private static string KitFolder() => RoomKitPackage.WebAssets();

    private static string DevelopFolder() =>
        Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(AccessTokenTests.SourceFolder())!)!,
            "tests", "ControlRoom.Server.Tests", "Develop");
}
