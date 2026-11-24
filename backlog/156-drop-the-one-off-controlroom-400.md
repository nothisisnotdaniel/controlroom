# 156 Drop the one-off ControlRoom 400 test workflow

As a maintainer, I want only workflows with a lasting purpose in the repo, so that CI stays easy to follow.

Category: ci

## What

Remove `.github/workflows/controlroom-400-tests.yml` ("ControlRoom 152 verification", runs on push to `factory-ci/400-windows-release-tests`), or give it a lasting purpose and trigger. Update any test that checks the list of workflows.

## Why

It was added to verify job 400 on one branch and now sits on develop doing nothing. Found in the review of PR #3.

## Notes

From PR #3 review, nit 3. Read-only workflow, so no risk while it stays.
