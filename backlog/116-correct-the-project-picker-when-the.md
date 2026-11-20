# 116 Correct the project picker when the chosen project disappears while focused

As the user, I want the project picker to show the project actually shown, so that the select never names a project that left the list.

Category: bug

## What

In app.js updateBacklogPicker, when the chosen project leaves the list while the select has focus, the sections switch to the fallback but the select keeps the stale option until the next refresh. Update the select once it loses focus (or at once if that is safe), with a test.

## Why

Raised by the job 340 ux review (nit 2): rare, but the picker can name a project that is no longer shown.

## Notes

Proposed by job 340-backlog-project-picker (`factory/jobs/340-backlog-project-picker/`).
