# 057 Filter drafts in the updater's develop tag pick too

As a ControlRoom maintainer, I want the updater and the server to pick the newest develop release the same way, so that the two parsers can't drift apart.

Category: bug

## What

scripts/updater.sh develop_tag doesn't filter by draft, but the server's ParseList does. Anonymous calls never see drafts (AC-11), so this is only for consistency: either filter drafts in the updater as well or note the difference in a comment.

## Why

review-security 3 (job 234): no bug today, but the two parsers differ, which could matter if the call ever becomes authenticated.

## Notes

Proposed by job 234-develop-prereleases-channel (`factory/jobs/234-develop-prereleases-channel/`).
