# 071 Name the task in the Done confirmation

As the operator, I want the Done confirmation in My tasks to name the task, so that I know which one I am about to mark done.

Category: ux

## What

tasks.js asks confirm(TasksTexts.confirmDone) with one fixed text ("Mark this task done?"). Make it name the item, for example "Mark #12 Title done?", and update the AC-9 test that pins the fixed text.

## Why

Review-ux and review-ui (job 254) noted the generic text; on a long list a tap on the wrong row is easy to miss. Job 254 kept the fixed text because its spec and test require it.

## Notes

Proposed by job 254-my-tasks (`factory/jobs/254-my-tasks/`).
