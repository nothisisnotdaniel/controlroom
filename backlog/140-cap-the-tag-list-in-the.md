# 140 Cap the tag list in the releases overview

As the user, I want the releases overview to read a bounded number of git tags, so that a repo with very many tags cannot slow the server.

Category: security

## What

The releases endpoint reads every tag git lists with no cap. Add a fixed cap (for example the newest 1000 by version sort) and say in the spec which tag counts as the release when the cap is reached.

## Why

Review-security of job 375: an unbounded list is read on each request; low risk today because repos are local and the call needs the token.

## Notes

Proposed by job 375-releases-overview (`factory/jobs/375-releases-overview/`).
