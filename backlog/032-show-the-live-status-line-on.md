# 032 Show the live status line on the job page

As the ControlRoom user, I want the job page to show the same `Live · <time>` and `Can't reach the server.` line as the board, so that I notice when its live refresh has stopped.

Category: ui

## What

Add the board's status line to the job page in board.js, updated on each poll with the same fixed texts. Add tests for the success and failure texts.

## Why

The UX review of job 183 noted that a failed poll on a job page gives no visible sign, so the user may read a stale job without knowing it.

## Notes

Proposed by job 183-factory-dashboard (review-ux round 1 nit 2).

Proposed by job 183-factory-dashboard (`factory/jobs/183-factory-dashboard/`).
