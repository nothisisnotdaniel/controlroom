# 129 Jobs view without your tasks

As the user, I want the Jobs view to show only jobs, so that my own tasks are not listed twice and the view stays about what the factory is doing.

Category: ux

## What

The Needs you section of the Jobs view (board.js renderNeedsYou) stops listing `[h]` tasks; they stay on the Tasks tab, which already shows them with its count. A job in needs-human stays visible in the Jobs view, because no other tab shows stuck jobs: it moves into a Stuck section among the stages (or keeps a section of its own named for jobs only), still marked as needing the user and still opening the Your decision view. With no stuck job, nothing of the section shows. Tests cover a board with tasks and no stuck job (no section, no task cards), a stuck job (shown, marked) and the Tasks tab count unchanged.

## Why

The Needs you section mixes stuck jobs with the user's own tasks, which the Tasks tab already lists, so the Jobs view repeats them.

## Notes

The user asked that Needs you not be shown in the Jobs view because it has its own tab. Only the tasks have their own tab; stuck jobs do not, so they stay in the Jobs view.
