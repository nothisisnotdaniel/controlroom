# 141 Loading text for the releases block

As the user, I want the releases block to say it is loading, so that an empty space does not look like a missing feature.

Category: ux

## What

While the first /api/releases answer is pending, the block shows a short loading line instead of staying hidden; a failure still shows Releases unavailable.

## Why

Review-ux of job 375: the block stays hidden while the first answer is pending.

## Notes

Proposed by job 375-releases-overview (`factory/jobs/375-releases-overview/`).
