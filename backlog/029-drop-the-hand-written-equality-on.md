# 029 Drop the hand-written equality on BacklogEntry

As the ControlRoom developer, I want BacklogEntry to use record equality, so that adding a field can't silently be left out of a hand-written Equals.

Category: tidy-up

## What

Give `BacklogEntry.After` a value-equal list type (for example an `EquatableArray` or `ImmutableArray` wrapper with value equality) and remove the hand-written `Equals`/`GetHashCode`. Keep every existing test passing.

## Why

The code review noted that the hand-written Equals and GetHashCode must be kept in step with the record's fields by hand, which is easy to get wrong later.

## Notes

Proposed by job 181-numbered-backlog-stories (review-code round 1 and 2, nit 1).

Proposed by job 181-numbered-backlog-stories (`factory/jobs/181-numbered-backlog-stories/`).
