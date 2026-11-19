# 056 Drop a develop check result after switching back to stable

As a ControlRoom user who switches from develop back to stable, I want a develop check that is still running to be discarded, so that I don't get an update push for a develop build.

## What

UpdateCheck.CheckHeld reads the channel once when a check starts. Re-check the channel before storing the result or sending the 'update' push, and drop the result if the channel changed.

## Why

review-security 2 (job 234): GET /api/update hides the result, but one push for a develop build can still go out. The risk is low.

## Notes

Proposed by job 234-develop-prereleases-channel (`factory/jobs/234-develop-prereleases-channel/`).
