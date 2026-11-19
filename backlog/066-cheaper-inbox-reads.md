# 066 Cheaper inbox reads

As the operator, I want the inbox to load quickly however many follow-ups are waiting, so that the server stays light.

## What

Each /api/inbox call reads every numbered [?] story (up to 1 MiB each) of every project. Read only the part of each story the inbox needs, or cache the result by the change fingerprint.

## Why

Review-security (job 251) noted the cost grows with the number of waiting stories; with many follow-ups each inbox load does a lot of file reading.

## Notes

Proposed by job 251-approval-inbox (`factory/jobs/251-approval-inbox/`).
