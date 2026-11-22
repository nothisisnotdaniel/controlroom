# 035 Keep the token in memory when localStorage is unavailable

As the ControlRoom user in a private browser window, I want the web app to keep my token for the open page when it can't store it, so that the token view doesn't come back right after I save.

Category: bug

## What

In wwwroot/token.js, keep the token in a module variable as a fallback when `localStorage` throws, and have `readToken` return it. Fix the comment to match. Add a WebApp test for the fallback.

## Why

The approver of job 184 found that `storeToken` swallows the error but `readToken` then returns an empty string, so the token view shows again after Save, unlike what the comment says.

## Notes

Proposed by job 184-access-token (decision.md, approver note).

Proposed by job 184-access-token (`factory/jobs/184-access-token/`).
