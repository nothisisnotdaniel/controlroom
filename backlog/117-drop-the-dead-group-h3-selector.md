# 117 Drop the dead .group h3 selector

As the maintainer, I want unused CSS rules removed, so that app.css only holds rules that still match the markup.

Category: tidy-up

## What

Since every backlog group is a details/summary, the `.group h3` selector in app.css matches nothing. Remove it and update the WebAppTests.AppCss_DoneGroupMarkerRulesStayScopedToGroupSummary assert that names it.

## Why

Raised by the job 342 code review (nit 1): dead selectors make the stylesheet harder to read and to change.

## Notes

Proposed by job 342-collapsible-groups (`factory/jobs/342-collapsible-groups/`).
