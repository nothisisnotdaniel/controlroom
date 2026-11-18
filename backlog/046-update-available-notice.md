# 046 Update available notice

As the user, I want ControlRoom to tell me when a newer release is on GitHub, so that I don't keep running an old version without knowing.

## What

The server checks the latest GitHub Release of the ControlRoom repo (`api.github.com`, https, port 443, no redirects, 10 s timeout, unauthenticated) once at start and then every 6 hours on `TimeProvider`, and compares it with its own version by SemVer (a `dev` build never reports an update). This is the only new outbound host; the CLAUDE.md outbound rule is changed to name it, and only the tag name and release URL are kept (nothing from the release text is shown as HTML). The board shows a fixed-text banner with the current and the new version and the command to update. A new push kind `update` (payload: the version only) is sent once per new version, with its own switch in the Notifications section. Full path.

## Why

The phone is where I look, and nothing tells me a release has happened. Without a notice I would run old versions for weeks.
