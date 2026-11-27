# 187 Pin RoomKit by content hash with a NuGet lock file

As the ControlRoom owner, I want restored packages pinned by content hash, so that a swapped RoomKit package on GitHub Packages fails the restore instead of being built in.

Category: security

## What

Turn on RestorePackagesWithLockFile for ControlRoom.Server and ControlRoom.Server.Tests, commit the generated packages.lock.json files, and make CI restore with --locked-mode. Document in CLAUDE.md how to update the lock file on a RoomKit bump.

## Why

Raised by review-security in job 477: the project pins only the RoomKit version, and the release SHA256SUMS comes from the same release as the package, so it catches corruption but not a tampered release. Without a lock file a replaced package would go unnoticed.

## Notes

Proposed by job 477-updater-last-check.fb9bc9de67a24b2085825fdf605fabb5 (`factory/jobs/477-updater-last-check.fb9bc9de67a24b2085825fdf605fabb5/`).
