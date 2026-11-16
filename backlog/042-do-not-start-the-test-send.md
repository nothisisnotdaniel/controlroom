# 042 Do not start the test-send wait when the test send fails

As the ControlRoom user, I want to retry a failed test notification right away, so that a passing network error does not make me wait.

## What

In `POST /api/push/test`, start the 10-second rate-limit window only when at least one send was attempted, or reset it when every send failed before reaching a push service. Keep the 429 response for a second call after a real send. Add tests with the injectable clock.

## Why

The code review of job 187 round 2 found that a failed test send still starts the window, so the user cannot retry for 10 seconds after an error.

## Notes

Proposed by job 187-push-notifications (review-code round 2, nit 2).

Proposed by job 187-push-notifications (`factory/jobs/187-push-notifications/`).
