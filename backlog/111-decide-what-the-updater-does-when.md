# 111 Decide what the updater does when rmdir of its lock fails

As the user, I want the updater to handle a failed rmdir of its lock folder on purpose, so that a stuck lock is never silently left behind.

Category: security

## What

In scripts/updater.sh a failed `rmdir` of the lock folder is ignored (stderr already goes to /dev/null, so nothing is logged). The builder left it because adding `|| finish busy` would end the run early and change behaviour. Decide whether a failed rmdir should end the run, be logged, or stay as it is, and test that choice.

## Why

Raised by the job 337 security review (round 1, nit 1): a lock that can't be removed is ignored without a trace.

## Notes

Proposed by job 337-updater-early-checks (`factory/jobs/337-updater-early-checks/`).
