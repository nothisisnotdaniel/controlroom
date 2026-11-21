# 136 Start, end and time per agent on job cards

As the user, I want each job card to show when the job started, when it ended and how long each agent worked, so that I see at a glance how long a job took and where the time went.

Category: ui

## What

Job cards in the Jobs view show the start time and, once the job has ended, the end time and the total duration, from `started` and `ended` in the job record (factory#22); a running job shows how long it has been running. The job view lists each agent with its time (`tokens.<agent>.seconds`) next to its tokens, formatted like 4 min or 1 h 12 min. A job or agent without these fields shows no time, never 0. Times use the browser's local time zone. Tests cover a running job, a finished job, an agent without seconds and an old record.

## Why

Today a card shows the stage and tokens but not when the job ran or how long its agents took.
