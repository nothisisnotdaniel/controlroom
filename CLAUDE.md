# ControlRoom — conventions

Every factory agent reads this file before working on ControlRoom. Keep it short and accurate.

- **What it is:** the user's control room for the software factory. A small server on the user's Mac reads
  the factory root (the parent folder of `ControlRoom/`) and serves one web app that shows every project's
  backlog and the factory's jobs (it replaces `factory/server` and `factory/dashboard.html` over time), and
  lets the user edit backlogs. The same web app is used on the Mac in a browser and on the iPhone, installed
  to the home screen as a web app (PWA). There is no native app.
- **Projects:** a project is a top-level folder of the factory root, apart from `factory` and dot-folders,
  that has a `CLAUDE.md` (the same rule as the `/factory` skill). Each has its own git repo, `backlog.md`
  and `develop` branch. Jobs are `factory/jobs/<id>/state.json` plus their Markdown records.
- **Access:** the server listens on `127.0.0.1` only, never on `0.0.0.0` or a LAN address. The user reaches
  it from the iPhone through Tailscale (`tailscale serve` gives HTTPS on the user's own tailnet and proxies to
  the local port), so nothing is reachable from the internet. Every request is first checked by `HostCheck`
  (DNS-rebinding defence): the whole `Host` must be exactly `127.0.0.1:8100`, `localhost:8100` or the
  `--TailnetHost <mac>.<tailnet>.ts.net` name (no port, ignoring case), else `421 {"error":"bad_host"}` before the
  method check and token gate; an invalid `TailnetHost` stops the server; the request log category
  (`Microsoft.AspNetCore.Hosting.Diagnostics`) is at `Warning` so a `Host` is never logged. Every `/api/` request (read or write) needs
  `Authorization: Bearer <token>` (else `401 {"error":"unauthorized"}`); the static app shell stays public, and so does `GET|HEAD /health`, which
  returns only `{"version":"…"}` (`no-store`, still behind `HostCheck` and the method rule) for the updater. The
  one token (32 random bytes, base64url, 43 chars) lives in `~/Library/Application Support/ControlRoom/access-token`
  (folder `0700`, file `0600`, outside the factory root; `--TokenFile <path>` overrides it) and is read on every
  request. `dotnet run --project src/ControlRoom.Server -- token` prints it (creating it when missing),
  `-- token --new` replaces it; a symlinked, loose-mode or invalid file stops the server until `token --new`.
  The token is never logged, put in a URL or cached by the service worker.
