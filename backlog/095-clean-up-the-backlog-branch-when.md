# 095 Clean up the backlog branch when its worktree cannot be made

As the user, I want ControlRoom to leave no empty backlog branch behind when it cannot create its backlog worktree, so that the factory and doctor never see a backlog branch that holds nothing.

Category: bug

## What

When `git worktree add` for the backlog worktree fails after the `backlog` branch was created, delete that branch again (only if it still points at develop and has no commits of its own), or create the branch only once the worktree exists, keeping the other Ensure steps in their order. Add a test that makes `worktree add` fail and checks no `backlog` ref is left.

## Why

Today a failed `worktree add` leaves an empty `backlog` ref. It is harmless, but `job.py doctor` and later attempts have to reason about a branch that should not exist (security review nit 1 of job 299).

## Notes

Proposed by job 299-edit-backlog-during-jobs (`factory/jobs/299-edit-backlog-during-jobs/`).
