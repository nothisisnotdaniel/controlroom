# 148 Test that a file in both wwwroot folders fails the build

As a ControlRoom maintainer, I want a test that a static file present in both RoomKit and ControlRoom wwwroot breaks the build, so that a duplicate web asset can never silently shadow the shared one.

Category: test

## What

Add a test that copies the repo, places the same path in src/RoomKit/wwwroot and src/ControlRoom.Server/wwwroot, runs dotnet build and expects it to fail with the duplicate static web asset error. It needs a RepoCopy build that expects failure and one full extra build.

## Why

review-code 2 in job 386: the conflict was only checked by hand in round 1, so a future SDK or project change could let duplicates through unnoticed.

## Notes

Proposed by job 386-roomkit-shared-core (`factory/jobs/386-roomkit-shared-core/`).
