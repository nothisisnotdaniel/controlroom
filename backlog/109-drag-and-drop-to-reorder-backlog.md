# 109 Drag and drop to reorder backlog items

As the user, I want to drag a backlog item to a new place by a handle, so that reordering takes one gesture instead of many taps on Up and Down.

Category: feature

## What

In edit mode give each movable line (open and [?] items) a drag handle (≡). Dragging by the handle uses pointer events (touch and mouse, no library, no HTML5 drag and drop because it is unreliable on iOS), shows where the item will land and auto-scrolls near the edges; the rest of the row still scrolls the page. Drops are only allowed within the item's own group. A drop sends one new change action `place` (target: the line it lands before or after, plus the version) to POST /api/backlog/change, which moves the line so that only lines of other groups and prose stay where they are, commits it as `backlog: move #N`, and refuses a stale version as changed. Remove the Up and Down buttons and actions (Top stays). Escape or dropping outside the list cancels.

## Why

Up and Down swap with the nearest line of any kind, including hidden done and [?] lines, so a tap often seems to do nothing and moving an item past a few others takes many taps. The user chose drag and drop without the buttons.

## Notes

Without Up/Down only Top is left for keyboard and VoiceOver users; the spec should say how a keyboard user can still move an item (for example arrow keys on the focused handle).
