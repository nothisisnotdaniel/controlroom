# ControlRoom backlog

`/factory next --project ControlRoom` runs the first open item, checks it off and records the job id.
Each item is a user story in its own file under `backlog/`, numbered `NNN-slug.md`; the line links it as `#N`.
The order of the lines is the priority. Add items with `python3 factory/tools/job.py add --project ControlRoom '<json>'`
(or by hand: write the story file first, then the line), and keep a number once it is given.
`- [?]` items are follow-ups the factory proposed: change `[?]` to `[ ]` to approve one, or delete the line and its file.
`- [h]` items are tasks for you. `(after: #3)` waits until item 3 is done; `(after: Times#5)` names another project's item.

- [x] Server and backlogs view: set up the stack (see CLAUDE.md) with a server on `127.0.0.1:8100` that finds the projects in the factory root and serves a mobile-first web app showing every project's backlog, read-only: one section per project, its lines grouped as open, waiting for approval (`[?]`), for the user (`[h]`) and done (collapsed), in file order, with the line title in bold and the rest of the line below. A project whose backlog can't be read shows a fixed red line. Tests use a temporary factory root with sample projects, never the real one. → 125-server-backlogs-view
- [ ] [#1](backlog/001-installable-web-app.md) Installable web app
- [ ] [#2](backlog/002-factory-dashboard.md) Factory dashboard
- [ ] [#3](backlog/003-access-token.md) Access token
- [ ] [#4](backlog/004-edit-backlogs.md) Edit backlogs (after: #3)
- [ ] [#5](backlog/005-start-at-login.md) Start at login
- [ ] [#6](backlog/006-push-notifications.md) Push notifications (after: #1; #3)
- [ ] [#7](backlog/007-host-header-check.md) Host-header check (after: #3)
- [ ] [#8](backlog/008-done-group-marker-style.md) Done-group marker style
- [ ] [#9](backlog/009-empty-titles-and-retry-hint.md) Empty titles and retry hint
- [ ] [#10](backlog/010-ac-1-test-without-reflection.md) AC-1 test without reflection
- [h] [#11](backlog/011-tailscale.md) Tailscale
- [?] [#12](backlog/012-start-factory-runs-from-the-phone.md) Start factory runs from the phone
- [?] [#13](backlog/013-releases-overview.md) Releases overview
