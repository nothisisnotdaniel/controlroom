# 014 Numbered backlog stories

As the user, I want ControlRoom to show backlog items by number and open their story files, so that I can browse the backlog and decide on items without asking for context in a chat.

## What

Each open, `[?]` and `[h]` backlog line now reads `- [m] [#N](backlog/NNN-slug.md) Title (after: #3; Times#5)`, and its user story (what to do and why) lives in that file. Parse this format and show each item as `#N Title` with its mark. Tapping an item opens its story, rendered from Markdown with raw HTML off; only files under `<project>/backlog/` of a discovered project may be read (no `..`, no absolute paths, no symlinks out). Show `(after: …)` dependencies as links to the items they wait for (another project's item as `Project#N`), and mark an item whose dependency is still open as waiting. Done lines keep the old free-text format (`- [x] … → <job-id>`) and must still show in the done group. Full path, high risk (file access).

## Why

The factory moved every open backlog item into its own numbered story file so that decisions can be made by reading the backlog alone. ControlRoom is where the backlog is read on the phone, so it has to show those stories; today it would show raw Markdown links and no story text.

## Notes

Item #4 (Edit backlogs) should create and remove story files along with the lines.
