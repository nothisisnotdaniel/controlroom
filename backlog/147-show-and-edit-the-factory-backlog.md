# 147 Show and edit the factory backlog

As the factory owner, I want the factory own backlog in ControlRoom next to the projects, so that I can read, approve and reorder factory changes from my phone too.

Category: feature

## What

List factory as a backlog section in ControlRoom (ProjectFinder skips it today). Since factory#30 the factory backlog lives in the factory's develop worktree, not in the root: `$FACTORY_DEVELOP_DIR` when set, else `<root>-factory-develop/` (here `/Volumes/External/Claude-factory-develop/`), a git worktree of the factory repo on branch `develop`. Read factory/backlog.md and factory/backlog/ there, and only when git lists that folder as a worktree of the root's repo on `develop`; otherwise show the factory section as unreadable with the reason. Edits (marks, order, new items, drafts, stop line) commit on `develop` in that worktree, or, while a factory job is in an active stage, on the factory repo's local `backlog` branch (in the worktree that has it checked out, else a temporary worktree outside the root, created from develop when missing), exactly as `job.py add --project factory [--backlog-branch]` does. Never write the factory root or `main`: the root's own factory/backlog.md lags until a release. Don't create the develop worktree; if it is missing, say to run any `job.py` backlog command once. Factory jobs and (after: factory#N) dependencies keep working as now. Tests use a temporary factory root with a develop worktree of its own.

## Why

The factory backlog can only be read and edited on the Mac today, so approving a factory follow-up needs the terminal. Writing to main directly would change the live factory; factory#30 (merged 25 Nov) added the develop worktree and backlog branch this writes to.

## Notes

Asked for on 2026-11-22. ControlRoom leaves out the factory folder on purpose (ProjectFinder.cs); it has nothing to do with the private repo. Story updated on 2026-11-25 for the develop worktree from factory#30.
