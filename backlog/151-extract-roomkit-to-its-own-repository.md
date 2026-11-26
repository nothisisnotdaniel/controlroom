# 151 Extract RoomKit to its own repository

As the owner of ControlRoom and PlayRoom, I want RoomKit in its own repository with its own releases once sharing it through ControlRoom gets in the way, so that each app picks up RoomKit fixes without waiting for a ControlRoom release.

Category: tidy-up

## What

Move src/RoomKit/, its tests and docs/roomkit.md into a new RoomKit repo with its own develop/main, release workflow and SemVer, publishing RoomKit.X.Y.Z.nupkg on each release. ControlRoom switches from the project reference to a package reference. PlayRoom changes only its package source. Pick the feed then: keep the local folder feed plus a release download in CI, or GitHub Packages if more than one app restores it in CI. Register RoomKit as a factory project with its own CLAUDE.md and backlog.

## Why

Today RoomKit is packed from ControlRoom releases, which is cheapest while ControlRoom is its only real user. Do this only when one of these triggers holds: PlayRoom needs RoomKit changes often and waiting for ControlRoom releases slows it down; a third app wants RoomKit; or RoomKit needs its own versions, for example a breaking change in the plug-in interface while ControlRoom itself stays the same. Until then a separate repo turns every shared fix into two jobs and a release.

## Notes

Discussed on 2026-11-22 after #146 merged. Leave unapproved until a trigger holds.
Decided on 2026-11-23: do it now, because a collaborator builds PlayRoom and it must not wait for ControlRoom releases. Feed: GitHub Packages (both ControlRoom and PlayRoom restore RoomKit in CI). The RoomKit GitHub repository is created by the owner before the job starts.
