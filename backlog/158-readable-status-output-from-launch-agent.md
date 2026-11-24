# 158 Readable status output from launch-agent.ps1

As a Windows user, I want the agent status in plain words, so that I can tell at a glance whether ControlRoom runs.

Category: ux

## What

`Get-RoomStatus` in `scripts/launch-agent.ps1` prints `Installed=True Running=False`. Print a short sentence instead, and when it isn't running, a hint to check Task Scheduler.

## Why

The output reads like data, not like a message for a person. Found in the review of PR #3.

## Notes

From PR #3 review, nit 5.
