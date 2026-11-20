# 086 Add the empty 30-day text to the History spec

As the operator, I want the History view's message for an empty 30-day window to be part of its written rules, so that a later change doesn't drop it by accident.

Category: docs

## What

history.js shows 'No finished jobs in the last 30 days.' when the window is empty, but AC-8 of job 264 does not list it. Add it to the fixed texts in the History test and docs.

## Why

Review-code, review-security and review-ux (job 264) all noted the extra text; it is harmless but untested as a fixed string.

## Notes

Proposed by job 264-costs-and-history (`factory/jobs/264-costs-and-history/`).
