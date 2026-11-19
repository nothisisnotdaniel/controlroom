# 061 Check that the job page refreshes in place

As the user, I want to confirm on a real browser that an open job page updates when its job changes, so that I can trust what it shows while a job runs.

## What

Open a running job's page in a browser (Mac and iPhone), wait for its board entry to change, and check that the state section shows the new values while the selected tab and scroll position stay (job 248 AC-7). Report anything off as a new item.

## Why

review-ui 2 / review-ux 2 (job 248): AC-7 is a manual check that no agent could run, so it is not verified yet.

## Notes

Proposed by job 248-job-detail-gaps (`factory/jobs/248-job-detail-gaps/`).
