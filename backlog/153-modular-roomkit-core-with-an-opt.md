# 153 Modular RoomKit core with an opt-in factory module

As the builder of RoomKit apps, I want a RoomKit core that serves no factory routes unless an app opts in, so that a non-factory app such as PRWatch can use RoomKit's shell, security and updates without the backlog and job features.

Category: feature

## What

Split `RoomKitApp.Run` into a core that is always on and a factory module an app adds on purpose. Core: host check, loopback binding, token, security headers, the version and token commands, health, update check and settings, the app shell, safe file I/O (`SafeFileWriter`, `PrivateFolder`) and the launch-agent and updater scripts. Factory module: backlogs and backlog editing, stories, search, capture, inbox, tasks, history, doctor, the job push watcher and `IJobFormat`, with their scripts and `room.js` keys. `IJobFormat` is required only by the factory module, and `BacklogWorktree` starts only with it. Make push a core service with pluggable event sources, so the factory module registers the job watcher and other apps can register their own. ControlRoom and PlayRoom opt in and behave exactly as before.

## Why

Today `RoomKitApp.Run` maps every shared endpoint and requires `IJobFormat`, and `BacklogWorktree` would clash with ControlRoom's worktree of the `backlog` branch. PRWatch, the third RoomKit app, has no backlog or jobs, so it can't use RoomKit until the factory parts are optional.

## Notes

Part of the PRWatch plan (plan-next-week.md, step 4). Land it before PlayRoom is built on RoomKit, then release ControlRoom. Tests: ControlRoom and PlayRoom tests unchanged; a core-only sample app serves health and version but none of the factory routes (404).
