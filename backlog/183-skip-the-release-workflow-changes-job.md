# 183 Skip the release workflow changes job on closed pull requests that are not release merges

As the owner, I want the release workflow to not start its changes job for closed pull requests that never release, so that CI does no needless work on fork or unmerged PRs.

Category: security

## What

In .github/workflows/release.yml, the changes job added by job 471 also runs on closed pull_request events, fork PRs included, where it only reads the event and fails open. Gate it the same way the version job is gated (merged PR from develop into main, or a push), and update the workflow tests that pin which jobs have an if: (AC-9 of job 471).

## Why

Raised by the security review of job 471: harmless today (contents: read, no token, no secrets, persist-credentials false), but running anything on fork PR events widens what untrusted events can trigger.

## Notes

Proposed by job 471-ci-skip-backlog-changes.9afbe8bdf51e4bd2b0eb2b2992fac9fe (`factory/jobs/471-ci-skip-backlog-changes.9afbe8bdf51e4bd2b0eb2b2992fac9fe/`).
