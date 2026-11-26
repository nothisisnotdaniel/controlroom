# 065 Refresh the inbox count right after any backlog change

As the operator, I want the inbox count to update as soon as I change a backlog item anywhere in the app, so that the badge never shows a stale number.

Category: bug

## What

A change committed from the backlog or story view now updates the inbox count only on the next live check. Call loadInboxCount after every committed change in those views too.

## Why

Review-code (job 251) noted AC-11 asks for the count after every committed change; today the backlog and story views lag until the next live check.

## Notes

Proposed by job 251-approval-inbox (`factory/jobs/251-approval-inbox/`).

Done in RoomKit: edit.js calls loadBacklogCounts() after a backlog change (backlog audit, 2026-11-26).
