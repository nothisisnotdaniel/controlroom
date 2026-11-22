# 120 Refuse a target field on backlog changes other than place

As the user, I want the backlog change API to refuse fields an action does not use, so that a malformed request is rejected instead of half-understood.

Category: security

## What

POST /api/backlog/change accepts a `target` field on every action but only `place` reads it. Refuse `target` (400 invalid) on every other action, with tests, and note the rule in CLAUDE.md. Check that the web app never sends it elsewhere.

## Why

Raised by the job 345 security review (nit 1): the exact-fields rule the other endpoints follow is looser here.

## Notes

Proposed by job 345-backlog-drag-drop (`factory/jobs/345-backlog-drag-drop/`).
