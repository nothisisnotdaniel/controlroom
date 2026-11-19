# 067 Retry button when the inbox fails to load

As the operator, I want a Retry button when the inbox can't load, so that I can try again at once instead of waiting.

## What

A failed inbox load replaces the list with "Can't load …" and keeps no count. Add a Retry button to that message.

## Why

Review-ux (job 251) noted the only recovery today is the next live check, which can feel stuck.

## Notes

Proposed by job 251-approval-inbox (`factory/jobs/251-approval-inbox/`).