- **Safety rules:**
  - The server never runs a shell command, `claude` or any program built from request input. Git is called
    with a fixed argument list (`ProcessStartInfo.ArgumentList`), never through a shell. Besides the project repos,
    git runs read-only in the factory root itself: only `git status --porcelain --untracked-files=all -- factory/jobs`
    for `GET /api/doctor` (`ProjectGit.Run`, `GIT_OPTIONAL_LOCKS=0`). In a project repo `GET /api/status` runs only the
    read-only `git rev-list --count refs/heads/main..refs/heads/develop` (`ProjectGit.AheadOfMain`); it never runs `gh`
    or calls the network. For edits while a job has the checkout on another branch, `BacklogWorktree` runs (fixed
    arguments; the only variable one is the worktree path built from the token folder and a discovered project name)
    `rev-parse --verify --quiet refs/heads/backlog`, `branch backlog refs/heads/develop`, `worktree prune`,
    `worktree add -q <path> backlog`, `worktree list --porcelain`, `rev-list --count refs/heads/develop..refs/heads/backlog`
    in the project repo, and `branch --show-current`, `merge -q --ff-only refs/heads/develop` (only when `backlog` has
    nothing develop lacks), `status`, `add` and `commit` in the worktree; it never pushes `backlog`. The project repo's own hooks run there as
    they do for a commit on develop (`worktree add`: post-checkout, `merge`: post-merge). The backlog reads
    (`BacklogSource`) run `branch --show-current` and that `rev-list` per project.
  - Paths come only from the discovered project list and the jobs folder, never from a request as a path;
    names from a request are matched against that list.
  - Writes are limited to a project's `backlog.md` and its `backlog/NNN-slug.md` story files: only through
    `POST /api/backlog/change`, one change at a time, only with a clean `backlog.md` and `backlog/`, checked against
    the version (SHA-256) the client saw (refused as changed otherwise), written atomically without following symlinks
    (`SafeFileWriter`), then committed with only the changed files. With the checkout on `develop` they go into the
    checkout and are pushed when there is an `origin`. With the checkout on any other branch (a job's `factory/<id>`
    or a detached HEAD) they go, with the same checks, into the server's own git worktree of the local `backlog`
    branch at `<token folder>/worktrees/<project>` (outside the factory root; `BacklogWorktree.Ensure` first: creates
    `backlog` from develop when missing, prunes, adds the worktree when its folder is missing, checks the
    `worktree list` path, no symlink and the branch, and fast-forwards to develop when `backlog` has nothing of its
    own; any failure gives `500 {"error":"worktree_failed"}` before a write), committed on `backlog`, never pushed
    (`pushed:false`, no warning); `job.py` merges `backlog` into develop between jobs. The checkout is never touched
    then. A project folder without its own repo gives `409 {"error":"not_develop"}` before every other check. The actions are approve,
    drop, `top`, `place` (body `line`, `target` (integers >= 0) and `where` `before|after`: both lines are of one group,
    the `[ ]` lines or the `[?]` lines, and differ; the line goes directly before or after `target` in the group's
    order and the group's lines are written back into the indexes the group occupied, every other line keeps its
    index and bytes; a no-op is `invalid`; committed as `backlog: move #N` or `backlog: move line K`; with a stop line
    the `[ ]` group includes it, so `target` may be its index, and `top` goes before the first item that is not done or
    the stop line, whichever comes first), the stop line actions (the line is exactly `- [stop] Auto stops here`, the
    text fixed by the server; the first exact one counts, as in `job.py`): `stop_add` (body `line`, a `[ ]` line; the
    stop line goes directly after it with the file's line ending; `invalid` when the file has one), `stop_move` (body
    `line` the stop line, `target` a `[ ]` line, `where` `before|after`; written back like `place`; a no-op is
    `invalid`) and `stop_remove` (body `line` the stop line), each committed as `backlog: stop line`; a file with a
    second exact stop line or a line starting with `- [stop]` (any case) that is not exact gives every `stop_*`
    `409 {"error":"stop_line"}` before other plan checks, add, title, story, `done` (only a `[h]` line becomes `[x]`) and `note` (on a numbered open
    item, with `storyVersion`: the note is cleaned to one line (whitespace runs to one space, trimmed), at most 1000
    characters, no control characters and no `Proposed by job`; dated `- YYYY-MM-DD: <note>` from the `TimeProvider`
    local date and appended under the story's first `## Notes` (after its last non-blank line, a blank line first
    unless that line is a dated note; a missing section is added at the end); only the story file is committed, as
    `backlog: note on #N`) and `capture` (body `text`, cleaned like a note but up to 2000 characters; the title is the
    text cut at the last space at or before character 60 (else at 60), trailing `.,;:!?` and spaces removed, and must
    pass the title rule; with the `add` checks a `- [?] [#N](backlog/NNN-slug.md) <title>` line goes after the last item
    (and after the stop line, like `add` bottom; `add` top never goes below it, as `job.py add`)
    and a draft story (`## What` the text, a fixed `## Why`, `## Notes` with `- YYYY-MM-DD: Draft from ControlRoom`) is
    created; committed as `backlog: capture #N`) and `category` (body `category`, exactly one of the nine `StoryCategory`
    names, with `storyVersion` and the `note` checks on a numbered item that is not done: the story's first counting
    `Category:` line is replaced, else one goes before the first `## ` with a blank line each side, else it is appended;
    the same category, or `category` on any other action, is `invalid`; only the story file is committed, as
    `backlog: category #N`). The one other file the server writes in the factory root is a
    job's request file `factory/jobs/<id>/human.json`, only through `POST /api/job/act` (see below). Nothing else in
    the factory root is written.
  - `POST /api/job/act` (body `{"id","action","round","note"?}`, at most 8 KiB, exact fields; `action` `approve`,
    `rework` (with `note`, cleaned like a backlog note) or `cancel`) asks the next `/factory` session for the project to
    approve or rework a `needs-human` job; the server itself runs nothing (no git, no `claude`). Under one lock
    (`JobActLock`) it matches `id` exactly against `JobReader.ListFolders` (never joined into a path; else
    `404 {"error":"not found"}`), reads `state.json` again (`409 not_waiting` unless `needs-human`, `409 changed` unless
    `round` matches), then writes `human.json` through `SafeFileWriter` or deletes it (no-follow). A request is pending
    while `human.json` is a regular file (`lstat`): `approve`/`rework` then give `409 pending`, `cancel` without a file
    `409 not_pending`; a symlinked (even dangling) or non-file `human.json` gives `500 write_failed` and is left as it
    is; a bad body `400 invalid` (a non-JSON `Content-Type` `415 invalid`). The file is exactly `{"action":"approve","round":R,"requested":"YYYY-MM-DDTHH:MM:SSZ"}`
    or `{"action":"rework","round":R,"note":"…","requested":"…"}` (UTF-8, one line break, at most 4 KiB; `R` the
    state's `round` (0 when missing), `requested` UTC from `TimeProvider`), not committed; the factory (`job.py human`)
    drops a stale one. The note is the user's own text and is never passed to a shell.
  - Text from files (backlog lines, job records) is shown as text, never as raw HTML; Markdown is rendered
    with raw HTML turned off.
  - Push files live next to the token file (never in the factory root): `push-keys.json` (VAPID P-256 key pair,
    a secret like the token) and `push.json` (subscriptions, `notifyDone`, `notifyUpdate` (default on) and `updateNotified`, `updateResultNotified` (the last updater run sent)). Both `0600`, written atomically
    (`0600` temp file, then rename); their folder must be `0700` and owned by the current user (created `0700`
    when missing; owner read with `lstat` in `UnixOwner`). A symlinked, loose-mode or invalid file or folder (or
    one owned by someone else) makes every `/api/push*` route return `500 {"error":"push_store"}` and is never
    overwritten. `POST /api/push/test` sends at most once per 10 s server-wide (`PushTestLimiter` on
    `TimeProvider`); a call inside that window returns `429 {"error":"too_many_requests"}` and sends nothing.
  - Backlog worktrees live in `worktrees/` in the same folder (`worktrees/<project>`, one per project, made by
    `git worktree add`); `worktrees/` follows the `PrivateFolder` rules (`0700`, owned by the user, not a symlink,
    created `0700` when missing) and a symlinked `worktrees/<project>` is refused.
  - Update files live in the same folder, with the same rules (`PrivateFolder`): `settings.json` is exactly
    `{"autoUpdate":B}` (channel `stable`), `{"autoUpdate":B,"channel":"stable"}` or
    `{"autoUpdate":B,"channel":"develop"}` with `B` `true` or `false` (one optional trailing line break; missing is
    off and `stable`), written only by `POST /api/update/settings` as the three-key form plus a line break (body: one
    or both of `autoUpdate` and `channel`, a missing one keeps its value; a channel change starts one update check
    without waiting; a refused or invalid file gives `500 {"error":"settings_store"}` and is never overwritten). `update-result.json` is written only by `scripts/updater.sh`; the server reads it (no-follow,
    `0600`, owned by the user, at most 4 KiB, exact schema) and never writes or deletes it.
  - The only outbound calls are Web Push POSTs to subscription endpoints, allowed only as `https`, port 443, no
    userinfo, on `*.push.apple.com`, `fcm.googleapis.com` or `updates.push.services.mozilla.com`, and the update
    check: one fixed `GET https://api.github.com/repos/nothisisnotdaniel/controlroom/releases/latest` at start and
    every 6 h (no `Authorization` or cookies, body capped at 1 MiB, only a `v`+SemVer `tag_name` kept, the release
    URL rebuilt from it); on the `develop` channel instead one fixed
    `GET https://api.github.com/repos/nothisisnotdaniel/controlroom/releases?per_page=100` (same rules; from the
    array only non-draft `vX.Y.Z` or `vX.Y.Z-develop.N` tags, the highest by SemVer); no redirects, 10 s timeout.
    The banner shows a result only for the channel it was found on. Endpoints, keys and the token are never logged (status codes
    only). A payload holds only `kind`, `project`, `job`, `stage`, `version`, a fixed `reason` and `count`, never
    record or backlog text; an `update` payload is exactly `kind`, `version` and `reason`, sent once per version;
    an `updated` payload is exactly `kind`, `version`, `previous`, `reason`, an `update_failed` payload exactly
    `kind`, `version` and the result's fixed code as `reason`, each sent once per updater run.
  - Scripts (`launch-agent.sh`, `updater.sh`) never read the token. The updater's only network calls are the
    latest-release redirect (`stable`) or the release list `GET` above (`develop`, `curl --proto =https --tlsv1.2
    -fsS --max-time 30 --max-filesize 1048576`, no `-L`), the release download (as `install --release`) and
    `GET http://127.0.0.1:8100/health`, without `Authorization`.
  - Release signing (self-signed certificate, no Developer ID or notarization) is off until `signing-cert.sha1` (the
    certificate's SHA-1 fingerprint) is committed at the repo root. The rule everywhere: a regular file (not a
    symlink) of exactly 40 hex characters plus at most one trailing line break; missing is off, present but invalid
    always fails. Once on, `check_and_extract` (both scripts) runs `codesign --verify --strict` with
    `-R=identifier "ControlRoom.Server" and certificate leaf = H"<sha1>"` on the server and
    `-R=certificate leaf = H"<sha1>"` on every top-level `*.dylib` and `createdump`; a failure or no absolute
    `codesign` on `PATH` is the check-failed result. `SIGNING_CERT_SHA1` is set only from the file, never from the
    environment. `install --release` reads `<repo>/signing-cert.sha1` before any download and writes its copy
    `<support>/signing-cert.sha1` (`0600`, `put_file`; removed when signing is off and with the updater); the updater
    reads that copy (must be `0600`, owned by the user, not a symlink, else `check_failed`). The private key is only
    in the `SIGNING_CERT_P12`/`SIGNING_CERT_PASSWORD` secrets and a per-run temp keychain. The pin covers only the
    Mach-O files (identity for macOS permissions), not the managed `*.dll` files: it is not tamper protection.
- **Stack:** .NET 10 ASP.NET Core minimal API (`Microsoft.NET.Sdk.Web`, no extra packages) serving a plain
  HTML/CSS/JS web app from its own `wwwroot` (no CDN, no build step, strict CSP), xUnit tests with
  `Microsoft.AspNetCore.Mvc.Testing` to host the app in memory. No new package without a reason in the spec.
  Run: `dotnet run --project src/ControlRoom.Server` (listens on `127.0.0.1:8100` only; the factory root is the
  parent of the folder holding `ControlRoom.slnx`, or `--FactoryRoot <path>`).
  `--PushSubject <mailto:…>` sets the VAPID `sub` (default `mailto:controlroom@localhost`).
  `--TailnetHost <mac>.<tailnet>.ts.net` is needed before the iPhone works through Tailscale.
  Start at login: `scripts/launch-agent.sh install [--tailnet-host <name>]|status|uninstall` (LaunchAgent `local.controlroom.server`,
  published server in `~/Library/Application Support/ControlRoom/server/`, log `~/Library/Logs/ControlRoom/server.log`).
  `scripts/launch-agent.sh install --release [vX.Y.Z] [--tailnet-host <name>]` installs a GitHub release instead (latest
  when no tag, or the newest `develop_tag` build when `settings.json` holds the develop channel; no .NET SDK needed; launchd runs `server/ControlRoom.Server` directly). It also installs the
  updater (`local.controlroom.updater`, `RunAtLoad` with `--start-delay 60` so the first check is about a minute
  after install or login, then every 6 h, and every 15 min (up to 5 h 45 min) while a factory job runs; `<support>/updater.sh`, marker `<support>/installed-release`, log
  `~/Library/Logs/ControlRoom/updater.log`), which installs newer releases (by SemVer; on `stable` prereleases are
  ignored, on `develop` the newest `vX.Y.Z` or `vX.Y.Z-develop.N`) only while `settings.json` says on and
  no job is in `spike|spec|build|review`, and with each one also its own `updater.sh` and plist; a plain `install` and
  `uninstall` remove it (with `updater.sh.previous` and `updater.plist.previous`).
  `dotnet run --project src/ControlRoom.Server -- version` prints `ControlRoom <version>` (`dev` without `-p:Version`);
  `GET /api/version` returns `{"version":"…"}`; `GET /api/update` returns `{current, available, latest, url,
  command, autoUpdate, channel}` from the last update check (`dev` never checks) and `settings.json` (`null` when refused).
- **Releases:** `.github/workflows/release.yml` runs for a merged PR from `develop` into `main` titled
  `release vX.Y.Z` (`.github/scripts/release-version.sh` checks the title and existing tags, and refuses
  `-develop`) and for every push to `develop` that changes more than `backlog.md` and `backlog/**` (a prerelease
  `X.Y.(Z+1)-develop.<run number>` from `.github/scripts/develop-version.sh` on `GITHUB_SHA`, checked against
  `refs/heads/develop` and `HEAD`); the version and commit reach later steps as `RELEASE_VERSION` and
  `RELEASE_TARGET`, never `VERSION`, so MSBuild can't read them as `$(Version)`. On a `macos-15` (arm64)
  runner it re-checks the PR, runs `dotnet test -c Release`, publishes `-r osx-arm64 --self-contained
  -p:Version=X.Y.Z`, signs and verifies it when `signing-cert.sha1` is committed (steps `Sign`, `Verify signature`,
  `Remove signing keychain` with `if: always()`; README "Signing"; `.github/scripts/signing-cert.sh` reads the file,
  `.github/scripts/signing-keychain.sh` puts the temp keychain on the user search list after `unlock-keychain` (`add`),
  checks the pinned identity before the first `codesign` (`check`, fails with its own error) and takes it off again in
  `Remove signing keychain` (`remove`, only a `::warning::` on failure, then `delete-keychain`),
  `.github/signing/entitlements.plist` holds the three hardened-runtime entitlements; the secrets reach only `Sign`
  through its `env:`; missing file is a `::warning::` and an ad-hoc build), smoke-checks `ControlRoom.Server version`, packs `controlroom-X.Y.Z-osx-arm64.tar.gz` (one top
  folder, with a byte copy of `scripts/updater.sh` (`0755`) as `<top>/updater.sh`), writes `SHA256SUMS`, attests build provenance and runs `gh release create vX.Y.Z` on the merge commit or pushed commit
  (`--title "ControlRoom X.Y.Z" --generate-notes`, plus `--prerelease` for a prerelease version). After a stable
  release, `Prune old prereleases` (`continue-on-error`) deletes every `-develop` prerelease and tag at or below
  that version (`.github/scripts/old-prereleases.sh`). Top-level `permissions: {}`; the job has only `contents: write`,
  `id-token: write`, `attestations: write`; every action pinned to a commit SHA; PR and ref fields only through `env:`.
  Check a tarball with `gh attestation verify <tarball> --repo nothisisnotdaniel/controlroom`. The repo slug is fixed
  in `launch-agent.sh`; renaming the repo breaks `--release`.
- **Test command:** `dotnet test` (from `ControlRoom/`; `dotnet build` must stay at 0 warnings). Tests build a
  temporary factory root (`TempFactoryRoot`) and host the server with `ServerFactory`; no test reads the real
  factory root or opens a real port.
- **Layout:**
  - `ControlRoom.slnx`, `Directory.Build.props` (nullable, implicit usings, code style in build), `.editorconfig`.
  - `src/ControlRoom.Server/`: `Program.cs` (entry; `token` runs `TokenCommand`,
    `version` runs `VersionCommand`), `ServerApp` (builds the app:
    Kestrel, headers, the `HostCheck`, GET/HEAD only (POST only on `/api/backlog/change`, `/api/job/act`, `/api/update/settings` and the four `/api/push/*` POST routes), the `/api/` token gate, static files, endpoints, push services), `AccessToken` (token file
    path, create, safe read, atomic replace, constant-time check), `TokenCommand` (`token`, `token --new`), `HostCheck` (allowed `Host` values, `TailnetHost` rule, fixed
    `421` body), `ServerEndpoint` (the one listen address), `FactoryRootLocator`,
    `ProjectFinder` (project discovery), `SafeFileReader` (no-follow, regular-file, size-capped text or bytes read, SHA-256 version),
    `SafeFileWriter` (temp file in the same folder, then rename; refuses a symlinked target or folder),
    `BacklogReader` (safe `backlog.md` read), `BacklogParser` (lines to entries, old and numbered `[#N](…)`
    format; `StopLineText`, `IsStopLine` and `Backlog.StopLine`, the first exact stop line's index, never an entry), `BacklogWaiting` (the Ready/Waiting rule: a dependency is met only by a done line of the named, or the
    same, project; a name matches exactly, else the single project equal ignoring case; unknown counts as unmet;
    `IsMissing`: unknown project or no line with that number in a readable backlog, never for an unreadable one or
    `factory`), `BacklogPlan` (the rule of `job.py next`: numbered `[ ]` lines in file order, blocked while a
    dependency is unmet, the first one not blocked is next, spikes too; the stop line is ignored, `belowStop` says
    the next item is after it), `BacklogsEndpoint` (`GET /api/backlogs`, with
    `waiting`, `line`, `category` (from each numbered line's story, read once through `StoryReader`, else `null`) and the file `version`, and per project `pending` (true when read from the worktree), `next` (`{number, title, story, spike, belowStop}` or `null`), `stopLine` (0-based index or `null`),
    `stopped` (a blocked line has a missing dependency) and `blocked` (`[{number, title, story, after:[{project, number,
    missing}]}]`, only unmet dependencies); `null`, `false`, `[]` when unreadable), `BacklogEditor` (`EditableText` lines that keep their bytes; approve, drop, done, move (top), place, title
    and add edits (`Add` takes the mark), `StopAdd`, `StopMove`, `StopRemove`, `StopLine` and `HasStopLineProblem`, `CleanNote` and `AppendNote`, `CleanCapture`, `CaptureTitle` and `RenderDraft`, slug, next number and story template like `job.py`), `ProjectGit` (fixed-argument git calls
    with timeouts and `GIT_TERMINAL_PROMPT=0`), `BacklogWorktree` (the `backlog` branch worktree at
    `<token folder>/worktrees/<project>`: `Ensure`, `Pending` (`backlog` has commits develop lacks and the folder
    exists), `Exists` (no git)) and `BacklogSource` (where `backlog.md` and stories are read: the worktree while the
    checkout is not on `develop` and `Pending`, else the project; used by `GET /api/backlogs`, `/api/story`,
    `/api/inbox`, `/api/tasks` (both no longer used by the web app since #135) and `/api/search`), `BacklogChangeEndpoint` (`POST /api/backlog/change`: request
    checks, `not_develop` (no repo)/`worktree_failed`/`dirty`/`changed`, write, commit, push (develop only); `done` commits `backlog: done #N`, `note` commits `backlog: note on #N`, `capture` commits `backlog: capture #N`, `stop_*` commit `backlog: stop line`), `StoryReader` (fenced read of
    `<project>/backlog/NNN-slug.md`), `StoryCategory` (the story's `Category: <name>` line like `job.py with_category`:
    the nine names in `job.py` order, `Parse` (first `Category:` line before the first `## `, else `null`) and `Set`), `StoryMarkdown` (Markdown to a JSON block tree, no HTML),
    `StoryEndpoint` (`GET /api/story?project=&number=`, with `markdown`,
    `storyVersion` and `category`), `InboxEndpoint` (`GET /api/inbox`: every `[?]` line as `{project, version, line, number, title,
    rest, job}` plus `unreadable` project names, `no-store`; `job` from the first `Proposed by job <id>` in the story's
    `## Notes` through `StoryReader`, kept only as `^[0-9]{1,6}-[a-z0-9]+(-[a-z0-9]+)*$` up to 80 chars; with a job first by
    its number, then without, ties by project (ordinal) and line), `TasksEndpoint` (`GET /api/tasks`: every `[h]` line as
    `{project, version, line, number, title, rest, story, after}` split into `ready` and `waiting` by `BacklogWaiting`
    (`after` only the unmet dependencies, `project` always set), plus `unreadable` names, `no-store`; project order, then
    file order), `HistoryEndpoint` (`GET /api/history`, read-only, `no-store`, no parameters: `{jobs, days, totals,
    chart, lifetime}` from `JobReader.ReadAll` and `TimeProvider` (UTC); `jobs` only stages `done`/`needs-human`/`reverted` as
    `{id, project, title, mode, path, round, outcome, tokens, finished}` (`outcome` `merged`, `findings` (decision
    `FINDINGS`), `escalated`, `reverted`; `finished` the `Updated` time or `null`), newest first then id descending,
    `null` last; `days` the 30 UTC dates ending today; `totals.projects`/`totals.modes` `{name, last7, last30}` with
    `{jobs, tokens, unknown}` (only names with a job in the 30 days, ordinal, `null` last); `chart.modes` `{name,
    tokens}` with 30 daily known-token sums; `lifetime` `{projects, modes, projectModes}` over every finished job
    whatever its age (also with `finished` `null`): `projects`/`modes` `{name, all}` with `{jobs, tokens, unknown}` (every
    name, ordinal, `null` last), `projectModes` `{name, modes}` one per lifetime project in that order with that project's
    mode rows; unknown tokens never count as 0, token sums saturate at `long.MaxValue`), `SearchEndpoint` (`GET /api/search?q=&mark=&project=&category=`: `{items, total, projects, unreadable}`, items
    `{project, number, mark, title, rest, story, match, category}`, `no-store`; `#N`/`Name#N` (name ignoring case), else every
    word (ordinal, ignore case) in title or rest, else in the story through `StoryReader.Read` (read only for an item that
    passes the filters and missed on title, for the `category` filter, or for a returned item's category; at most once per item); `mark` (`open|approval|task|done`),
    `project` (exact name) and `category` (one of the nine) filters; order
    number, title, story, filter, each by project then file line; at most 100 items; a repeated parameter, unknown mark,
    category or project, or `q` over 200 chars gives `400 {"error":"invalid"}`), `JobReader` (`factory/jobs/<id>/state.json` to a typed
    `JobSummary`; `request` is the `human.json` action, filled only for `needs-human`; `tokens` is `null` when no agent
    gives a valid count; `Updated`, `CiUrl` and `CiSha` (never in JSON); `Updated` only from an exact `YYYY-MM-DDTHH:MM:SSZ` `updated`), `BoardEndpoint` (`GET /api/board`: jobs and `[h]` tasks), `ChangeFingerprint` (SHA-256 fingerprints from `lstat`
    metadata only: `jobs` over each `state.json` and `human.json` of `JobReader.ListFolders`, per project `backlog` over `backlog.md`
    and `stories` over `backlog/*.md`, each also over the worktree's copies when its folder exists; a symlink adds only a `link` marker, a missing path `missing`; `NoFollowStat`:
    `lstat`, `O_NOFOLLOW` folder listing and `fstatat` via P/Invoke, never a file opened or a target followed),
    `ChangesEndpoint` (`GET /api/changes`: `{"jobs":…,"projects":{"<name>":{"backlog":…,"stories":…}}}`, `no-store`), `JobEndpoint` (`GET /api/job?id=`:
    a job and its records from a fixed list, and `request`: `null`, the parsed `human.json` or `{"action":"unreadable"}`
    for a bad, symlinked or oversized file, read only through `SafeFileReader`), `JobAction` (the `human.json` schema:
    `Parse` (exact keys, at most 4 KiB), `Render`, no-follow `Read`), `JobActEndpoint` (`POST /api/job/act`), `PushCrypto` (RFC 8291 `aes128gcm`, RFC 8292 VAPID ES256 JWT, P-256
    point check, `Base64Url`), `PrivateFolder` (the `0700` folder next to the token file: no-follow `0600` reads,
    atomic writes, `EnsureFolder`/`IsReady`, `PrivateFileException`), `PushStore` (`push-keys.json`/`push.json` next to the token file;
    `PushSubscriptionRules`: endpoint host allowlist and key checks), `PushSender` (`PushPayload` with the fixed
    reason table, `PushTransport` (the injectable `HttpMessageHandler`), one POST per subscription, `404`/`410`
    removes it), `PushWatcher` (hosted service, `Scan()` every 30 s on `TimeProvider`; first scan is the baseline),
    `PushEndpoint` (`GET /api/push`, `POST /api/push/subscribe|unsubscribe|settings|test`), `PushTestLimiter`
    (one test send per 10 s), `UnixOwner` (`geteuid`, `lstat` uid via P/Invoke), `AppVersion` (informational version without
    `+build`, else `dev`), `VersionCommand` (`version`), `VersionEndpoint` (`GET /api/version`), `UpdateCheck` (`SemVer` (2.0.0 precedence, no build metadata),
    `RunningVersion`, `UpdateTransport` (the injectable handler and timeout), `UpdateState` (latest release, in
    memory), `UpdateChecker` (hosted service, `Check()` at start and every 6 h on `TimeProvider`, the `update` push
    once per version)), `UpdateEndpoint` (`GET /api/update`, `POST /api/update/settings`), `UpdateSettingsStore` (`settings.json`),
    `UpdateResult` (`update-result.json` schema and `UpdateResultReader`), `HealthEndpoint` (`GET /health`), `DoctorCheck` (the
    health report like `job.py doctor`, read-only: `stuck` active jobs (`spike|spec|build|review|approve`) over 24 h
    since `Updated` or without it, `{id, project, stage, hours}`, `null` first then oldest, ties by id; `uncommitted` job
    ids from `git status` in the factory root (ids by `JobReader.IdPattern`, no dot names; no `.git` or a failed call
    adds `factory` to `unreadable`); `mergedBranches` `{project, branch}`: `factory/<id>` branches in `git branch
    --merged develop` (else `main`, by `rev-parse --verify --quiet refs/heads/develop`) whose job is `done`;
    `stories` `{project, number, problem, line}` with `no_story`, `duplicate`, `missing` (target
    `backlog/NNN-slug.md`, `lstat` no regular file; a symlinked `backlog/` counts as missing), `bad_link` (any other
    target, never resolved) and `orphan` (`line` the file name), titles at most 80 chars, story files never opened;
    a project with an unreadable `backlog.md` or a failed git call is only named in `unreadable`), `DoctorEndpoint`
    (`GET|HEAD /api/doctor`: `{stuck, uncommitted, mergedBranches, stories, unreadable}`, `no-store`, no parameters),
    `ProjectStatusEndpoint` (`GET|HEAD /api/status`: `{projects:[{name, ci, ahead}]}` in `ProjectFinder` order, `no-store`,
    any query parameter `400 invalid`; `ci` `{status, url, sha, time}` from the project's (exact name) highest-numbered
    job (ties by id descending) with `ci.status` `success|failure|timeout|skipped`, `url` only a plain
    `https://github.com/…` URL up to 500 chars, `sha` 7–40 lowercase hex sent as 7 chars, `time` the job's `Updated`;
    `ahead` from `ProjectGit.AheadOfMain` or `null`),
    `wwwroot/` (`index.html` (the header holds only `h1#top-title`; the five sections Jobs, Backlogs, Capture, History, Health are a bottom tab bar (no Search tab: search is at the top of Backlogs, #133; no Inbox or Tasks tab, #135)
    `nav.nav.tab-bar` after `p.live-notice`, each link an inline `svg.tab-icon` (`aria-hidden`, paths only) over a
    `span.tab-label`; the Backlogs link holds the two count badges `span.nav-count.nav-count-tasks#tasks-count` and `span.nav-count.nav-count-approval#approval-count`), `app.css` (the bar is fixed to the bottom on
    every width, one row of five, the tasks badge on the icon's left and the approval badge on its right, each its own `--count-tasks-*`/`--count-approval-*` colour pair in light and dark, `--tab-bar-height` plus the home indicator inset reserved as body bottom padding,
    the edit notice above it), `token.js` (stored token under
    `controlroom-token`, `authFetch` for every `/api/` call, the token view, which hides the header and the tab bar), `app.js` (routing (`showView` shows the tab bar on every view but the token view and sets `aria-current` on one tab, `job` on Jobs and `story` on Backlogs; `#search` routes to Backlogs, replaced by `#backlogs` with `history.replaceState`, scrolled to the top with the search field focused; every Backlogs show calls `showSearch`; `show`, the picker and a new section never move `#backlog-search`, the first child of `#backlogs`), backlogs (each readable project's `div.plan` between the head and the groups: `Next:` item
    with a `Spike` badge and `span.next-stop` `Auto stops before #N` when `next.belowStop`, `Nothing ready to run.` or the fixed stopped text, and a `Waiting (k)` group with `Waits for`
    links, a missing one as text plus `No such item`; the plan's signature (with `stopLine`) in `data-plan`, and a live refresh also
    replaces a section whose signature changed; a `Project` select (`div.backlog-picker`, `select#backlog-project`, readable
    projects only, no All) shows one section at a time, the choice in `controlroom-backlog-project` (falls back to the first
    readable project without storing it), kept on a live refresh and set by a story link into a readable project; every group (Open, Waiting for approval, For you,
    Done and Waiting) folds as a `details.group` named in `data-group`, only Done closed by default, each state per project in
    `controlroom-backlog-groups` and copied by name on a live refresh; every row of every group starts with its 1-based place in
    that group (`positionElement`: `span.position` with a `span.visually-hidden` `position ` for screen readers); the
    stop line is `li.stop-line` (`data-line`, a `span.stop-line-label` `Auto stops here` with `role="separator"`, no
    position) in the Open list before the first row whose line is after it, else at the end, and Open shows with only
    the divider when it has no rows (`renderStopLine`, `placeStopLine`); a numbered
    row with a category shows `categoryChip` (`span.category-chip.category-<name>`, text only, class only from the fixed
    `Categories` list) after the title), story (the same chip under the title, then a status chip (#125, `statusChip`: `span.status-chip.status-<key>` from the fixed `StoryStatuses` map, `[x]` Done, `[?]` Unapproved, `[ ]` Open, `[h]` Your task, same box style as the category chip, a colour pair per status in light and dark) placed by `showStoryStatus` from the mark that edit.js's `showStoryDecision` looks up with `findStoryEntry` (all four groups of `/api/backlogs`; `findStoryLine` is its not-done part), none when the line can't be found, carried over on a live refresh), then the backlog position (#126, `storyPosition`: `span.story-position` `Position N`, muted text, N the item's 1-based index in its `/api/backlogs` group, the same number the list shows) placed by `showStoryPosition` after the status chip from the same `findStoryEntry` lookup (`position`), none when the line can't be found, carried over on a live refresh)), `edit.js` (per-project edit mode; a `button.drag-handle` (`≡`, `Move #N`, arrow-key hint) at the start of every
    `[ ]` and `[?]` row, prepended by `appendEditRow`: pointer events only (captured on the handle, the only element with
    `touch-action: none`), a `.drop-indicator` among the rows of the row's own list, auto-scroll in the 48 px edges by
    `requestAnimationFrame`, one `place` per drop or ArrowUp/ArrowDown, Escape and `pointercancel` send nothing, focus back
    on the handle after a key move; `dragBusy` holds back live.js's section refresh; the stop line divider gets the
    same kind of handle (`Move stop line`, `place.stop`) and is one of the Open rows while an item is dragged (its
    `data-line` is a `place` target); its drag or arrow key sends one `stop_move` (`sendStopMove`, focus back on its
    handle); `Remove stop line` after a confirm; without a stop line every `[ ]` row's edit row has `Stop after this`
    and the Open group `Add stop line` (after its first `[ ]` row), all through `sendChange`; the Add form, Edit story and Add note (`add-note-button` in the story bar; only
    one of the two forms open at a time), Approve and Drop in the story bar of a `[?]` item only (`story-approve-button`,
    `story-drop-button`, group `approval` from `/api/backlogs`; a `Category` select (`label.story-category`,
    `select.story-category-select`, a disabled `No category` option only without one) while the item is not done, sending
    `action:"category"` through `sendChange`; version and line looked up again at the tap, through
    `sendChange`, Drop after a confirm;
    after either succeeds the view goes `history.back()` or to `#backlogs` (#128, #135, `leaveDecidedStory`) keeping the
    notice for that route (`routeEditNotice`); Done in the story bar of a `[h]` item only (#134, `story-done-button`, group `user`, after the Category select, `action:"done"` through `sendChange` with the line looked up again at the tap, then `leaveDecidedStory`, no confirm); every committed `sendChange` refetches the Backlogs counts; hidden while a story form is open), fixed texts per error code), `board.js` (jobs board and job page: the board shows jobs only, never `[h]` tasks (the Tasks tab lists them): `needs-human` jobs go in a `section.stuck` `Stuck (n)` before the stages, left out when there are none, each card with a `Needs you` chip (`chip-human`); job page (a read-only state section between the facts and the record tabs: stage and round, review rows per round with visible values, decision chip, CI and note; a board card of a `needs-human` job with a request shows `Approve sent` or `Rework sent`), `pollBoard` fetched by `live.js`; a live refresh of the job page is held back while `act.js` says the decision section is in use; the `ControlRoom <version>`
    line at the foot of Jobs and the update banner under its heading, each asked once per page load), `live.js` (the one `setInterval`: asks `/api/changes` every 5 s while visible, no token view and nothing
    pending, and at once on becoming visible; refetches only the board, the changed backlog sections (kept while a form
    is open or focus is inside) and the open story (in place, never while Edit story or Add note is open); a failed check shows
    the fixed `Connection lost. Reconnecting…` notice under the header, or in the `board-status` line on the board;
    script order `token.js`, `board.js`, `act.js`, `push.js`, `edit.js`, `counts.js`, `search.js`, `capture.js`, `history.js`, `doctor.js`, `status.js`, `live.js`, `app.js`; a changed
    `backlog` fingerprint (or a recovered connection) refetches the Backlogs counts; a changed `backlog` or `stories` fingerprint re-runs the current search;
    a changed `jobs` fingerprint refetches the history while it is shown; a changed `jobs`, `backlog` or `stories`
    fingerprint refetches the health view while it is shown; a changed `jobs` or `backlog` fingerprint refetches the
    status lines while Backlogs is shown), `act.js` (the "Your decision" section under the state section of a `needs-human` job page only: Approve (after a
    confirm), Rework with a note box (`maxlength` 1000) and Send (off while blank); with a pending request the fixed
    `Waiting for the next /factory session for <project>: <Approve|Rework> sent <local time>` text, the note as text and
    Cancel request; fixed texts per error code in `ActErrors`; `jobDecisionBusy` (a send in flight, or the note box has
    text or focus) keeps a live refresh from rebuilding it), `counts.js` (#135: the two badges on the Backlogs tab icon, the `[h]` count (`user`) and the `[?]` count
    (`approval`) summed over readable projects of one `/api/backlogs` answer (`fetchBacklogs`), older answers dropped by a request
    counter; each hidden at 0 or unknown (failed load), else the number (`aria-hidden`) plus a `span.visually-hidden`
    `, N task(s)` / `, N unapproved`; fixed texts in `CountTexts`; fetched at page load, after a token is saved, after every
    committed change and on a live backlog change; `#inbox` and `#tasks` route to Backlogs, replaced by `#backlogs` with
    `history.replaceState`, scrolled to the top), `search.js` (search at the top of Backlogs (#133),
    read-only: the form (field `Search backlogs`, mark, project and category selects, the project list from the first answer) is
    built once into `div#backlog-search` and kept, so Back finds it as it was; while the field or a filter is set `#backlogs` has
    the class `searching` and CSS hides every child but the box (results in place of the backlog), cleared it shows the backlog again; one `/api/search` per change through `authFetch`, older answers
    dropped by a request counter, nothing sent with an empty field and no filter; only the results are replaced; fixed
    texts in `SearchTexts`), `capture.js` (the `#capture` view behind the Capture tab (#135; header title `Capture`), scrolled to the top: a `Project` select of readable projects from
    `/api/backlogs` on every show (last used one in `localStorage` `controlroom-capture-project`), the `Idea` textarea
    (`maxlength` 2000) and Save (off while blank or `changeBusy`); Save fetches the version, sends `action:"capture"`
    through `sendChange`, then clears the text and shows the fixed saved text (`sendChange` refreshes the counts); fixed texts in
    `CaptureTexts`), `history.js` (the `#history` view, read-only: `Last 7 days` and `Last 30 days` tables for projects
    and modes (`formatTokens`, `+ k unknown`), an `All time` block (`#history-all`, from `lifetime`, after the 30-day
    block or the `noRecent` text and before the chart; hidden when `lifetime` is empty or malformed), a stacked bar chart per day built with `createElementNS` (`role="img"`,
    text `aria-label`, legend with each mode's 30-day total), then the job list (title links `#job/<id>`, outcome badge,
    tokens or `unknown`) with a `Project` select that filters the list and the All time block (that project's row and its `projectModes`
    modes) on the client without a fetch, never the 7/30-day tables or the chart (kept in `localStorage`
    `controlroom-history-project`); fetched on every show; fixed texts in `HistoryTexts`), `doctor.js` (the `#doctor`
    view, nav `Health` with no count, read-only: one section per non-empty list (`Stuck jobs`, `Uncommitted job
    records`, `Merged leftover branches`, `Story problems`), each with the fixed text of what `/factory doctor` would
    do; stuck jobs link `#job/<id>` with `<stage>, <n> h` or `no update time`, numbered story problems link
    `#story/<project>/<n>`; only `All healthy.` when every list is empty, `Could not check: <names>`; fetched on every
    show, never on a timer; fixed texts in `DoctorTexts`), `status.js` (a `div.project-status` line per readable project
    on Backlogs, between the head and `div.plan`: `CI passed|failed|timed out|skipped` or `No CI recorded` (a link,
    `target="_blank"` `rel="noopener noreferrer"`, only to the server-kept `https://github.com/` URL; `ci-bad` on
    failed/timed out), the local time, then `N commits on develop not on main` / `develop is level with main` /
    `Git status unavailable`; a failed call shows only `Status unavailable.`; fetched on every Backlogs show and, while
    shown, after a changed `jobs` or `backlog` fingerprint; a refresh replaces only the lines, never one with focus;
    fixed texts in `StatusTexts`), `push.js` (Notifications section on the
    board: state texts, Turn on (permission only from the tap), done and update switches, Send test, Turn off),
    `sw.js` (service worker: caches only the fixed app shell, never `/api/`; bump its cache version when the
    shell list changes; `push` shows a notification from fixed titles, `notificationclick` opens `#job/<id>`,
    `#backlogs` or `#jobs`; `/health` is never cached), `manifest.webmanifest`, `icons/` (opaque RGB PNGs, 180/192/512 px)).
  - `scripts/launch-agent.sh`: POSIX `sh` LaunchAgent installer (publish, `bootout`, replace, plist `0644`,
    `bootstrap`; refuses symlinks; never reads the token). After every `bootout` (server and updater) it polls
    `launchctl print` until the service is gone (`sleep 0.5` between polls, at most 30 polls, then goes on); a failed
    `bootstrap` is retried after `sleep 1` up to 5 times (6 attempts) before the fixed `launchctl bootstrap failed`
    message; `sleep` is found on `PATH` (not for `status`). With `--release` it runs `curl`, `tar` and `shasum`
    instead of `dotnet`: tag and latest-release redirect checked as `v`+SemVer, downloads into a `mktemp -d` folder
    in the support folder (removed on every exit), `curl --max-filesize` 200 MiB for the tarball and 4 KiB
    for `SHA256SUMS`, one matching `SHA256SUMS` line, archive entries only regular files and folders under
    `controlroom-<ver>-osx-arm64/`, at most 10000 entries and 512 MiB of unpacked content (counted through a pipe
    before extraction), checked again after extraction, then `verify_signature` when `signing-cert.sha1` pins the
    signing certificate. Without a tag it uses `develop_tag` when `<support>/settings.json` is a regular file (not a
    symlink) of at most 64 bytes holding `{"autoUpdate":true|false,"channel":"develop"}` plus at most one line break,
    else `latest_tag`. `remove_updater` also removes `updater.sh.previous` and `updater.plist.previous` (a symlink at
    either is refused by `install` and `uninstall`). The updater plist comes from the shared `updater_plist`. Not
    part of the server.
  - `scripts/updater.sh`: POSIX `sh` updater run by launchd (`--factory-root <root> [--start-delay <1-600>]`; any
    other argument list ends `job_running`; the delay is one `sleep` before anything is read, skipped without `sleep`
    on `PATH`). While a job runs it logs `<UTC> waiting <installed>`, removes its lock, waits `sleep 900` (in the
    background with `wait`, so `bootout`'s TERM ends it at once) and repeats steps 1-4, at most 23 times, then ends
    `job_running` (no wait without `sleep` on `PATH`). It prints one final log line
    `<UTC> <word>[ <tags>]` (`off`, `not_release`, `busy`, `job_running`, `current`, `latest_failed`,
    `download_failed`, `check_failed`, `skipped_failed`, `updated`, `rolled_back`, `bootstrap_failed`, `swap_failed`,
    `rollback_failed`). Lock `<support>/.update.lock` (stale after 1 h); every bootout is followed by `wait_gone`
    (polls `launchctl print` until it fails, `sleep 0.5` between, at most 30 polls, then goes on) and every bootstrap
    is retried after `sleep 1` up to 5 times; a new server that never bootstraps is rolled back as `bootstrap_failed`
    (result reason `bootstrap_failed`, not skipped next run), an old one that never bootstraps is `rollback_failed`,
    both without polling `/health`; swap: bootout, `server` to
    `server.previous`, new folder to `server`, bootstrap, poll `/health` up to 15 times (an answer counts only while
    `launchctl print` shows the server running and its pid is the only listener `lsof` shows on port 8100); rolls
    back when it does not answer, and (as `swap_failed`) when the marker can't be updated. Self-update (all inside
    `swap()` after the signal trap): before the bootout it stages the release's `$WORK/out/$TOP/updater.sh` (a regular
    file of at most 256 KiB whose first line is exactly `#!/bin/sh`; `ROOT`/`HOME` without control characters) as
    `<support>/.updater.XXXXXX` (`0700`) and the `updater_plist` output (`0644`); after the new server folder is in
    place and before the bootstrap it copies the installed `updater.sh` and updater plist to `<support>/updater.sh.previous`
    (`0700`) and `<support>/updater.plist.previous` (`0644`), then renames the staged files over `<support>/updater.sh`
    and `~/Library/LaunchAgents/local.controlroom.updater.plist` (every write a rename). Every rollback (through
    `roll_back`) renames the `.previous` copies back, only for files this run replaced. It never bootouts, bootstraps or
    runs the updater itself. When the release's copy is not used it logs `<UTC> updater_kept missing|bad|write_failed`
    before the final line (a failed write puts back what it renamed) and the server update goes on. A `state.json` over
    64 KiB counts as a running job. `valid_tag` (`v`+SemVer, `-develop.N` allowed, no build
    metadata), `latest_tag`, `download`, `check_and_extract`, `fingerprint_file`, `verify_signature`, `xml_escape`,
    `updater_plist`, `number_greater`, `text_greater`, `semver_newer`, `channel_tag` and `develop_tag` are byte-identical
    to `launch-agent.sh` (a test compares them). The channel comes from `settings.json` (at most 64 bytes).
  - `.github/workflows/release.yml` and `.github/scripts/release-version.sh` (title `release vX.Y.Z` to `X.Y.Z`;
    existing tags on stdin), `.github/scripts/develop-version.sh` (run number to the next `-develop.<run>`
    version; tags on stdin), `.github/scripts/old-prereleases.sh` (version to the `-develop` tags at or below
    it; tags on stdin) and `.github/scripts/signing-cert.sh` (prints the fingerprint from `signing-cert.sha1`,
    nothing when it is missing, exit 1 when invalid), `.github/scripts/signing-keychain.sh` (`add|remove <keychain>`
    on the `security list-keychains -d user` search list, keeping the others in order; `check <keychain> <sha1>`
    through `find-identity -p codesigning` without `-v`; never prints `security`'s output, no password argument; exit 1
    on a `security` failure, 2 on wrong usage); `.github/signing/entitlements.plist`.
  - `tests/ControlRoom.Server.Tests/`: one test class per unit or concern (`LaunchAgentScriptTests` runs the script with fake
    `launchctl`/`dotnet`/`curl`/`codesign` in a temp `HOME`, runs every `install --release` from its own copy of the
    scripts (`ScriptFixtures.BuildReleaseTree`, so the real `signing-cert.sha1` is never read) and builds release
    tarballs with `/usr/bin/tar`; `UpdaterScriptTests` (with its part `UpdaterSelfUpdateTests.cs`) runs `updater.sh` with fake
    `launchctl`/`curl`/`sleep`/`mv`/`codesign`; `ScriptFixtures.UpdaterPlist` is the expected updater plist for both; `ScriptFixtures` holds the fake `codesign` and a `PATH` folder without it;
    `ReleaseWorkflowTests` reads `release.yml` as text and runs `.github/scripts/signing-cert.sh`;
    `SigningKeychainScriptTests` runs `signing-keychain.sh` with a fake `security` first on `PATH`); `TempFactoryRoot` (`InitGit`, `AddBareRemote`
    and `Git` for a real local repo), `TempTokenFolder` and `ServerFactory` (own temp token file; clients send `Host: 127.0.0.1:8100`; optional tailnet name;
    `CreateTokenClient()` for API calls; `Push` fake push service, `Update` fake GitHub (`FakeUpdateHandler`), `CurrentVersion` (default `dev`),
    `Time` fixed clock whose timers never fire (it records their due time and period; `Zone` sets its
    `LocalTimeZone`, default UTC), `Logs`) are the shared fixtures; `PushFakes` holds `FakeTime`, `FakePushHandler`, `FakeUpdateHandler`, `PushReceiver` (test-side
    decrypt) and `LogCapture`.
- **Style:** C# like Context: file-scoped namespaces, `internal sealed` unless a type must be public, no
  primary constructors, no `var`, braces on every body, camelCase private fields without `_`; the build
  stays at 0 warnings. The web app is mobile first (iPhone width), works in light and dark mode and uses
  fixed error texts, never exception messages.
- **Git:** `develop` is the working branch: factory work branches off it as `factory/<job-id>` and is merged back with `--no-ff`. `main` holds releases only and is pushed to only for a new release.
