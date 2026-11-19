# 088 One doctor check at a time

As the operator, I want /api/doctor to share one running check between requests, so that several open tabs don't start many git processes at once.

## What

DoctorEndpoint starts 1 + 2n git processes per request with nothing serialising them. Add an in-flight guard (concurrent callers await the same running check) and, optionally, a short cache of a few seconds; test that two parallel requests start one set of git calls.

## Why

Review-security (job 266) noted that parallel authenticated requests each spawn their own git processes; a phone and a laptop polling together double the load.

## Notes

Proposed by job 266-doctor-view (`factory/jobs/266-doctor-view/`).
