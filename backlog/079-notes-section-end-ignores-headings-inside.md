# 079 Notes section end ignores headings inside code blocks

As the operator, I want a note added after a story with a fenced code block to land in the right place, so that a # line inside code does not split the Notes section.

## What

The Notes section ends at the next line starting with # or ##, even inside a fenced code block. Skip fenced blocks when finding the section end, update the spec rule and add a test.

## Why

Review-code (job 259) noted the edge case; job 259 followed its spec, which defines the end without fences.

## Notes

Proposed by job 259-notes-on-story (`factory/jobs/259-notes-on-story/`).
