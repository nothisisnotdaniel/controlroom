# 044 Clearer refusal when the tailnet name is not configured

As the ControlRoom owner, I want a clear hint when my iPhone reaches the server before the tailnet name is set, so that I know to run the server with --TailnetHost instead of guessing what bad_host means.

## What

When a request is refused with `421 bad_host` and no tailnet name is configured, log one fixed warning line (without the Host value) saying the tailnet name is not set and how to set it (`--TailnetHost` or `launch-agent.sh install --tailnet-host`); and have the web app show a fixed text for the `bad_host` error code. Keep the response body unchanged.

## Why

The UX review of job 188 noted that the phone only sees {"error":"bad_host"}, which does not say what to do; the user is likely to hit this right after the merge.

## Notes

Proposed by job 188-host-header-check (review-ux round 1 nit 2).

Proposed by job 188-host-header-check (`factory/jobs/188-host-header-check/`).
