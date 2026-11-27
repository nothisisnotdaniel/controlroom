# 190 Offer Drop on approved backlog items

As the backlog owner, I want a Drop button on approved items in ControlRoom's edit mode, so that I can remove a story I no longer want.

Category: feature

## What

Raise the RoomKit pin in both csproj files to the release that has RoomKit#15 (Drop on `- [ ]` items) and adjust ControlRoom's tests and the shell cache version as that bump needs. Check in the app that an approved item shows Drop in edit mode, asks for confirmation, and is removed with its story file in one commit, and that an item another line waits for is refused with RoomKit's message. Update the backlog editing part of CLAUDE.md.

## Why

RoomKit holds the drop logic; ControlRoom only gets it with the pin bump, so without this item the new Drop never reaches the app.
