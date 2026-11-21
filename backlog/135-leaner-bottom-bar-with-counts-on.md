# 135 Leaner bottom bar with counts on Backlogs

As the user, I want the bottom bar to hold only Jobs, Backlogs, Capture, History and Health, with the counts of my tasks and of unapproved items on the Backlogs tab, so that the bar is roomy and every item is handled from the backlog.

Category: ux

## What

Remove the Inbox and Tasks tabs from the bottom tab bar (#127). The Backlogs tab icon shows two small badges: the number of `[h]` items and the number of `[?]` items across all projects, each hidden at 0, each with an accessible name (for example 2 tasks, 5 unapproved), styled so they can be told apart in light and dark mode. Capture comes back as a tab button (icon and tiny label) that opens the capture form in its own view as before #132, and the form leaves the top of the Inbox. The old #inbox and #tasks links open the Backlogs, so bookmarks keep working. Tests cover the tab bar's tabs, both badges and their counts (including 0), the Capture tab and form, and the redirects.

## Why

Approve, Drop and Done are all in the story view now, so the Inbox and Tasks views repeat what the backlog shows, and the bar is tight at phone width. Capture is used often enough to deserve its own button.

## Notes

The user chose Capture as a bottom-bar button again (it was moved to the Inbox in #132) and the counts as badges on the Backlogs tab icon.
