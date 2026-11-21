# 138 Active and blocked job counts on the Jobs tab

As the user, I want the Jobs tab icon to show how many jobs are running and how many are waiting for me, so that I see at a glance from any view whether the factory is busy or stuck.

Category: ux

## What

The Jobs tab icon in the bottom bar shows two small badges, like the Backlogs tab's (#135): the number of active jobs (stage `spike`, `spec`, `build`, `review` or `approve`) and the number of blocked jobs (stage `needs-human`), across all projects. Each badge is hidden at 0 or when the jobs can't be loaded, has an accessible name (for example 1 running, 2 blocked), and has its own colour pair in light and dark mode so the two can be told apart, the blocked one standing out. The counts come from the same jobs data the board uses and update with each live check, so they change without a reload when a job starts, finishes or gets stuck. Tests cover both badges and their counts (including 0 and a failed load) and a live update.

## Why

Today I have to open the Jobs board to see whether a job is running or is stuck waiting for me. A stuck job can sit unnoticed while I work in the backlog, and on the phone I want to know at a glance whether the factory needs me.

## Notes

- 2026-11-21: Draft from ControlRoom
- 2026-11-21: Written out: two badges on the Jobs tab, matching the Backlogs badges of #135.
