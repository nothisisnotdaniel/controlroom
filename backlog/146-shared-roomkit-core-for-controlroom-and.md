# 146 Shared RoomKit core for ControlRoom and PlayRoom

As the owner of two factories, I want the parts every factory control app needs in one shared library, so that ControlRoom and PlayRoom (the GameFactory app) get the same fixes for sign-in, push, updates and backlog editing without copying code.

Category: tidy-up

## What

Split ControlRoom into a reusable library `RoomKit` and the ControlRoom app on top of it, in this repo: `src/RoomKit/` (a Razor class library for the shared server code and static web assets) and `src/ControlRoom.Server/` using it. RoomKit holds what is not about this factory's job format: the access token, host header and tailnet listening, the installable web app shell (manifest, service worker, bottom tab bar, token page), push notifications, the updater and launch agent scripts, update settings, the backlog reader and editor (stories, marks, drafts, stop line, backlog branch), search, history and token totals, and the health view. Everything app-specific becomes configuration or an interface the app supplies: app name and icon, data folder, LaunchAgent labels, default port, release repository, the tabs, and a job-format interface (stages, review keys, paths, which stages are active or blocked) that ControlRoom implements for today's `state.json`. Behaviour of ControlRoom stays the same: every existing test passes, moved where needed. RoomKit is packed as a NuGet package by the release workflow (version follows ControlRoom's), so another app can reference it; the spec picks the feed (GitHub Packages or a local folder feed) and says why. `docs/roomkit.md` explains what is in it and how an app plugs in.

## Why

GameFactory needs its own control app, PlayRoom, with game-specific views (playtest reports, screenshots, feel checks, adapter status). A plain copy would drift: every fix to sign-in, push, the updater or backlog editing would have to be made twice. A shared core keeps both apps in step.

## Notes

The user chose a shared core library over a fork on 2026-11-22. PlayRoom itself is built in the GameFactory repo (/Volumes/External/GameFactory, BUILD_PROMPT.md stage 8) once RoomKit is packaged.
