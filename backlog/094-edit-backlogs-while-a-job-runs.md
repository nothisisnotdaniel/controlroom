# 094 Edit backlogs while a job runs

As the user, I want to edit a project's backlog in ControlRoom while a factory job runs in it, so that I don't have to wait hours for not_develop to go away.

## What

When a project's checkout is not on develop (a job branch), `POST /api/backlog/change` writes to a local `backlog` branch instead of refusing: it keeps its own git worktree of that branch outside the factory root (for example under `~/Library/Application Support/ControlRoom/worktrees/<project>`, same folder rules as the token folder), creates the branch from develop when missing, applies the change there with the same checks (version, clean files, `SafeFileWriter`), commits only the changed files and does not push. The project checkout is never touched. While `backlog` has commits develop lacks, `GET /api/backlogs` and `GET /api/story` read from that worktree, and the backlog page shows a fixed-text note that the edits wait for the running job and are merged into develop after it (factory#17 does the merge). On develop with a clean tree, edits work as today. Update the CLAUDE.md safety rules to name the worktree and branch. Full path.

## Why

Today every backlog edit is refused with not_develop while a job runs, which in auto mode is most of the time.
