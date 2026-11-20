# 033 Refuse a token folder others can write to

As the ControlRoom owner, I want the server to refuse a token folder that group or others can write to, so that another local user can't swap or read my access token.

Category: security

## What

When the token folder already exists, check its mode on start: refuse to start with a fixed error if group or others can write to it (or tighten it to 0700 when it is the default private path). Add tests for a loose default folder and a loose `--TokenFile` folder.

## Why

The security and code reviews of job 184 found that an existing token folder with loose modes is neither tightened nor refused, so a folder made earlier by hand could expose the token file.

## Notes

Proposed by job 184-access-token (review-security round 1 nit 1, review-code nit 2).

Proposed by job 184-access-token (`factory/jobs/184-access-token/`).
