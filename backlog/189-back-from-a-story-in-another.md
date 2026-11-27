# 189 Back from a story in another backlog returns to the backlog it came from

As someone reading a backlog in ControlRoom, I want Back from a linked story in another project to bring me to the backlog I started in, so that I don't lose my place.

Category: bug

## What

Clicking a dependency or plan link to another project's story (for example a `Waits for RoomKit#12` link in the ControlRoom backlog) calls chooseBacklogProject in route(), which stores the other project as the backlog choice; Back then shows that project's backlog instead of the one the link was clicked in. Keep the choice that was active when the story was opened from a list and restore it when Back (the button or the browser's back) returns to the Backlogs view, together with the scroll position it keeps today. A story opened by a deep link or bookmark keeps today's behaviour (the story's project becomes the choice). Add a web test for the cross-project link, Back, and the picker's value.

## Why

Following a cross-project dependency is a common way to check what an item waits for, and every time Back lands in the wrong backlog, so the user has to pick the project again and find their place.
