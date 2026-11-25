# 166 Keep the claim 'checked' time current and show it after a first failed check

Category: ui

## What

The 'checked n min ago' text on claimed items is worked out once when the backlog is rendered and doesn't change after that. Update it while the page stays open (for example once a minute). When the very first check of origin fails, the item shows as out of date with no checked time; show when the check was tried, or say it hasn't succeeded yet.

## Why

A time that stays at '1 min ago' for an hour, or an out-of-date mark with no time, makes the claim marks look more current or more puzzling than they are.
