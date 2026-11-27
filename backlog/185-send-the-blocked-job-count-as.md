# 185 Send the blocked job count as the badge on every server push

As someone who runs ControlRoom from the iPhone home screen, I want every push to carry the blocked job count as the app badge, so that the icon shows jobs that need me even while the app is closed.

Category: feature

## What

Once RoomKit has a public badge API for server pushes (RoomKit#12) and is released, bump the RoomKit PackageReference and set the needs-human job count as the badge on every push ControlRoom sends. Update the App badge section of ControlRoom CLAUDE.md.

## Why

Job 476 could only set the badge while the app is open, because RoomKit 0.2.0 has no public way to add badge to a push. Without this the badge goes stale while the app is closed.

## Notes

Proposed by job 476-blocked-jobs-badge.489646d30a9d4d00b22cb5f428b4004e (`factory/jobs/476-blocked-jobs-badge.489646d30a9d4d00b22cb5f428b4004e/`).
