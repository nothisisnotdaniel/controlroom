# 005 Start at login

As the user, I want the ControlRoom server to start at login and restart if it stops, so that the app is reachable whenever the Mac is on.

## What

A script that installs and removes a macOS LaunchAgent so the server starts at login and restarts if it stops, with its log in a known place; documented in CLAUDE.md and the README.

## Why

If the server has to be started by hand, it won't be running when I reach for my phone. A LaunchAgent with a known log location makes it dependable and easy to debug.
