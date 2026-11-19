# 017 Approval inbox

As the user, I want one list of every item waiting for approval across all projects, so that I can work through them in one go.

## What

An Inbox view lists every `[?]` item of every project, oldest proposal first, each with its project, `#N Title`, the job that proposed it (from the story's Notes) and its story one tap away. Each item has Approve (`[?]` to `[ ]`) and Drop (removes the line and its story file), using the write path of #4 with its checks and one commit per change. The app shows the count of waiting items, for example on the Inbox tab. Full path, high risk (writes).

## Why

Follow-ups pile up across projects after every job, and today they sit in four files. One queue with the story at hand turns approving into a short routine instead of a chat with Claude.
