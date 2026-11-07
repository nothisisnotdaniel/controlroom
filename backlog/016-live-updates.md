# 016 Live updates

As the user, I want the board and the backlogs to update on their own when something changes, so that I see a job move or a new item arrive without reloading.

## What

The server notices changes to `factory/jobs/*/state.json` and the projects' `backlog.md` and `backlog/` files and tells open pages, for example with server-sent events or a cheap version check the page polls; the spec picks one and says why. A page refreshes only the part that changed and keeps the scroll position and any open story. When the connection drops, the page says so in a fixed text and reconnects. No new package. Full path.

## Why

The factory runs for long stretches, and the phone is where I check on it. A view that only changes on reload shows stale state, which is exactly the mistake the installable app (#1) is designed to avoid.
