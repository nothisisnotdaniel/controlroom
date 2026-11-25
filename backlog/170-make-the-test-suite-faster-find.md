# 170 Make the test suite faster: find and fix the slowest tests

Category: test

## What

`dotnet test -c Release` takes about 5 min on the Windows runner and about 7 min on macOS for about 2970 tests. Measure where the time goes (for example `dotnet test --logger "trx"` or console verbosity normal with per-test durations, per test project and per test class), list the slowest 20 tests in the build log, and fix the causes: fixed sleeps and polling waits replaced by waiting on the actual event, long timeouts in tests that expect failure, repeated setup of temporary git repos or servers that can be shared per class, and test classes that can safely run in parallel. Keep every test's meaning; don't delete or skip tests to save time. Record the before and after times of the full suite in build.md.

## Why

The full suite runs twice in every CI build and twice in every factory job (builder and approver), so its run time adds up to most of the wait for a build and a good part of every job.
