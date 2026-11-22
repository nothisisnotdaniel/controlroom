# 152 Windows executables for ControlRoom

As someone running a factory on a Windows PC, I want ControlRoom releases to include a Windows build that installs, starts at sign-in and updates itself, so that I can use ControlRoom without a Mac.

Category: feature

## What

Add a win-x64 build to release.yml next to osx-arm64: a self-contained single-file controlroom.exe in a .zip, listed in SHA256SUMS, for prereleases and releases alike. Make the macOS-only parts work on Windows: the launch agent (RoomKit scripts/launch-agent.sh, launchctl) gets a Windows counterpart, for example a per-user Task Scheduler task started at sign-in, and the updater (scripts/updater.sh) gets a PowerShell version that checks SHA256SUMS, swaps the exe and restarts the task. Fix data folder paths (%LOCALAPPDATA%\\ControlRoom instead of ~/Library/Application Support), file permissions on the token file (an ACL for the current user only instead of chmod 600) and any path or shell assumptions in the server. Since this lives in RoomKit, PlayRoom inherits it. Run the tests on a windows-latest runner in CI, and update README.md with Windows install and run steps.

## Why

Releases are built for Apple Silicon Macs only, so anyone on Windows cannot run ControlRoom, or later PlayRoom. The launch agent and updater are shell and launchd scripts, so a plain win-x64 publish alone would run but never start at sign-in or update itself.

## Notes

Asked for on 2026-11-22. High risk: installer, updater, token file permissions and a new CI runner.
