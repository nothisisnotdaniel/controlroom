# 039 Keep focus and the open Add form after an edit reloads the backlog

As the ControlRoom user, I want the page to keep my place after a change, so that I can make several edits in a row without finding my spot again.

Category: ux

## What

After a successful backlog change, when the project view reloads, put the focus back on the control that was used (or on the moved item's matching button) and keep the Add form open if it was open, with its fields cleared. Keep screen reader announcements of the notice working.

## Why

The UX review of job 185 found that the reload after each change drops the focus and closes the Add form. That is tiring for a series of edits and confusing for keyboard and screen reader users.

## Notes

Proposed by job 185-edit-backlogs (review-ux round 1, nit 3).

Proposed by job 185-edit-backlogs (`factory/jobs/185-edit-backlogs/`).
