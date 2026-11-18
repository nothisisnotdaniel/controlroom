# 051 Develop prerelease builds and an opt-in develop update channel

As the ControlRoom owner, I want every push to develop to publish a prerelease build, and an opt-in setting that lets this Mac follow those builds, so that I can run and test the newest work without making a release.

## What

1) Extend .github/workflows/release.yml (as Times does) so a push to develop builds the same osx-arm64 tarball plus SHA256SUMS and attestation and publishes it as a GitHub prerelease vX.Y.(Z+1)-develop.<run number>, one patch above the highest proper release tag (a develop-version script like Times .github/scripts/develop-version.sh). Pushes that change only backlog.md / backlog/ are skipped. Release PRs keep working as today and still refuse -develop titles. After a proper release vX.Y.Z, delete every -develop prerelease below X.Y.Z with its tag (warn only on failure). 2) launch-agent.sh install --release accepts a -develop tag. 3) Add an update channel setting (stable default, or develop) next to the auto-update switch, stored server-side. On stable, the update notice (#46) and the updater (#47) behave exactly as now and ignore prereleases. On develop, they also consider -develop prereleases and pick the newest by SemVer precedence (a develop build of the next patch counts as newer than the current release). The updater keeps every #47 safety check (SHA256SUMS, health and launchd check, rollback). Keep the outbound allowlist (api.github.com, github.com release downloads) and the rule that the server never runs commands.

## Why

Today ControlRoom builds only on release PRs into main, so nothing runs on develop and work merged by the factory can only be tried by building from source. Times already publishes develop prereleases. Without a channel, those builds would have to be installed by hand every time.

## Notes

Asked for by the user on 2026-11-18. The user chose the opt-in develop channel over install-by-hand only. Run as quality, high risk.
