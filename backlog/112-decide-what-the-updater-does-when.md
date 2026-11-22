# 112 Decide what the updater does when kill in cleanup fails

As the user, I want a failed kill in the updater's cleanup to be handled on purpose, so that a leftover background process is never missed silently.

Category: security

## What

In scripts/updater.sh `cleanup` a failed `kill` writes only to stderr, which is /dev/null, so it never reaches the log. Decide whether it should be logged or can stay as it is, and test that choice.

## Why

Raised by the job 337 security review (round 1, nit 2): a failed kill leaves no trace in the updater log.

## Notes

Proposed by job 337-updater-early-checks (`factory/jobs/337-updater-early-checks/`).
