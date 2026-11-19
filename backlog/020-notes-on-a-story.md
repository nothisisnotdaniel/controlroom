# 020 Notes on a story

As the user, I want to add a note to a story from the phone, so that my decisions and extra context reach the agent that builds it.

## What

A story page has an Add note action. The note is appended to the story's `## Notes` section (created if missing) with the date, as plain text, and committed on `develop` with only that file, with the same checks as #4 (develop, clean file, version check). The spec updates the write rules in CLAUDE.md to allow story files. Notes are limited in length and shown as text. Full path, high risk (writes).

## Why

While reading a story I often know something it doesn't say: a constraint, a preference, a decision. The spec writer reads the story first, so a note there is the cheapest way to steer a job without a chat.
