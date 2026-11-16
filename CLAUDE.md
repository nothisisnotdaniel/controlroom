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
  the local port), so nothing is reachable from the internet. Every request that changes something needs
  the app's access token; reads may need it too (the spec that adds it decides).
- **Safety rules:**
  - The server never runs a shell command, `claude` or any program built from request input. Git is called
    with a fixed argument list (`ProcessStartInfo.ArgumentList`), never through a shell.
  - Paths come only from the discovered project list and the jobs folder, never from a request as a path;
    names from a request are matched against that list.
  - Writes are limited to a project's `backlog.md`: only on its `develop` branch, only with a clean
    `backlog.md`, checked against the version the client saw (refused as changed otherwise), then committed
    in that project's repo with only that file. Nothing else in the factory root is written.
  - Text from files (backlog lines, job records) is shown as text, never as raw HTML; Markdown is rendered
    with raw HTML turned off.
- **Stack:** .NET 10 ASP.NET Core minimal API (`Microsoft.NET.Sdk.Web`, no extra packages) serving a plain
  HTML/CSS/JS web app from its own `wwwroot` (no CDN, no build step, strict CSP), xUnit tests with
  `Microsoft.AspNetCore.Mvc.Testing` to host the app in memory. No new package without a reason in the spec.
  Run: `dotnet run --project src/ControlRoom.Server` (listens on `127.0.0.1:8100` only; the factory root is the
  parent of the folder holding `ControlRoom.slnx`, or `--FactoryRoot <path>`).
- **Test command:** `dotnet test` (from `ControlRoom/`; `dotnet build` must stay at 0 warnings). Tests build a
  temporary factory root (`TempFactoryRoot`) and host the server with `ServerFactory`; no test reads the real
  factory root or opens a real port.
- **Layout:**
  - `ControlRoom.slnx`, `Directory.Build.props` (nullable, implicit usings, code style in build), `.editorconfig`.
  - `src/ControlRoom.Server/`: `Program.cs` (entry), `ServerApp` (builds the app: Kestrel, headers, GET/HEAD
    only, static files, endpoints), `ServerEndpoint` (the one listen address), `FactoryRootLocator`,
    `ProjectFinder` (project discovery), `SafeFileReader` (no-follow, regular-file, size-capped text read),
    `BacklogReader` (safe `backlog.md` read), `BacklogParser` (lines to entries, old and numbered `[#N](…)`
    format), `BacklogsEndpoint` (`GET /api/backlogs`, with `waiting`), `StoryReader` (fenced read of
    `<project>/backlog/NNN-slug.md`), `StoryMarkdown` (Markdown to a JSON block tree, no HTML),
    `StoryEndpoint` (`GET /api/story?project=&number=`), `JobReader` (`factory/jobs/<id>/state.json` to a typed
    `JobSummary`), `BoardEndpoint` (`GET /api/board`: jobs and `[h]` tasks), `JobEndpoint` (`GET /api/job?id=`:
    a job and its records from a fixed list), `wwwroot/` (`index.html`, `app.css`, `app.js` (routing, backlogs,
    story), `board.js` (jobs board and job page, polls every 5 s while visible),
    `sw.js` (service worker: caches only the fixed app shell, never `/api/`; bump its cache version when the
    shell list changes), `manifest.webmanifest`, `icons/` (opaque RGB PNGs, 180/192/512 px)).
  - `tests/ControlRoom.Server.Tests/`: one test class per unit or concern; `TempFactoryRoot` and
    `ServerFactory` are the shared fixtures.
- **Style:** C# like Context: file-scoped namespaces, `internal sealed` unless a type must be public, no
  primary constructors, no `var`, braces on every body, camelCase private fields without `_`; the build
  stays at 0 warnings. The web app is mobile first (iPhone width), works in light and dark mode and uses
  fixed error texts, never exception messages.
- **Git:** `develop` is the working branch: factory work branches off it as `factory/<job-id>` and is merged back with `--no-ff`. `main` holds releases only and is pushed to only for a new release.
