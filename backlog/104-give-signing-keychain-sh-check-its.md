# 104 Give signing-keychain.sh check its own message for a malformed fingerprint

As the user, I want a clear error when the signing keychain check gets a malformed fingerprint, so that a broken call is told apart from a missing identity.

Category: ci

## What

signing-keychain.sh check reports a malformed SHA-1 argument the same way as an identity that is not in the keychain. Validate the argument (40 hex characters) and fail with its own message, with a test.

## Why

Raised by the job 333 code review (round 1, nit 1). signing-cert.sh already catches a bad file earlier, so this only matters if check is ever called another way; without it the error message would point at the wrong cause.

## Notes

Proposed by job 333-fix-release-signing (`factory/jobs/333-fix-release-signing/`).
