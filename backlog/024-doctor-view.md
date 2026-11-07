# 024 Doctor view

As the user, I want to see the factory's health problems in the app, so that I notice stuck jobs or leftovers without running `/factory doctor`.

## What

A Health view shows what `job.py doctor` reports, computed by the server itself and read-only: jobs in an active stage for over a day, job folders not committed in the factory repo, `factory/*` branches already merged into `develop`, backlog lines with a missing or wrong story file. The server only reads files and runs git with fixed arguments; it never runs `job.py`. Each finding says what `/factory doctor` would do about it. Full path.

## Why

Problems like a stuck job or uncommitted records stay invisible until something else breaks. Showing them where I already look makes them cheap to fix early.
