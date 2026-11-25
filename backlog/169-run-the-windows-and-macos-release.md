# 169 Run the Windows and macOS release builds in parallel

Category: ci

## What

In .github/workflows/release.yml the macOS `release` job needs the whole `windows` job, so the macOS test run (about 7 min 18 s) starts only after the Windows tests and ZIP (about 5 min 30 s) are done; a develop push takes about 14½ minutes. Split it: a `windows` job and a `macos` job (checkout, .NET, test, publish, sign, verify, smoke check, pack, pack RoomKit, upload the tarball and nupkg as artifacts) both start right after `version`. Then a short `release` job needs both: it downloads the three files, writes SHA256SUMS, attests them and creates the GitHub release, then prunes. Keep every check and the security properties: pinned actions, `permissions: {}` at the top with each job's own permissions, contents: write / id-token / attestations only on the final job, signing secrets only in the macOS signing step, the keychain cleanup with if: always(), and refs reaching shell only through env. Check that the artifacts the release job uploads are the same files the macOS job verified (same names and checksums). Update the workflow's header comment and any docs that describe the jobs.

## Why

Each develop build takes about 14½ minutes before the updater can install it, and about 5 minutes of that is the macOS job waiting for Windows. Running them side by side should bring a run to about 8–9 minutes without dropping any test or check.
