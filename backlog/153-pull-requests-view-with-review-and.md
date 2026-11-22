# 153 Pull requests view with Review and consent buttons

As the factory's owner, I want to see every open pull request and its review in ControlRoom and ask for a review from the iPhone, so that I can look after collaborators' PRs without a terminal.

Category: feature

## What

Add a Pull requests view that reads only `factory/prs/<repo>-<n>/` (`pr.json`, `state.json`, the latest `round-<k>/comment.md`), never `gh` or the network. For each PR it shows the number, title, author and repo; a mark: new (no round), changed (the head differs from the latest round's `headSha`), reviewed, merged or closed; the latest round's test result, reviewer verdicts and comment (Markdown with raw HTML off). Add `POST /api/pr/act` with `action` `review` or `cancel`. It works like `/api/job/act`: under one lock, the PR is matched against the discovered records, never joined into a path. It writes or deletes `request.json` through `SafeFileWriter`, exactly `{"action":"review","headSha":"…","consent":true|false,"requested":"…"}`, with `headSha` the one the client saw (`409 changed` otherwise). For a PR whose author has no write access, the Review button first shows the warning: its code runs with the Mac's environment (tokens, keys) and can write to the repo's `.git` (hooks). Only an explicit tap on Run its tests sets `consent: true`. Update CLAUDE.md's safety rules for the one new file the server writes.

## Why

PRs from collaborators are otherwise invisible until a Claude session runs `/factory pr`. ControlRoom already lets the user approve and rework jobs through a request file. Doing the same for PRs keeps its rule that the server runs nothing, while the factory's watcher does the work.

## Notes

Needs the watcher and the request format from factory#43.
