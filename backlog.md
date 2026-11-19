# ControlRoom backlog

`/factory next --project ControlRoom` runs the first open item, checks it off and records the job id.
Each item is a user story in its own file under `backlog/`, numbered `NNN-slug.md`; the line links it as `#N`.
The order of the lines is the priority. Add items with `python3 factory/tools/job.py add --project ControlRoom '<json>'`
(or by hand: write the story file first, then the line), and keep a number once it is given.
`- [?]` items are follow-ups the factory proposed: change `[?]` to `[ ]` to approve one, or delete the line and its file.
`- [h]` items are tasks for you. `(after: #3)` waits until item 3 is done; `(after: Times#5)` names another project's item.

- [x] Server and backlogs view: set up the stack (see CLAUDE.md) with a server on `127.0.0.1:8100` that finds the projects in the factory root and serves a mobile-first web app showing every project's backlog, read-only: one section per project, its lines grouped as open, waiting for approval (`[?]`), for the user (`[h]`) and done (collapsed), in file order, with the line title in bold and the rest of the line below. A project whose backlog can't be read shows a fixed red line. Tests use a temporary factory root with sample projects, never the real one. → 125-server-backlogs-view
- [x] [#14](backlog/014-numbered-backlog-stories.md) Numbered backlog stories → 181-numbered-backlog-stories
- [x] [#1](backlog/001-installable-web-app.md) Installable web app → 182-installable-web-app
- [x] [#2](backlog/002-factory-dashboard.md) Factory dashboard → 183-factory-dashboard
- [x] [#3](backlog/003-access-token.md) Access token → 184-access-token
- [x] [#4](backlog/004-edit-backlogs.md) Edit backlogs (after: #3) → 185-edit-backlogs
- [x] [#5](backlog/005-start-at-login.md) Start at login → 186-start-at-login
- [x] [#6](backlog/006-push-notifications.md) Push notifications (after: #1; #3) → 187-push-notifications
- [x] [#7](backlog/007-host-header-check.md) Host-header check (after: #3) → 188-host-header-check
- [x] [#8](backlog/008-done-group-marker-style.md) Done-group marker style → 189-done-marker-style
- [x] [#9](backlog/009-empty-titles-and-retry-hint.md) Empty titles and retry hint → 190-empty-titles-retry-hint
- [x] [#10](backlog/010-ac-1-test-without-reflection.md) AC-1 test without reflection → 191-listen-test-no-reflection
- [h] [#11](backlog/011-tailscale.md) Tailscale
- [?] [#12](backlog/012-start-factory-runs-from-the-phone.md) Start factory runs from the phone
- [?] [#13](backlog/013-releases-overview.md) Releases overview
- [x] [#15](backlog/015-job-detail.md) Job detail (after: #2) → 248-job-detail-gaps
- [x] [#16](backlog/016-live-updates.md) Live updates → 249-live-board-updates
- [ ] [#17](backlog/017-approval-inbox.md) Approval inbox (after: #4)
- [ ] [#27](backlog/027-my-tasks.md) My tasks (after: #4; #14)
- [ ] [#18](backlog/018-search-and-filter.md) Search and filter
- [ ] [#19](backlog/019-next-and-blocked.md) Next and blocked (after: #14)
- [ ] [#20](backlog/020-notes-on-a-story.md) Notes on a story (after: #3)
- [ ] [#21](backlog/021-quick-capture.md) Quick capture (after: #3)
- [ ] [#22](backlog/022-act-on-escalated-jobs.md) Act on escalated jobs (after: #2; #3)
- [ ] [#23](backlog/023-costs-and-history.md) Costs and history (after: #2)
- [ ] [#24](backlog/024-doctor-view.md) Doctor view
- [ ] [#25](backlog/025-ci-and-git-status.md) CI and git status
- [ ] [#26](backlog/026-usage-view-spike.md) Usage view spike
- [?] [#28](backlog/028-close-the-symlink-race-when-reading.md) Close the symlink race when reading story files
- [?] [#29](backlog/029-drop-the-hand-written-equality-on.md) Drop the hand-written equality on BacklogEntry
- [?] [#30](backlog/030-cap-the-bytes-read-per-board.md) Cap the bytes read per board and job response
- [?] [#31](backlog/031-show-pending-reviewer-chips-with-a.md) Show pending reviewer chips with a text marker
- [?] [#32](backlog/032-show-the-live-status-line-on.md) Show the live status line on the job page
- [?] [#33](backlog/033-refuse-a-token-folder-others-can.md) Refuse a token folder others can write to
- [?] [#34](backlog/034-check-the-token-file-s-mode.md) Check the token file's mode and owner on the open handle
- [?] [#35](backlog/035-keep-the-token-in-memory-when.md) Keep the token in memory when localStorage is unavailable
- [?] [#36](backlog/036-hints-in-the-token-view.md) Hints in the token view
- [?] [#37](backlog/037-refuse-after-in-titles-regardless-of.md) Refuse (after: in titles regardless of case and spacing
- [?] [#38](backlog/038-clearer-validation-messages-in-the-backlog.md) Clearer validation messages in the backlog edit forms
- [?] [#39](backlog/039-keep-focus-and-the-open-add.md) Keep focus and the open Add form after an edit reloads the backlog
- [?] [#40](backlog/040-hint-to-run-status-after-installing.md) Hint to run status after installing the LaunchAgent
- [?] [#41](backlog/041-move-the-notifications-section-higher-on.md) Move the Notifications section higher on the board
- [?] [#42](backlog/042-do-not-start-the-test-send.md) Do not start the test-send wait when the test send fails
- [?] [#43](backlog/043-mention-tailnet-host-in-the-launch.md) Mention --tailnet-host in the launch-agent.sh usage text
- [?] [#44](backlog/044-clearer-refusal-when-the-tailnet-name.md) Clearer refusal when the tailnet name is not configured
- [x] [#45](backlog/045-release-builds-on-github.md) Release builds on GitHub → 225-release-builds-github
- [x] [#46](backlog/046-update-available-notice.md) Update available notice (after: #45) → 228-update-available-notice
- [x] [#47](backlog/047-automatic-updates.md) Automatic updates (after: #46) → 229-automatic-updates
- [ ] [#48](backlog/048-strict-semver-prerelease-identifiers-in-release.md) Strict SemVer prerelease identifiers in release tags
- [?] [#49](backlog/049-verify-release-signatures-in-the-updater.md) Verify release signatures in the updater without gh
- [?] [#50](backlog/050-updater-port-check-when-another-user.md) Updater port check when another user holds the port
- [x] [#51](backlog/051-develop-prerelease-builds-and-an-opt.md) Develop prerelease builds and an opt-in develop update channel → 234-develop-prereleases-channel
- [?] [#52](backlog/052-test-that-keeping-the-update-channel.md) Test that keeping the update channel starts no check
- [?] [#53](backlog/053-keep-focus-on-the-channel-select.md) Keep focus on the channel select after saving
- [?] [#54](backlog/054-hint-that-the-develop-channel-installs.md) Hint that the develop channel installs unreleased builds
- [?] [#55](backlog/055-limit-how-often-channel-changes-start.md) Limit how often channel changes start update checks
- [?] [#56](backlog/056-drop-a-develop-check-result-after.md) Drop a develop check result after switching back to stable
- [?] [#57](backlog/057-filter-drafts-in-the-updater-s.md) Filter drafts in the updater's develop tag pick too
- [?] [#58](backlog/058-guard-the-release-workflow-s-github.md) Guard the release workflow's GITHUB_ENV writes
- [?] [#59](backlog/059-behaviour-tests-for-the-web-app.md) Behaviour tests for the web app's JavaScript
- [?] [#60](backlog/060-calmer-colour-for-non-urgent-job.md) Calmer colour for non-urgent job notes
- [?] [#61](backlog/061-check-that-the-job-page-refreshes.md) Check that the job page refreshes in place
- [?] [#62](backlog/062-refresh-version-and-update-info-on.md) Refresh version and update info on live changes
- [?] [#63](backlog/063-browser-check-of-the-live-update.md) Browser check of the live-update notice
- [?] [#64](backlog/064-keep-stale-story-marks-across-route.md) Keep stale story marks across route changes
