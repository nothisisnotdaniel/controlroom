# 030 Cap the bytes read per board and job response

As the ControlRoom owner, I want each /api/board and /api/job response to stop reading after a fixed total byte budget, so that a folder full of large records can't make one request read and parse many megabytes.

Category: security

## What

Add a total byte budget per response in BoardEndpoint and JobEndpoint (for example 8 MiB for /api/job and 4 MiB for /api/board). When the budget runs out, return the rest of the records as not readable (the existing `readable: false`) instead of reading them. Add tests with a temp factory root whose records go over the budget.

## Why

The security review of job 183 noted that one /api/job response can hold up to 87 records of 1 MiB each, all parsed, and /api/board reads every state.json on each 5 s poll. That is fine on loopback today, but a budget bounds the work once the app is reachable over Tailscale.

## Notes

Proposed by job 183-factory-dashboard (review-security round 1 nit 2).

Proposed by job 183-factory-dashboard (`factory/jobs/183-factory-dashboard/`).
