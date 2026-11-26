# 181 Show the number of blocked jobs as the app badge on the iPhone

As someone who runs ControlRoom from the iPhone home screen, I want the app icon to show how many jobs need me, so that I notice blocked jobs at a glance.

Category: feature

## What

Add the blocked (needs-human) job count as the `badge` field to every push the server sends, using RoomKit 0.2.0 payload support. In the page, set or clear the badge with the RoomKit helper whenever job counts load (through the RoomKit host hook) so it drops when jobs are unblocked and the app is opened. Bump the RoomKit PackageReference to 0.2.0 if #178 has not already. Document it in ControlRoom CLAUDE.md, including that iOS needs notification permission and that the badge only updates on a push or when the app is open.

## Why

A badge is the quickest signal that the factory is waiting on me, without opening the app or the notification center.
