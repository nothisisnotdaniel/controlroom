# 125 Status chip in the story view

As the user, I want the story view to show whether the item is done, unapproved or open, so that I know where it stands without going back to the backlog.

Category: ui

## What

In the story view (#story/<project>/<n>), show a status chip in the story header taken from the item's backlog line in /api/backlogs: `[x]` is Done, `[?]` is Unapproved, `[ ]` is Open, and `[h]` is Your task (so every item shows a status). It uses the same chip style as the category chip and stays readable in dark mode and at phone width. After Approve in the story view (#105) the chip changes from Unapproved to Open without a reload. An item whose line can no longer be found shows no chip. Tests cover each status and the change after Approve.

## Why

Today the story view shows the story but not its state, so a reader can't tell whether the item is already done, still waiting for approval or ready to run without going back to the backlog list.
