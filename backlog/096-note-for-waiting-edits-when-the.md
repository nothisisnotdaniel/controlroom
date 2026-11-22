# 096 Note for waiting edits when the backlog worktree cannot be read

As the user, I want the backlog page to tell me when my edits are waiting but the backlog worktree cannot be read, so that I know why I see the older backlog.

Category: ux

## What

Decide and implement when the fixed-text note (edits wait for the running job) shows if the `backlog` branch has commits develop lacks but its worktree cannot be read: show the note with a second fixed sentence saying the waiting edits cannot be shown right now, and read develop as before. Add a test for that state.

## Why

Today the note depends only on readable pending edits, so an unreadable worktree silently shows the develop backlog and hides that edits are waiting (UX review nit 1 of job 299).

## Notes

Proposed by job 299-edit-backlog-during-jobs (`factory/jobs/299-edit-backlog-during-jobs/`).
