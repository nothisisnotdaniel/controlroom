# 188 Show the severity of security backlog items

As the factory owner, I want ControlRoom to show each security item's severity (low, medium, high, critical) next to its title, so that I can see at a glance which holes to approve first.

Category: ui

## What

Read the `Severity: <level>` line directly below `Category: security` in each backlog story and show it as a small coloured pill in the backlog list and on the item's detail view (for example grey low, amber medium, red high, dark red critical). Items without the line show nothing. No editing in this item.

## Why

Factory #71 (job 481) added a severity level to security stories, but ControlRoom does not show it, so the user still has to open each story to see how serious it is.

## Notes

Follow-up of factory job 481, chosen by the user on 2026-11-27. The format is documented in the factory job.py docstring (`job.py severity`).
