# 015 Job detail

As the user, I want to open a job and read its spec, build log, reviews and decision on the phone, so that I can follow and judge a job without the Mac.

## What

Tapping a job on the board opens a job page: its state (stage, round, mode, path, risk, reviews per round, decision, note, CI result) and its records `spec.md` or `task.md`, `spec-review.md`, `findings.md`, `build.md`, `round-<n>/review-<key>.md`, `decision.md` and `feedback.md`, each rendered from Markdown with raw HTML off and shown only if it exists. Job ids from a request are matched against the folders in `factory/jobs/`; file names come from that fixed list, never from the request. Read-only. Full path.

## Why

The board says where a job is but not why: a CHANGES verdict or an escalation only makes sense with the review or decision text. Being able to read them on the phone is what makes the notifications and the approval decisions useful away from the Mac.
