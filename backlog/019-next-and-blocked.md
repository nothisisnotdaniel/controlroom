# 019 Next and blocked

As the user, I want to see per project which item the factory runs next and which items are waiting on others, so that I know what a run will do and what holds things up.

## What

Each project shows its next item (the first open item whose `(after: …)` items are all done, the same rule as `job.py next`) and a Waiting group listing each blocked item with the items it waits for, as links, including other projects' items like `Times#5`. A dependency on a number that doesn't exist is shown as a fixed warning. Read-only. Full path.

## Why

Order and dependencies decide what `/factory auto` does next, but they are hard to see in a flat list. Seeing the next item and the blockers makes it easy to reorder or unblock before starting a run.
