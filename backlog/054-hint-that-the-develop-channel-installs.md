# 054 Hint that the develop channel installs unreleased builds

As a ControlRoom user choosing an update channel, I want the develop option to say it installs unreleased builds, so that I don't pick it by accident.

## What

Add a short hint to the develop channel label or note in the update box saying it installs unreleased (prerelease) builds from develop. The current texts are fixed by job 234's spec AC-12, so this changes those texts.

## Why

review-ux 2 (job 234): today nothing on screen warns that develop builds are unreleased.

## Notes

Proposed by job 234-develop-prereleases-channel (`factory/jobs/234-develop-prereleases-channel/`).
