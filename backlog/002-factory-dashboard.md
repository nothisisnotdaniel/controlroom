# 002 Factory dashboard

As the user, I want the factory's jobs board inside ControlRoom, so that I have one place for backlogs and jobs, on the Mac and on the phone.

## What

Bring the factory dashboard (`factory/dashboard.html`, `/board.json` in `factory/server`) into ControlRoom: the jobs board from every `factory/jobs/*/state.json` (stage, round, reviews, decision, mode, path, risk, project filter, live refresh), the `[h]` lines of every project at the top, and a job page that shows its spec, spec review, build log, reviews and decision as Markdown (raw HTML off). Once it matches the old dashboard, `factory/server` can be retired (a factory backlog change, done by hand).

## Why

The old dashboard only runs on the Mac at port 8000 and is a second server to keep running. Moving it into ControlRoom puts job progress next to the backlogs, makes it reachable from the phone, and lets `factory/server` be retired.
