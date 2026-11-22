# 055 Limit how often channel changes start update checks

As a ControlRoom user, I want repeated update-channel flips to start at most one GitHub check per short gap, so that the anonymous GitHub quota is not used up.

Category: feature

## What

POST settings in UpdateEndpoint.cs starts Task.Run(checker.CheckSafely) on every channel change. Overlapping checks are skipped, but back-to-back flips each start one. Add a minimum gap between channel-change checks, like PushTestLimiter.

## Why

review-security 1 (job 234): only the token holder can do it, but each flip spends the anonymous 60/h GitHub quota that the regular checks need.

## Notes

Proposed by job 234-develop-prereleases-channel (`factory/jobs/234-develop-prereleases-channel/`).
