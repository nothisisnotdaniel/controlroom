# 103 Keep the runner keychain search list non-empty when removing the signing keychain

As the user, I want the release cleanup to leave the runner keychain search list in a sane state, so that a later step on the same runner never finds an empty search list.

Category: ci

## What

In .github/scripts/signing-keychain.sh, remove calls security list-keychains -d user -s with the keychains that are left, even when none are left, which empties the search list. Skip that call (or restore the default login keychain) when the remaining list is empty, and add a test with the fake security.

## Why

Raised by the job 333 security review (round 1, nit 1). Harmless on a throwaway GitHub runner today, but an empty search list can break later keychain or codesign steps if the workflow ever grows or runs on a reused runner.

## Notes

Proposed by job 333-fix-release-signing (`factory/jobs/333-fix-release-signing/`).
