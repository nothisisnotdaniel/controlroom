# 177 Pin the RoomKit package content with a lock file and locked-mode restore

As the ControlRoom owner, I want restores to check a lock file in CI, so that a changed RoomKit package with the same version can never slip into a build.

Category: security

## What

Enable NuGet lock files (RestorePackagesWithLockFile) for the ControlRoom projects, commit the generated packages.lock.json files, and make every CI restore in release.yml and controlroom-400-tests.yml run in locked mode (--locked-mode or RestoreLockedMode=true in CI). Document in CLAUDE.md how to update the lock files when the RoomKit pin changes.

## Why

ControlRoom pins RoomKit 0.1.0 by version only. Without a content hash, a package republished under the same version, or a tampered feed, would be restored without notice. Raised by the security review of job 429.

## Notes

Proposed by job 429-extract-roomkit-repo (review-security nit 1).
