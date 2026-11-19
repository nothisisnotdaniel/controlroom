# 093 Browser check of the Backlogs status line

As the operator, I want the CI result and develop/main count on Backlogs checked in a real browser on an iPhone-width screen, so that I know they read well and the CI link works.

## What

Open ControlRoom at 375 px wide in light and dark mode; check each project's status line (CI passed, failed, running, unknown; ahead counts of 0 and more), that the CI link opens the right run, and that the line's late appearance doesn't jump the layout badly.

## Why

Reviewers of job 269 could only read the code, and its web tests check the script as text because the repo has no JS runtime.

## Notes

Proposed by job 269-ci-and-git-status (`factory/jobs/269-ci-and-git-status/`).
