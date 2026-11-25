# 165 Bound the claim fetch: shallow fetch and cap ls-remote output before parsing

Category: security

## What

The collaborator-claims reader (job 409) runs `git fetch` for refs/factory/claims/* without `--depth=1` and `--prune`, and parses the whole `git ls-remote` output before taking the first 200 refs. Fetch with `--depth=1 --prune`, and stop reading ls-remote output after 200 lines (or a byte limit) before parsing. Add tests with a local bare origin that has more than 200 claim refs.

## Why

The server's only network call should do the least work it can: a claim is one small commit, and a big or hostile origin shouldn't make it read or keep more than it needs.
