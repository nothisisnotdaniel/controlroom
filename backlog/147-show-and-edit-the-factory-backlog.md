# 147 Show and edit the factory backlog

As the factory owner, I want the factory own backlog in ControlRoom next to the projects, so that I can read, approve and reorder factory changes from my phone too.

Category: feature

## What

List factory as a backlog section in ControlRoom, read from factory/backlog.md and factory/backlog/ in the factory root, which ProjectFinder skips today. Edits (marks, order, new items, drafts, stop line) commit on the factory repo develop branch, or on its backlog branch while a factory job is running, exactly as for projects. Factory jobs and (after: factory#N) dependencies keep working as now. Tests use a temporary factory root with a factory folder of its own.

## Why

The factory backlog can only be read and edited on the Mac today, so approving a factory follow-up needs the terminal. Writing to main directly would change the live factory, so this waits for the factory develop and backlog branches.

## Notes

Asked for on 2026-11-22. ControlRoom leaves out the factory folder on purpose (ProjectFinder.cs); it has nothing to do with the private repo.
