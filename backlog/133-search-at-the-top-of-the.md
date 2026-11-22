# 133 Search at the top of the Backlogs

As the user, I want to search from the top of the Backlogs view instead of a Search tab, so that the bottom bar has fewer tabs and search sits next to the items it finds.

Category: ux

## What

Remove Search from the bottom tab bar (#127). The Backlogs view shows the search field at its top, above the project picker and the groups, with the same search behaviour, results and API as the Search view today; results show in place of the backlog while a query is entered, and clearing the field shows the backlog again. The old #search link opens the Backlogs with the search field focused, so bookmarks keep working. Tests cover the tab bar without Search, the field on the Backlogs, a search and clearing it, and the #search redirect.

## Why

The bottom bar has eight tabs, which is tight at phone width, and searching is mostly done to find a backlog item.
