# 098 Updater waits for launchd before starting the new server

As the user, I want automatic updates to actually install, so that the Mac runs the newest ControlRoom instead of rolling back every time.

## What

In `scripts/updater.sh` (and `launch-agent.sh` where it does the same), after `launchctl bootout` wait until `launchctl print gui/<uid>/local.controlroom.server` fails (the service is gone, polling every 0.5 s, at most 15 s), then `launchctl bootstrap`, and retry the bootstrap up to 5 times 1 s apart while it exits non-zero (error 5 or 37). Check the bootstrap exit status instead of ignoring it, and log a fixed reason (`bootstrap_failed`) when it still fails, then roll back as today. Do the same for the rollback and the swap-failed paths. Add `LaunchAgentScriptTests`/updater tests with a fake `launchctl` whose first bootstrap after a bootout fails with 5 and whose print keeps the service for a moment, and check the update ends as `updated`.

## Why

On 2026-11-19 the updater rolled back 0.1.1-develop.18 although the build was fine: right after bootout, launchd was still removing the service, `launchctl bootstrap` failed with `5: Input/output error`, the updater ignored it and the health check timed out. Reproduced by hand with the same two commands. Until this is fixed, automatic updates never install anything.
