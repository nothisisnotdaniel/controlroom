# 180 Keep a scrollable list of sent notifications in a Notifications tab

As someone who gets ControlRoom push notifications, I want to scroll back through them inside the app, so that they do not just pile up and vanish in the iPhone notification center.

Category: feature

## What

Each time the server sends a Web Push, append an entry (time, kind, project, job, stage, version, reason, count: the same fixed fields as the payload, never free text from elsewhere) to a log in the private folder, with the same 0600/0700 rules as push.json, keeping the newest 100 and writing atomically. Add an authenticated API that returns the entries newest first, and a Notifications tab in the tab bar (next to Capture, before …) that shows them as a list: title as in the service worker table, short body, relative time, each linking to its job or backlog item when it has one. Mark entries newer than the last visit as unread and show their number on the tab. Document the file, the API and the tab in ControlRoom CLAUDE.md.

## Why

iOS notifications are easy to clear or miss; an in-app list makes them a history you can go back through.
