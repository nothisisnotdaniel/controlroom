# 126 Backlog position in the story view

As the user, I want the story view to show the item's position in the backlog, so that I see how soon it will run without going back to the list.

Category: ui

## What

In the story view (#story/<project>/<n>), show the item's position next to the status chip (#125): the same position number the backlog list shows in front of the item (#108), taken from /api/backlogs, so the two always agree. It updates when the backlog changes (a drag, an approve, a new item) the next time the view loads its data, without a reload. An item whose line can no longer be found, or that the list shows without a number, shows no position. Tests cover a numbered item, an item after a reorder, and an item with no position.

## Why

The position is the order jobs take items in. Today the story view hides it, so judging how soon something runs means going back to the backlog list and finding the row.
