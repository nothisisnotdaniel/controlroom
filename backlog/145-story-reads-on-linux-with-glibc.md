# 145 Story reads on Linux with glibc older than 2.33

As the ControlRoom owner, I want the no-follow story read to work on Linux systems with glibc older than 2.33, so that ControlRoom does not refuse story files there.

Category: security

## What

The folder-relative story read uses fstat, which glibc only exports as a symbol from 2.33. On older glibc, add a fallback (for example __fxstat or statx) or document that such systems are not supported, with a test where possible.

## Why

Review-security of job 378: on older glibc the new read may fail; the builder left it because a fallback is new behaviour.

## Notes

Proposed by job 378-story-open-nofollow (`factory/jobs/378-story-open-nofollow/`).
