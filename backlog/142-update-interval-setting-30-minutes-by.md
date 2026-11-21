# 142 Update interval setting, 30 minutes by default

As the user, I want the updater to check every 30 minutes by default and to set that interval in ControlRoom, so that new releases reach the Mac soon while we work a lot, and less often when we do not.

Category: feature

## What

A setting `updateInterval` in the settings file next to `autoUpdate` and `channel` (0600, same safety rules), with the fixed choices 30 min, 1 h, 2 h, 6 h and 12 h; default 30 min, and any unknown or missing value falls back to 30 min. ControlRoom shows it next to the auto-update switch and saves it through the existing settings API. The updater uses it: the updater LaunchAgent runs on a short fixed interval and each run ends at once (no download, no log line beyond a short skip) when less than the chosen interval has passed since the last real check, or the plist interval is rewritten when the setting changes; the spec picks one and says why, and a change must take effect without a reinstall. The check a minute after login and the 15-minute retry while a factory job runs stay as they are. Tests cover the default, each choice, an unknown value, the settings API and the updater honouring the interval with a fake clock.

## Why

The updater checks every 6 hours, so after a merge the new release can take up to 6 hours to reach the Mac (on 2026-11-21 v0.1.1-develop.43 waited until a manual run). While the factory merges several jobs a day that is too slow; when little work happens, frequent checks are not needed.
