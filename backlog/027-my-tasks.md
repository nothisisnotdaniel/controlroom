# 027 My tasks

As the user, I want one list of every manual task across all projects, with what to do and a way to tick it off, so that I can work through them without keeping a separate file.

## What

A Tasks view lists every `[h]` item of every project, split into Ready now (all `(after: …)` items done, the same rule as #19) and Waiting (each with the items it waits for, as links, including other projects' items like `ReviewPack#1`). Each item shows its project, `#N Title` and its story one tap away (the steps to do). A Done button turns `[h]` into `[x]` using the write path of #4, with its checks and one commit per change, and asks for confirmation first. The app shows the count of ready tasks on the tab. Full path, high risk (writes).

## Why

Manual tasks (release checks on a real machine, signing setup, shop and licence steps) are spread over four backlogs and are easy to forget. Today they live in a hand-made manual-tasks.md that goes stale; one list in ControlRoom with the steps at hand and a Done button keeps them current.

## Notes

Added on 2026-11-15 at the user's request, to replace the hand-made manual-tasks.md. Shares the Ready/Waiting rule with #19 and the write path with #4.
