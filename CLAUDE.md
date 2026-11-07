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
- **Stack:** none yet. The first factory job picks it within these bounds: .NET 10 (ASP.NET Core minimal
  API) for the server, a plain HTML/CSS/JS web app served by it (no CDN, a strict CSP), xUnit tests. No new
  package without a reason in the spec. Its builder fills in this line, the test command and the layout.
- **Test command:** none yet.
- **Layout:** none yet.
- **Style:** C# like Context: file-scoped namespaces, `internal sealed` unless a type must be public, no
  primary constructors, no `var`, braces on every body, camelCase private fields without `_`; the build
  stays at 0 warnings. The web app is mobile first (iPhone width), works in light and dark mode and uses
  fixed error texts, never exception messages.
- **Git:** `develop` is the working branch: factory work branches off it as `factory/<job-id>` and is merged back with `--no-ff`. `main` holds releases only and is pushed to only for a new release.
