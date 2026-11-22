# 064 Keep stale story marks across route changes

As the operator, I want a story change seen while the story was hidden to still count, so that no change is silently dropped.

Category: bug

## What

live.js liveRouteChanged clears staleStories on every route change. Keep the marks for stories not yet re-opened, or document why clearing is safe.

## Why

Review-ux (job 249) noted the mark is dropped; it is harmless today because loadStory fetches fresh on entry, but a later cache would make it a real bug.

## Notes

Proposed by job 249-live-board-updates (`factory/jobs/249-live-board-updates/`).
