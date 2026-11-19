# 092 Don't follow a symlinked .git in ProjectGit

As the operator, I want ControlRoom's git helpers to refuse a .git that is a symlink, so that they match the lstat checks used elsewhere and can't be pointed at another repo.

## What

ProjectGit.CurrentBranch and ProjectGit.AheadOfMain use Directory.Exists/File.Exists on <project>/.git, which follow symlinks. Switch both to an lstat-style check (FileSystemInfo.LinkTarget is null), treat a symlinked .git as no repo, and add tests for both helpers.

## Why

Review-security (job 269) noted the mismatch with the lstat checks elsewhere; changing it alters behaviour for both helpers, so it needs its own job.

## Notes

Proposed by job 269-ci-and-git-status (`factory/jobs/269-ci-and-git-status/`).
