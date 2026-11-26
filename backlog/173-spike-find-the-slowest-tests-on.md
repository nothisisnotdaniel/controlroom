# 173 Spike: find the slowest tests on CI

As the user, I want to know which tests take the most time on the GitHub runners, so that we fix the ones that matter.

Category: test

## What

Download the .trx test results of a recent release run (or add trx output to it), list the 20 slowest tests and test classes on macOS and Windows, and compare them with a local run. Say why each is slow (waits, servers started per test, git repos, process starts) and suggest one backlog item with the biggest saving.

## Why

The suite takes about 2 minutes locally but 5-7 minutes on CI; we don't know which tests cause the gap.

## Notes

No code changes. Spike only.
