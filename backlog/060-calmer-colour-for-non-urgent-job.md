# 060 Calmer colour for non-urgent job notes

As the user reading a job on the phone, I want a job's note shown in red only when the job needs me, so that an ordinary note doesn't look like an alarm.

Category: ui

## What

app.css .job-note (job page) and the card note on the board use var(--human) (red) for every note. Use the red only when the stage is needs-human and a neutral colour otherwise, in both views, in light and dark mode.

## Why

review-ui 1 (job 248): every note reads as urgent today, even on a job that is fine.

## Notes

Proposed by job 248-job-detail-gaps (`factory/jobs/248-job-detail-gaps/`).
