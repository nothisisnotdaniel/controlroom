# 131 Lifetime totals in History

As the user, I want the History view to show totals for the whole lifetime of the factory, so that I see how many jobs and tokens it has used overall, not only in the last 30 days.

Category: feature

## What

Next to the Last 7 days and Last 30 days totals, History shows an All time block: jobs and tokens per project and per mode over every finished job /api/history knows, with the same table layout and the same unknown rule (a job without token data counts as unknown, never 0). The project picker filters it like the other blocks. /api/history returns the lifetime totals with the others; old jobs without a finish date still count. Tests cover the lifetime totals, the unknown count and the project filter.

## Why

History only looks back 30 days, so the factory's total use and how it splits across projects and modes can't be seen anywhere.
