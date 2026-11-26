# 176 Show each backlog item's factory estimate

As someone planning from the phone, I want each backlog item in ControlRoom to show how long it will likely take and how many tokens it will use, so that I can choose what to run next without opening the dashboard.

Category: feature

## What

Read the estimate the factory works out (factory #62: `job.py estimate`, or the `estimate` field it adds to `/board.json`) and show it on each open item in the backlog view as a short line, for example `~35 min · ~6M tokens · 12 jobs`. Show the range and the group it was based on (for example `similar jobs` or `all jobs`) in the item's details. Show nothing when there is no estimate (no history yet, or an older factory without the command), and never fail the backlog view because of it. Add tests for an item with an estimate, one without, and a missing or malformed estimate.

## Why

The factory can already tell from past jobs how long an item is likely to take, but it is only useful if it can be seen where the items are planned and reordered, which is the ControlRoom backlog view. Without it, picking items for a day or for the rest of the weekly budget stays guesswork.

## Notes

Added together with factory #62, which works out the estimate.
