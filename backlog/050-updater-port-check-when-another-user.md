# 050 Updater port check when another user holds the port

As the ControlRoom owner, I want the updater to tell a port held by another user apart from a dead server, so that an update rolls back cleanly instead of ending in rollback_failed.

Category: bug

## What

scripts/updater.sh server_owns_port uses lsof, which cannot see other users sockets without root. A port held by another user therefore shows no listener and ends in rollback_failed. Detect this case (for example: the port is busy but lsof shows no owner) and roll back cleanly with a clear reason. Also prefer /usr/sbin/lsof over a Homebrew lsof.

## Why

Left by the job 229 round-2 rework (point 2, launchd ownership check). It fails closed today, but a failed push with rollback_failed is misleading and leaves the user to sort it out by hand.

## Notes

Proposed by job 229-automatic-updates (round 2, build.md left line; review-security and review-code nits).
