# 160 Refuse a symlinked factory folder in the develop worktree

As the user, I want ControlRoom to refuse a factory backlog edit when the develop worktree's factory/ folder is a symlink, so that the rule that edits never leave the develop worktree is checked by ControlRoom itself and not left to git.

Category: security

## What

In src/RoomKit/FactoryBacklog.cs, before any read or write of the factory backlog in the develop worktree, check <develop>/factory with lstat (no-follow): it must be a real directory, not a symlink, otherwise the factory entry is unreadable (or the change is refused) with a fixed error, and nothing is written. Also check the develop folder itself without following a symlink where Directory.Exists is used today. Add tests for a symlinked factory/ folder (read and change refused, files unchanged) and update CLAUDE.md's factory backlog rule.

## Why

Today a symlinked factory/ only fails because git status refuses paths beyond a symlink, so the change ends as dirty. That works, but it is an accident of git, not a visible rule (security review nit in job 402).

## Notes

From job 402, review-security nit 2 (FactoryBacklog.cs:522).
