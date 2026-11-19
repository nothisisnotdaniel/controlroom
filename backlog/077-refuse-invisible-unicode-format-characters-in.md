# 077 Refuse invisible Unicode format characters in written text

As the operator, I want notes, titles and stories to refuse invisible format characters, so that nothing hidden can change how a backlog line or story reads.

## What

BacklogEditor.CleanNote only rejects char.IsControl; Unicode category Cf (U+202E bidi override, U+200B zero-width space and similar) passes. Refuse or strip them in notes, and check title and story input the same way, with tests.

## Why

Review-security (job 259) noted that bidi overrides can make text read differently from what is stored; job 259 kept to its spec.

## Notes

Proposed by job 259-notes-on-story (`factory/jobs/259-notes-on-story/`).
