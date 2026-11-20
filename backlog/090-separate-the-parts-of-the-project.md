# 090 Separate the parts of the project status line

As the operator, I want the CI result and the develop/main count on each Backlogs project to be clearly separated, so that I can read the status line at a glance.

Category: ui

## What

The .project-status line in app.css/status.js shows its parts with only a 10px gap between them. Add a visible separator (for example a middle dot) that screen readers don't announce, or a clearer layout, and check it at 375 px in light and dark mode.

## Why

Review-ui (job 269) noted that the gap is the only cue; the builder left it because the separator choice also affects what screen readers read.

## Notes

Proposed by job 269-ci-and-git-status (`factory/jobs/269-ci-and-git-status/`).
