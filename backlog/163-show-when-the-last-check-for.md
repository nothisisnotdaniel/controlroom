# 163 Show when the updater last checked for updates

As the user of ControlRoom, I want to see when the last check for updates ran, so that I can tell the updater is working and how current the "no update" state is.

Category: feature

## What

Under the `Check for updates` interval select at the foot of Jobs (`renderUpdateInterval` in `src/ControlRoom.Server/wwwroot/board.js`, inside `#auto-update`), show one line of muted text, `Last checked <local date and time>`. If no check is recorded, show `Not checked yet` instead. The line appears only where the interval select appears, so it is never shown on `dev` or with a refused `settings.json`. It is filled with `textContent` and uses the device's time zone. Its fixed texts go in `BoardTexts`.

The time comes from the updater's own stamp, `<support>/last-check`. `scripts/updater.sh` and `scripts/updater.ps1` write it on every due run as Unix seconds plus a line break. The server reads it on each `GET /api/update` through `PrivateFolder`, with the same rules as the other update files: no-follow, a regular `0600` file owned by the user, at most 32 bytes, exactly digits plus at most one line break, and not in the future by the `TimeProvider`. It never writes or deletes the file. `GET /api/update` (`UpdateEndpoint`, `UpdateResponse`) gains `lastCheck`: the time as UTC `YYYY-MM-DDTHH:MM:SSZ`, or `null` when the stamp is missing, refused or invalid. A refused or invalid stamp never turns the response into an error. The CLAUDE.md lines for `GET /api/update` and the update files, plus `docs/roomkit.md` if it describes the response, say so.

Tests check four things:
- `lastCheck` for a valid stamp, `null` for a missing, symlinked, loose-mode, oversized, non-digit or future stamp, and the rest of the response unchanged in each case.
- That the server never creates `last-check`.
- That the web app shows `Last checked …` or `Not checked yet` under the interval select, and nothing on `dev`.
- That `dotnet build` stays at 0 warnings.

## Why

The interval select says how often the updater checks, but nothing shows whether it actually ran. When no update arrives, the user can't tell whether there is no newer release or the updater stopped (unloaded agent, refused files, a job holding it back). The updater already records each check in `last-check`, so showing that time is a small, read-only change that answers the question from the phone.

## Notes

- 2026-11-25: Draft from ControlRoom
- 2026-11-25: Written by the factory
- 2026-11-25: The capture doesn't say which check. I took the updater's check (the `last-check` stamp written by `updater.sh`/`updater.ps1`, the one the `Check for updates` interval controls), not the server's in-memory 6 h `UpdateChecker` run that feeds the banner.
