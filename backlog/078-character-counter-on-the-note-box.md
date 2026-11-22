# 078 Character counter on the note box

As the operator, I want to see how many characters my note has left, so that a long note is not cut off without warning.

Category: ux

## What

The note textarea in edit.js has maxlength 1000 and silently stops at the limit. Show a small remaining-characters count near it, readable in light and dark mode.

## Why

Review-ui (job 259) noted there is no visible counter; on a phone the cut-off is easy to miss.

## Notes

Proposed by job 259-notes-on-story (`factory/jobs/259-notes-on-story/`).
