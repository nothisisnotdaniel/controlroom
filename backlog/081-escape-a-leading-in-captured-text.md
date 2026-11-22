# 081 Escape a leading # in captured text

As the operator, I want a capture that starts with # to stay plain text in the draft story, so that it doesn't turn into a heading that breaks the story's sections.

Category: bug

## What

BacklogEditor.RenderDraft puts the cleaned text on its own line under ## What, so text starting with # or ## (e.g. "## Notes") becomes a heading. Escape or indent a leading # and update the AC-3 test for the story bytes.

## Why

Review-security and the spec review (job 261) noted the heading injection; job 261 kept the bytes its AC-3 fixes.

## Notes

Proposed by job 261-quick-capture (`factory/jobs/261-quick-capture/`).
