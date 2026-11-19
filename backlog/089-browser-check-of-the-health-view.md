# 089 Browser check of the Health view

As the operator, I want the Health view checked in a real browser on an iPhone-width screen, so that I know its findings are readable and its links work.

## What

Open ControlRoom at 375 px wide in light and dark mode; check the eight-link nav, each kind of Health finding (stuck job, uncommitted records, merged branch, missing story) with its /factory doctor hint, that a link to an uncommitted job opens, and that the view refreshes when a backlog or story file changes.

## Why

Reviewers of job 266 could only read the code, and its web tests check the script as text because the repo has no JS runtime.

## Notes

Proposed by job 266-doctor-view (`factory/jobs/266-doctor-view/`).
