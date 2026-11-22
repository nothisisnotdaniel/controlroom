# 150 Check job ids and folders from IJobFormat

As a ControlRoom maintainer, I want RoomKit to check the job ids and folders that an IJobFormat returns, so that a faulty or hostile job format cannot point RoomKit outside the jobs folder.

Category: security

## What

In RoomKit, enforce IdPattern on every id from IJobFormat.JobFolders and ReadAll, and check that each folder lies inside the configured JobsFolderPath (no symlinks or .. escapes). Drop entries that fail and log them, with tests for each case.

## Why

review-security 4 in job 386: today RoomKit trusts the app's job format. That is fine for ControlRoom, but PlayRoom and later apps plug in their own, and enforcing it changes behaviour, so it needs its own job.

## Notes

Proposed by job 386-roomkit-shared-core (`factory/jobs/386-roomkit-shared-core/`).
