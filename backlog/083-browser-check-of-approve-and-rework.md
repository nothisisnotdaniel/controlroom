# 083 Browser check of Approve and Rework on an escalated job

As the operator, I want the Approve, Rework and Cancel actions on a needs-human job checked in a real browser on an iPhone-width screen, so that I know a request is written and shown as pending.

Category: test

## What

With a job in needs-human, open ControlRoom at 375 px wide in light and dark mode; send Approve, Rework with a note, and Cancel; check each writes factory/jobs/<id>/human.json with the right action, the job shows the request as pending, and errors (a too-long note, a second request) read clearly.

## Why

Reviewers of job 262 could only read the code; nobody has used the new actions in a browser.

## Notes

Proposed by job 262-act-on-escalated (`factory/jobs/262-act-on-escalated/`).
