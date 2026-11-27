# 184 Close the More menu when keyboard focus leaves it

As someone using ControlRoom with a keyboard, I want the … menu in the tab bar to close when I tab out of it, so that it does not stay open over the content.

Category: ux

## What

In app.js setUpMoreMenu, close the open … (More) menu and set aria-expanded false when focus moves outside the menu and its button (focusout with relatedTarget outside both), without moving focus back to the button.

## Why

The UX review of job 475 found that tabbing out of the open menu leaves it open over the content; a focusout close is friendlier for keyboard and screen reader users.

## Notes

Proposed by job 475-more-menu-tabs.4bc0d3c44f8849c3a586d62c320880bd (`factory/jobs/475-more-menu-tabs.4bc0d3c44f8849c3a586d62c320880bd/`).
