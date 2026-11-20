# 034 Check the token file's mode and owner on the open handle

As the ControlRoom owner, I want the token file's mode and owner checked on the file the server actually opened, so that a file swapped in after the check can't be used as the token.

Category: security

## What

Let SafeFileReader (or a token-specific reader) return the opened handle, and check mode 0600 and the current user as owner with `fstat` on that handle instead of on the path. Keep the current error texts and add tests for a wrong mode and a wrong owner where the OS allows it.

## Why

The security and code reviews of job 184 noted that the mode check runs on the path before the `O_NOFOLLOW` open and the owner is not checked, which leaves a small check-then-open gap.

## Notes

Proposed by job 184-access-token (review-security round 1 nit 2, review-code nit 3).

Proposed by job 184-access-token (`factory/jobs/184-access-token/`).
