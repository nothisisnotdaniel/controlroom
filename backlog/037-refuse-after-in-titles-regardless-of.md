# 037 Refuse (after: in titles regardless of case and spacing

As the ControlRoom user, I want a backlog title containing a variant of `(after:` to be refused, so that a title I type can never be read by job.py as a dependency marker.

## What

In `BacklogEditor` title validation for add and title edits, refuse any `(` followed by optional whitespace and `after:` in any case (for example `(After:`, `( after:`), matching how `factory/tools/job.py` parses the `(after: …)` suffix. Return the existing `invalid` error code. Add tests for each variant.

## Why

The security review of job 185 found that only the exact lowercase `(after:` is refused, so a variant may slip through. If job.py parses it differently, an item could silently start waiting on another item.

## Notes

Proposed by job 185-edit-backlogs (review-security round 1, nit 2).

Proposed by job 185-edit-backlogs (`factory/jobs/185-edit-backlogs/`).
