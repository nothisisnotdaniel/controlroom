# 038 Clearer validation messages in the backlog edit forms

As the ControlRoom user, I want each invalid edit to say what is wrong, so that I can fix my input without guessing.

## What

Give the `POST /api/backlog/change` validation failures distinct fixed error codes (for example empty title, title too long, title with a line break or `(after:`, story field too long), and show a fixed text for each in `edit.js`. Where it is cheap, check the same rules in the Add and title forms before sending. Never show exception messages.

## Why

The UX review of job 185 noted that every validation failure shows the same generic 'invalid' text, which doesn't tell the user what to change, especially on the phone.

## Notes

Proposed by job 185-edit-backlogs (review-ux round 1, nit 2).

Proposed by job 185-edit-backlogs (`factory/jobs/185-edit-backlogs/`).
