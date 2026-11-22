# 058 Guard the release workflow's GITHUB_ENV writes

As the ControlRoom maintainer, I want the release workflow to refuse a version or commit value with unexpected characters before writing it to GITHUB_ENV, so that a regression in a version script can't inject extra environment lines.

Category: security

## What

In .github/workflows/release.yml (Version step), check RELEASE_VERSION and RELEASE_TARGET with a one-line case guard (for example *[!0-9A-Za-z.-]*) exit 1) right before echoing them into $GITHUB_ENV, and add a ReleaseWorkflowTests check for the guard.

## Why

review-security 1 (job 247): today the scripts already emit only a strict version and SHA, so this is defence in depth. Without it, a future script regression could write arbitrary lines to GITHUB_ENV.

## Notes

Proposed by job 247-develop-ci-test-version (`factory/jobs/247-develop-ci-test-version/`).
