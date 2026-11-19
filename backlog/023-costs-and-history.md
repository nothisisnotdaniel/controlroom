# 023 Costs and history

As the user, I want to see tokens per job, project and mode and a list of past jobs with their outcome, so that I know where my usage goes and how jobs have gone.

## What

A History view lists finished jobs newest first with project, title, mode, path, rounds, outcome (merged, escalated, reverted) and tokens, filterable by project. Totals per project and per mode for the last 7 and 30 days sit on top. All numbers come from `state.json`; jobs without token data count as unknown, not zero. Read-only. Full path.

## Why

With weekly limits, knowing which modes and projects cost what helps choose modes. The factory's own backlog has the same wish for the old dashboard (factory#5), and ControlRoom is replacing it.

## Notes

Merged on 2026-11-15: factory #5 (Costs on the dashboard). Add a simple chart of tokens over time, so the modes can be compared.
