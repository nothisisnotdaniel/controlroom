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
    with a fixed argument list (`ProcessStartInfo.ArgumentList`), never through a shell.
  - Paths come only from the discovered project list and the jobs folder, never from a request as a path;
    names from a request are matched against that list.
  - Writes are limited to a project's `backlog.md` and its `backlog/NNN-slug.md` story files: only through
    `POST /api/backlog/change`, one change at a time, only on its `develop` branch, only with a clean
    `backlog.md` and `backlog/`, checked against the version (SHA-256) the client saw (refused as changed
    otherwise), written atomically without following symlinks (`SafeFileWriter`), then committed in that
    project's repo with only the changed files (and pushed when there is an `origin`). The actions are approve,
    drop, up/down/top, add, title, story and `done` (only a `[h]` line becomes `[x]`). Nothing else in the
    factory root is written.
  - Text from files (backlog lines, job records) is shown as text, never as raw HTML; Markdown is rendered
    with raw HTML turned off.
  - Push files live next to the token file (never in the factory root): `push-keys.json` (VAPID P-256 key pair,
    a secret like the token) and `push.json` (subscriptions, `notifyDone`, `notifyUpdate` (default on) and `updateNotified`, `updateResultNotified` (the last updater run sent)). Both `0600`, written atomically
    (`0600` temp file, then rename); their folder must be `0700` and owned by the current user (created `0700`
    when missing; owner read with `lstat` in `UnixOwner`). A symlinked, loose-mode or invalid file or folder (or
    one owned by someone else) makes every `/api/push*` route return `500 {"error":"push_store"}` and is never
    overwritten. `POST /api/push/test` sends at most once per 10 s server-wide (`PushTestLimiter` on
    `TimeProvider`); a call inside that window returns `429 {"error":"too_many_requests"}` and sends nothing.
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
  when no tag; no .NET SDK needed; launchd runs `server/ControlRoom.Server` directly). It also installs the
  updater (`local.controlroom.updater`, every 6 h, `<support>/updater.sh`, marker `<support>/installed-release`, log
  `~/Library/Logs/ControlRoom/updater.log`), which installs newer releases (by SemVer; on `stable` prereleases are
  ignored, on `develop` the newest `vX.Y.Z` or `vX.Y.Z-develop.N`) only while `settings.json` says on and
  no job is in `spike|spec|build|review`; a plain `install` and `uninstall` remove it.
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
  -p:Version=X.Y.Z`, smoke-checks `ControlRoom.Server version`, packs `controlroom-X.Y.Z-osx-arm64.tar.gz` (one top
  folder), writes `SHA256SUMS`, attests build provenance and runs `gh release create vX.Y.Z` on the merge commit or pushed commit
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
    Kestrel, headers, the `HostCheck`, GET/HEAD only (POST only on `/api/backlog/change`, `/api/update/settings` and the four `/api/push/*` POST routes), the `/api/` token gate, static files, endpoints, push services), `AccessToken` (token file
    path, create, safe read, atomic replace, constant-time check), `TokenCommand` (`token`, `token --new`), `HostCheck` (allowed `Host` values, `TailnetHost` rule, fixed
    `421` body), `ServerEndpoint` (the one listen address), `FactoryRootLocator`,
    `ProjectFinder` (project discovery), `SafeFileReader` (no-follow, regular-file, size-capped text or bytes read, SHA-256 version),
    `SafeFileWriter` (temp file in the same folder, then rename; refuses a symlinked target or folder),
    `BacklogReader` (safe `backlog.md` read), `BacklogParser` (lines to entries, old and numbered `[#N](…)`
    format), `BacklogWaiting` (the Ready/Waiting rule: a dependency is met only by a done line of the named, or the
    same, project; unknown counts as unmet), `BacklogsEndpoint` (`GET /api/backlogs`, with `waiting`, `line` and
    the file `version`), `BacklogEditor` (`EditableText` lines that keep their bytes; approve, drop, done, move, title
    and add edits, slug, next number and story template like `job.py`), `ProjectGit` (fixed-argument git calls
    with timeouts and `GIT_TERMINAL_PROMPT=0`), `BacklogChangeEndpoint` (`POST /api/backlog/change`: request
    checks, `not_develop`/`dirty`/`changed`, write, commit, push; `done` commits `backlog: done #N`), `StoryReader` (fenced read of
    `<project>/backlog/NNN-slug.md`), `StoryMarkdown` (Markdown to a JSON block tree, no HTML),
    `StoryEndpoint` (`GET /api/story?project=&number=`, with `markdown`
    and `storyVersion`), `InboxEndpoint` (`GET /api/inbox`: every `[?]` line as `{project, version, line, number, title,
    rest, job}` plus `unreadable` project names, `no-store`; `job` from the first `Proposed by job <id>` in the story's
    `## Notes` through `StoryReader`, kept only as `^[0-9]{1,6}-[a-z0-9]+(-[a-z0-9]+)*$` up to 80 chars; with a job first by
    its number, then without, ties by project (ordinal) and line), `TasksEndpoint` (`GET /api/tasks`: every `[h]` line as
    `{project, version, line, number, title, rest, story, after}` split into `ready` and `waiting` by `BacklogWaiting`
    (`after` only the unmet dependencies, `project` always set), plus `unreadable` names, `no-store`; project order, then
    file order), `JobReader` (`factory/jobs/<id>/state.json` to a typed
    `JobSummary`), `BoardEndpoint` (`GET /api/board`: jobs and `[h]` tasks), `ChangeFingerprint` (SHA-256 fingerprints from `lstat`
    metadata only: `jobs` over each `state.json` of `JobReader.ListFolders`, per project `backlog` over `backlog.md`
    and `stories` over `backlog/*.md`; a symlink adds only a `link` marker, a missing path `missing`; `NoFollowStat`:
    `lstat`, `O_NOFOLLOW` folder listing and `fstatat` via P/Invoke, never a file opened or a target followed),
    `ChangesEndpoint` (`GET /api/changes`: `{"jobs":…,"projects":{"<name>":{"backlog":…,"stories":…}}}`, `no-store`), `JobEndpoint` (`GET /api/job?id=`:
    a job and its records from a fixed list), `PushCrypto` (RFC 8291 `aes128gcm`, RFC 8292 VAPID ES256 JWT, P-256
    point check, `Base64Url`), `PrivateFolder` (the `0700` folder next to the token file: no-follow `0600` reads,
    atomic writes, `PrivateFileException`), `PushStore` (`push-keys.json`/`push.json` next to the token file;
    `PushSubscriptionRules`: endpoint host allowlist and key checks), `PushSender` (`PushPayload` with the fixed
    reason table, `PushTransport` (the injectable `HttpMessageHandler`), one POST per subscription, `404`/`410`
    removes it), `PushWatcher` (hosted service, `Scan()` every 30 s on `TimeProvider`; first scan is the baseline),
    `PushEndpoint` (`GET /api/push`, `POST /api/push/subscribe|unsubscribe|settings|test`), `PushTestLimiter`
    (one test send per 10 s), `UnixOwner` (`geteuid`, `lstat` uid via P/Invoke), `AppVersion` (informational version without
    `+build`, else `dev`), `VersionCommand` (`version`), `VersionEndpoint` (`GET /api/version`), `UpdateCheck` (`SemVer` (2.0.0 precedence, no build metadata),
    `RunningVersion`, `UpdateTransport` (the injectable handler and timeout), `UpdateState` (latest release, in
    memory), `UpdateChecker` (hosted service, `Check()` at start and every 6 h on `TimeProvider`, the `update` push
    once per version)), `UpdateEndpoint` (`GET /api/update`, `POST /api/update/settings`), `UpdateSettingsStore` (`settings.json`),
    `UpdateResult` (`update-result.json` schema and `UpdateResultReader`), `HealthEndpoint` (`GET /health`), `wwwroot/` (`index.html`, `app.css`, `token.js` (stored token under
    `controlroom-token`, `authFetch` for every `/api/` call, the token view), `app.js` (routing, backlogs,
    story), `edit.js` (per-project edit mode, the Add form, Edit story, fixed texts per error code), `board.js` (jobs board and job page (a read-only state section between the facts and the record tabs: stage and round, review rows per round with visible values, decision chip, CI and note), `pollBoard` fetched by `live.js`; the `ControlRoom <version>`
    line at the foot of Jobs and the update banner under its heading, each asked once per page load), `live.js` (the one `setInterval`: asks `/api/changes` every 5 s while visible, no token view and nothing
    pending, and at once on becoming visible; refetches only the board, the changed backlog sections (kept while a form
    is open or focus is inside) and the open story (in place, never while Edit story is open); a failed check shows
    the fixed `Connection lost. Reconnecting…` notice under the header, or in the `board-status` line on the board;
    script order `token.js`, `board.js`, `push.js`, `edit.js`, `inbox.js`, `tasks.js`, `live.js`, `app.js`; a changed
    `backlog` or `stories` fingerprint refetches the inbox and the tasks and their counts), `inbox.js` (the `#inbox` view: every `[?]` item with project,
    `#N Title`, `Proposed by <job>` link, Approve and Drop through `sendChange` (never rebuilt while `changeBusy`), fixed
    texts; the nav count, hidden at 0 or unknown, fetched once per page load, after a token is saved and after every
    committed change), `tasks.js` (the `#tasks` view: `Ready now` and `Waiting` sections of `[h]` items with project,
    `#N Title` story link, `rest` and `Waits for` links (`Project#N` or `#N`), a Done button on Ready items only
    (confirm, then `action:"done"` through `sendChange`, never rebuilt while `changeBusy`), fixed texts in `TasksTexts`;
    the nav count of Ready items, hidden at 0 or unknown, fetched as the inbox count), `push.js` (Notifications section on the
    board: state texts, Turn on (permission only from the tap), done and update switches, Send test, Turn off),
    `sw.js` (service worker: caches only the fixed app shell, never `/api/`; bump its cache version when the
    shell list changes; `push` shows a notification from fixed titles, `notificationclick` opens `#job/<id>`,
    `#backlogs` or `#jobs`; `/health` is never cached), `manifest.webmanifest`, `icons/` (opaque RGB PNGs, 180/192/512 px)).
  - `scripts/launch-agent.sh`: POSIX `sh` LaunchAgent installer (publish, `bootout`, replace, plist `0644`,
    `bootstrap`; refuses symlinks; never reads the token). With `--release` it runs `curl`, `tar` and `shasum`
    instead of `dotnet`: tag and latest-release redirect checked as `v`+SemVer, downloads into a `mktemp -d` folder
    in the support folder (removed on every exit), `curl --max-filesize` 200 MiB for the tarball and 4 KiB
    for `SHA256SUMS`, one matching `SHA256SUMS` line, archive entries only regular files and folders under
    `controlroom-<ver>-osx-arm64/`, at most 10000 entries and 512 MiB of unpacked content (counted through a pipe
    before extraction), checked again after extraction. Not part of the server.
  - `scripts/updater.sh`: POSIX `sh` updater run by launchd (`--factory-root <root>`); prints exactly one log line
    `<UTC> <word>[ <tags>]` (`off`, `not_release`, `busy`, `job_running`, `current`, `latest_failed`,
    `download_failed`, `check_failed`, `skipped_failed`, `updated`, `rolled_back`, `swap_failed`,
    `rollback_failed`). Lock `<support>/.update.lock` (stale after 1 h); swap: bootout, `server` to
    `server.previous`, new folder to `server`, bootstrap, poll `/health` up to 15 times (an answer counts only while
    `launchctl print` shows the server running and its pid is the only listener `lsof` shows on port 8100); rolls
    back when it does not answer, and (as `swap_failed`) when the marker can't be updated. A `state.json` over
    64 KiB counts as a running job. `valid_tag` (`v`+SemVer, `-develop.N` allowed, no build
    metadata), `latest_tag`, `download` and `check_and_extract` are byte-identical to `launch-agent.sh` (a test
    compares them). The channel comes from `settings.json` (at most 64 bytes).
  - `.github/workflows/release.yml` and `.github/scripts/release-version.sh` (title `release vX.Y.Z` to `X.Y.Z`;
    existing tags on stdin), `.github/scripts/develop-version.sh` (run number to the next `-develop.<run>`
    version; tags on stdin) and `.github/scripts/old-prereleases.sh` (version to the `-develop` tags at or below
    it; tags on stdin).
  - `tests/ControlRoom.Server.Tests/`: one test class per unit or concern (`LaunchAgentScriptTests` runs the script with fake
    `launchctl`/`dotnet`/`curl` in a temp `HOME` and builds release tarballs with `/usr/bin/tar`; `UpdaterScriptTests` runs `updater.sh` with fake
    `launchctl`/`curl`/`sleep`/`mv`;
    `ReleaseWorkflowTests` reads `release.yml` as text); `TempFactoryRoot` (`InitGit`, `AddBareRemote`
    and `Git` for a real local repo), `TempTokenFolder` and `ServerFactory` (own temp token file; clients send `Host: 127.0.0.1:8100`; optional tailnet name;
    `CreateTokenClient()` for API calls; `Push` fake push service, `Update` fake GitHub (`FakeUpdateHandler`), `CurrentVersion` (default `dev`),
    `Time` fixed clock whose timers never fire (it records their due time and period), `Logs`) are the shared fixtures; `PushFakes` holds `FakeTime`, `FakePushHandler`, `FakeUpdateHandler`, `PushReceiver` (test-side
    decrypt) and `LogCapture`.
- **Style:** C# like Context: file-scoped namespaces, `internal sealed` unless a type must be public, no
  primary constructors, no `var`, braces on every body, camelCase private fields without `_`; the build
  stays at 0 warnings. The web app is mobile first (iPhone width), works in light and dark mode and uses
  fixed error texts, never exception messages.
- **Git:** `develop` is the working branch: factory work branches off it as `factory/<job-id>` and is merged back with `--no-ff`. `main` holds releases only and is pushed to only for a new release.
