# 179 Move History and Health behind a … menu in the tab bar

As someone who uses ControlRoom on the iPhone, I want the rarely used History and Health sections behind a … item in the tab bar, so that the bar keeps only what I use daily and has room for a Notifications tab.

Category: ux

## What

Change the bottom tab bar in wwwroot/index.html to Jobs, Backlogs, Capture and a … (More) item. Tapping … opens a small menu (a popover or sheet above the bar) listing History and Health; choosing one shows that view as today, and the … item is marked current (aria-current) while History or Health is shown. The menu closes on choice, on a tap outside and on Escape, and is reachable by keyboard and screen reader (button with aria-expanded, menu items as links). Deep links #history and #health keep working. Update app.js routing and the tab-bar section of ControlRoom CLAUDE.md. If the styles need more than ControlRoom can add on top of RoomKit app.css, keep them in ControlRoom and note any RoomKit follow-up instead of editing RoomKit.

## Why

History and Health are rarely used, and five tabs leave no room for a Notifications tab on a phone screen.

## Notes

The Notifications tab itself comes with the next item; this one only frees the slot.
