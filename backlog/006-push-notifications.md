# 006 Push notifications

As the user, I want push notifications from ControlRoom when a job needs me or finishes, or new items wait for approval, so that I hear about it without checking.

## What

Web push to the installed iPhone app (iOS 16.4 or later) when a job reaches `needs-human` or `done`, or a project gets new `[?]` lines; the server keeps its push keys and subscriptions next to its settings and sends only the project, job id and stage, never record text.

## Why

The factory runs for long stretches on its own, and the points where it needs me are easy to miss. Sending only the project, job id and stage keeps record text off Apple's push servers.
