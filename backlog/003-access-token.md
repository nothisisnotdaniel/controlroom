# 003 Access token

As the user, I want every request to ControlRoom to need an access token, so that only my devices can read and change my backlogs.

## What

The server creates a random access token on first start (kept in a file next to the server's settings, mode `0600`, never in a URL or log) and every request must carry it; the web app asks for it once and keeps it on the device. A command prints it and another replaces it. Full path, high risk.

## Why

Tailscale limits who can reach the server, but any process on the Mac or a browser page could still call `127.0.0.1:8100`. A token that lives only on my devices is the gate every write feature (like editing backlogs) needs first.
