# 048 Strict SemVer prerelease identifiers in release tags

As the user, I want release tags checked against strict SemVer, so that a malformed prerelease tag such as v1.2.3-01 is refused before anything is built or installed.

## What

Make `valid_tag` in `.github/scripts/release-version.sh` and the matching tag check in `scripts/launch-agent.sh install --release` refuse numeric prerelease identifiers with a leading zero (for example `1.2.3-01`), as SemVer 2.0.0 section 9 requires. Add test cases for both the release workflow script and the launch-agent script.

## Why

The security review of job 225-release-builds-github (nit 3) found that both checks accept leading-zero numeric identifiers. The tag charset is already limited, so this is not exploitable, but a non-SemVer tag could be published or installed and would sort wrongly against later versions in update checks.

## Notes

Proposed by job 225-release-builds-github (security review round 1, nit 3); the user asked for it to go to the backlog.
