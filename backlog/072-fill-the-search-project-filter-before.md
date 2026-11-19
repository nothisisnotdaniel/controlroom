# 072 Fill the search project filter before the first search

As the operator, I want the project filter in Search to list every project as soon as the view opens, so that I can pick a project before I type anything.

## What

search.js fills the project select only from the first /api/search response (AC-8 of job 255), so it holds only "All projects" until a search runs. Fill it when the view opens, for example from an empty search or an existing project list, and update the AC-8 test.

## Why

Review-ui, review-ux and review-code (job 255) noted that a tap on the select before any search shows nothing to pick, which looks broken on a phone.

## Notes

Proposed by job 255-search-and-filter (`factory/jobs/255-search-and-filter/`).
