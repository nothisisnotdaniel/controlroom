# 025 CI and git status

As the user, I want to see each project's last CI result and how far `develop` is ahead of `main`, so that I can tell at a glance whether a project is healthy and has unreleased work.

## What

Each project shows the CI result the factory last recorded (the `ci` field in its newest job's `state.json`, with link and time) and the number of commits on `develop` not on `main`, read with git and fixed arguments. The server never calls `gh` or the network. Share the git reading with #13 (Releases overview) if that is built first. Read-only. Full path.

## Why

A red CI run on `develop` or a pile of unreleased commits is easy to miss. Both are already known on the Mac; putting them next to the backlog makes them visible without opening GitHub.
