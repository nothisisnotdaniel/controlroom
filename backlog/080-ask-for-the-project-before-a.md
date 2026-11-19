# 080 Ask for the project before a capture is saved

As the operator, I want Capture to make me choose the project, so that a quick idea never lands in the wrong backlog.

## What

capture.js fillCaptureProjects silently preselects the first project when none is stored (AC-7 of job 261). Add a "Choose a project" placeholder and keep Save disabled until a project is picked, and update the AC-7 test.

## Why

Review-ux (job 261) noted that a hurried save can go to the wrong project without the user noticing.

## Notes

Proposed by job 261-quick-capture (`factory/jobs/261-quick-capture/`).
