# 110 Category chip on backlog items and stories

As the user, I want a chip with each item's category (feature, bug, security, ux, ui, tidy-up, test, ci, docs) on the backlog and in the story view, so that I can see what kind of work it is.

## What

Read the `Category: <name>` line of each story (the format from factory#20) and show it as a chip on each backlog line and in the story view; an item without a valid category shows no chip. Each category has its own colour that works in light and dark mode, with the name as text. In the story view, a Category select (edit mode) changes it through a new change action `category` on POST /api/backlog/change (only the story file, committed as `backlog: category #N`). The search view can filter by category. Reading the category must not slow the backlog view much (story files are read with StoryReader, capped).

## Why

Without a category the backlog is a flat list; the chip shows at a glance which items are bugs, security work or features.
