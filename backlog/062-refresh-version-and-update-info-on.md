# 062 Refresh version and update info on live changes

As the operator, I want the version and update notices to refresh while the board stays open, so that a new release shows up without reloading the page.

## What

loadVersion and loadUpdate in board.js now run only when the board is fetched again, and each is asked only once per page load. Decide whether they should also run on a slower timer or on a live change, and make them do so.

## Why

Review-code (job 249) noted that the version and update checks no longer run on every 5 s poll. A long-open board may show a stale version or miss an update notice.

## Notes

Proposed by job 249-live-board-updates (`factory/jobs/249-live-board-updates/`).
