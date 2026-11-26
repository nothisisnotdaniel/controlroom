# 171 One project filter on the Backlog page, with factory

As the user, I want one project filter on the Backlog page that also lists factory, so that I don't pick a project twice and can search the factory backlog.

Category: ux

## What

The Backlog page has two project filters: the Project dropdown at the top (from /api/backlogs, which lists factory) and the search box's own project filter (search.js, filled from /api/search, which uses only ProjectFinder and so never lists or searches factory). Make the top Project dropdown the only filter: add an 'All projects' choice that shows every backlog, remove the search box's project select, and have search use the dropdown's choice (all projects, or just the chosen one). Have /api/search also read the factory backlog from the develop worktree, the same way /api/backlogs does (FactoryBacklog / BacklogSource.ForFactory), so factory items show up in results.

## Why

Two filters for one choice are confusing, and the search one is missing factory, so factory backlog items can't be searched at all.

## Notes

Keep the stored choice (controlroom-backlog-project) working; an unknown stored name falls back as today. Old #search bookmarks still open the Backlogs with the search field focused.

## Folded in from #72 (backlog audit, 2026-11-26)

Fill the search project filter before the first search: search.js fills the project select only from the first /api/search response (AC-8 of job 255), so it holds only "All projects" until a search runs. Fill it when the view opens, for example from an empty search or an existing project list, and update the AC-8 test.
