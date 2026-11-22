# 113 Place the auto stop line from the backlog view

As the user, I want to put, move and remove the auto stop line in a backlog from the web app, so that I can tell a running /factory auto where to stop from my phone.

Category: feature

## What

Show the stop line (`- [stop] Auto stops here`, the format from factory#21) in the Open group as a visible divider (`Auto stops here`). In edit mode the user can add it (after a chosen open item, or below the first one), move it (with the same drag and drop as items, within the Open group) and remove it, through new change actions on POST /api/backlog/change (`stop_add`, `stop_move`, `stop_remove`; only backlog.md, version-checked, committed as `backlog: stop line`). At most one stop line per backlog; a second one or one with other text is refused. The Next line of the plan shows when the next item is below the stop line (`Auto stops before #N`). BacklogParser and job.py read the same line.

## Why

The user wants to set a stop point for an auto run from the phone; factory#21 makes auto honour the line, this item lets the user place it.
