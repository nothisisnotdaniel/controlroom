# 047 Automatic updates

As the user, I want ControlRoom to install new releases on its own when I turn that on, so that the Mac keeps the current version while I'm away.

## What

A setting `autoUpdate` (off by default, switch in the app next to the update notice, stored in a `0600` settings file next to `push.json` with the same safety rules). The server itself never downloads or runs anything: a separate updater (`scripts/updater.sh`, installed by `launch-agent.sh` as a second LaunchAgent with a fixed interval, for example every 6 hours) reads the setting, and when it is on and a newer release exists, downloads it the same way as `install --release`, checks `SHA256SUMS` (and the attestation if #45 adds one), skips the update while a factory job is in a running stage, swaps the installed server atomically while keeping the previous version, restarts the agent and goes back to the previous version when the new one does not answer `GET /api/version` within 30 s. Each run writes one line to its own log. A push `updated` (old and new version) or `update_failed` (fixed reason) tells the user what happened. `launch-agent.sh uninstall` removes the updater too. Full path.

## Why

With notices alone I still have to be at the Mac to update. When I'm away for days the Mac should keep itself current, with a safe way back if a release is broken.
