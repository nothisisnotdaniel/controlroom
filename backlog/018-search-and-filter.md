# 018 Search and filter

As the user, I want to search all backlogs and filter by mark and project, so that I can find an item quickly in a long backlog.

## What

A search box filters items across all projects by number (`#12`, `Times#12`), title and story text, as you type. Filters for mark (open, `[?]`, `[h]`, done) and project can be combined with the search. The search runs on the server or in the page, whichever the spec finds simpler for about 200 items; story text is only read from allowed story files. Read-only. Full path.

## Why

Context alone has 100 open items. Scrolling to find one, or to check whether an idea is already there, is slow on a phone; a duplicate check before adding an item also needs it.
