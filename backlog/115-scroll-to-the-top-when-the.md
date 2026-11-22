# 115 Scroll to the top when the Backlogs project changes

As the user, I want the Backlogs view to start at the top after I pick another project, so that a short backlog never shows up scrolled past its start.

Category: ux

## What

In app.js applyBacklogChoice, reset the scroll position to the top of the Backlogs view when the user picks another project in the picker (not on live refreshes of the same project).

## Why

Raised by the job 340 ux review (nit 1): switching project keeps the old scroll position.

## Notes

Proposed by job 340-backlog-project-picker (`factory/jobs/340-backlog-project-picker/`).
