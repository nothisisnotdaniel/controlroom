# 043 Mention --tailnet-host in the launch-agent.sh usage text

As the ControlRoom owner, I want the launch-agent.sh usage text to list the --tailnet-host option, so that I can find it from the script itself without reading the README.

## What

Change the usage line printed by `scripts/launch-agent.sh` (bad or missing arguments) to show `install [--tailnet-host <name>]|uninstall|status`, and update `LaunchAgentScriptTests.BadArguments_PrintUsageExitTwoAndChangeNothing` to expect the new text.

## Why

The UX review of job 188 found that the new option can only be found in the README and CLAUDE.md; without it, a reinstall without the tailnet name silently leaves the iPhone refused with bad_host.

## Notes

Proposed by job 188-host-header-check (review-ux round 1 nit 1).

Proposed by job 188-host-header-check (`factory/jobs/188-host-header-check/`).
