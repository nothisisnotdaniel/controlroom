# 132 Capture at the top of the Inbox

As the user, I want to capture a new item at the top of the Inbox instead of in its own tab, so that the bottom bar has fewer tabs and new items start where I review them.

Category: ux

## What

Remove Capture from the bottom tab bar (#127). The Inbox view shows the capture form at its top, above the proposed items, with the same fields, checks, texts and API as the Capture view today; after a successful capture the Inbox refreshes and the new item appears in it when it is a [?] item. The old #capture link opens the Inbox with the form in view, so bookmarks keep working. Tests cover the tab bar without Capture, the form on the Inbox, a capture from there, and the #capture redirect.

## Why

The bottom bar has eight tabs, which is tight at phone width. Capturing and reviewing proposed items belong together.
