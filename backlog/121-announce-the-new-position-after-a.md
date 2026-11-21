# 121 Announce the new position after a drag for screen readers

As a VoiceOver user, I want to hear where an item landed after I move it, so that I know the move worked without looking.

Category: ux

## What

After a successful drop or keyboard move in the backlog, announce `Moved to position N` (fixed text) through an aria-live region; on a refused move announce the fixed error text. Tests for the texts and the live region.

## Why

Raised by the job 345 ux review (nit 1): today a move gives no spoken feedback.

## Notes

Proposed by job 345-backlog-drag-drop (`factory/jobs/345-backlog-drag-drop/`).
