# 162 Show jobs with UUID-suffixed ids

As someone watching the factory from ControlRoom, I want every job listed, including those whose id ends in a UUID, so that I can see my own and my collaborator's jobs since factory #40.

Category: bug

## What

JobReader.IdPattern (src/ControlRoom.Server/JobReader.cs) only accepts `<1-6 digits>-<slug>`, so the job folders named `<n>-<slug>.<32 hex>` that job.py has made since factory #40 (job 398) are skipped by the board, the job actions, history and releases that use StateJsonJobFormat.IdPattern. Accept an optional `.` followed by exactly 32 lower-case hex characters after the slug, and keep refusing everything else (path separators, dots elsewhere, upper case, longer suffixes). Check every place that builds a path or a link from a job id still works with the dot. Add tests for a legacy id, a UUID id, and refused look-alikes.

## Why

Since job 398 ControlRoom shows none of the new jobs (10 so far, including running ones like 405), so neither person can see from the phone what is running, and actions such as approve or rework can't reach those jobs.
