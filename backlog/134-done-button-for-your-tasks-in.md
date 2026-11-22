# 134 Done button for your tasks in the story view

As the user, I want a Done button in the story view of my own [h] tasks, so that I can tick a task off right after reading it.

Category: ux

## What

In the story view of an item whose backlog line is `[h]`, show a Done button in the story bar, next to Edit story and Add note. It changes the line from `[h]` to `[x]` through the same backlog change API as approve and drop (POST /api/backlog/change with the backlog version and line, through sendChange and its fixed error texts; add a `done` change for `[h]` lines if none exists, refusing it for any other mark). After success the view goes back to where the user came from, like Approve and Drop (#128); a failure stays and shows the error. Items that are not `[h]` show no Done button. Tests cover the button on `[h]` only, the change, the refusal for other marks, and going back.

## Why

Your tasks can only be ticked off by editing backlog.md by hand today. Reading the task and ticking it should happen in one place.
