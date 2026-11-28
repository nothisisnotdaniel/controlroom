# 192 This week page from the structured weekly plan (after: factory#102)

As the factory owner, I want a This week page in ControlRoom that shows what is still to do for the weekly plan, so that I and my collaborator see at a glance what is left and who it waits on.

Category: feature

## What

Add a This week page that shows the resolved weekly plan from `job.py week --json` (factory #102): the goals with their status, then the tasks grouped by owner (you, collaborator, factory), each with its live state (done, running, waiting on a person, blocked by its after item, open) and a link to its backlog story, then the release lines with merges since the last release. Done tasks are shown dimmed at the end. Refresh it like the other live pages.

## Why

The weekly plan becomes a structured file in factory #102. ControlRoom is where the collaborator follows the factory, so the week's open work should be visible there and not only in a markdown file.

## Notes

Decided on 28 Nov 2026. Read the data from the factory, do not parse the markdown view.
