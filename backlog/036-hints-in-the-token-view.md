# 036 Hints in the token view

As the ControlRoom user, I want the token view to tell me when the field is empty or the token looks wrong, so that I know what to fix.

Category: ux

## What

In wwwroot/token.js, show a short fixed hint when Save is pressed with an empty field, and after a refused token mention that a token is 43 characters (shown by `token show`). Fixed texts only; add WebApp tests.

## Why

The UX review of job 184 noted that an empty Save only refocuses the field and a refused token gives no hint, so a user typing the token by hand on the iPhone is left guessing.

## Notes

Proposed by job 184-access-token (review-ux round 1 nits 1 and 2).

Proposed by job 184-access-token (`factory/jobs/184-access-token/`).
