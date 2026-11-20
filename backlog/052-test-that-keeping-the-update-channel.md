# 052 Test that keeping the update channel starts no check

As a ControlRoom maintainer, I want the test for a settings POST that keeps the channel to prove no update check started, so that a regression that starts a needless GitHub call is caught.

Category: test

## What

UpdateApiTests.PostThatKeepsTheChannel_StartsNoCheck asserts 'no request' right after the POST, so a wrongly started background check could slip past it. Find a way to observe that no check started (for example a check counter or hook on the checker) without a time-based wait and without racing a wrongly started check.

## Why

review-code 1 (job 234): the test can only miss a regression, never fail at random, so a real bug of this kind would go unnoticed.

## Notes

Proposed by job 234-develop-prereleases-channel (`factory/jobs/234-develop-prereleases-channel/`).
