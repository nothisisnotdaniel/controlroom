# 004 Edit backlogs

As the user, I want to approve, drop, add, reorder and edit backlog items from ControlRoom, so that I can make backlog decisions from my phone without opening a Claude session.

## What

From the backlogs view, approve a `[?]` line (to `[ ]`), drop it, add a line at the top or the bottom, move a line up, down or to the top, and edit an item's title or story. Adding an item writes its numbered story file (`backlog/NNN-slug.md`, next number = highest + 1) in the same commit as the line, the way `factory/tools/job.py add` does; dropping one removes its line and its file. Every change follows the write rules in CLAUDE.md (develop only, clean `backlog.md`, version check, one commit per change with a clear message) and then pushes `develop` when the project has a remote; a refused change says why in a fixed text. Full path, high risk.

## Why

Reading stories on the phone is only half of it: the decisions (approve a `[?]`, move something up) still need a session today. Each change is a small, checked commit on `develop`, so the factory and git history stay consistent.
