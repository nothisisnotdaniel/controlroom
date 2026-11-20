# 074 Notice when a live search refresh fails

As the operator, I want to see when the search results could not be refreshed, so that I know they may be stale.

Category: ux

## What

search.js runSearch keeps the old results without any notice when a live re-run fails (for example a 400 after the chosen project folder disappeared). Show a short note that the results may be out of date, cleared on the next good run.

## Why

Review-code and review-ux (job 255) noted the quiet failure; it is retried, but a long-open view could show stale results with no hint.

## Notes

Proposed by job 255-search-and-filter (`factory/jobs/255-search-and-filter/`).
