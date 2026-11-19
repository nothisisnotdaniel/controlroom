# 091 Tell the user the CI link opens a new tab

As the operator, I want the CI link on a Backlogs project to say it opens a new tab, so that I'm not surprised when the browser switches tabs.

## What

status.js renders the CI run link with target=_blank and the fixed link text from job 269. Add visually hidden text such as '(opens in a new tab)' or an aria hint, and update the test that pins the link text.

## Why

Review-ux (job 269) noted that nothing announces the new tab; the spec fixed the link text, so it was left for a later job.

## Notes

Proposed by job 269-ci-and-git-status (`factory/jobs/269-ci-and-git-status/`).
