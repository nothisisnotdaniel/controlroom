# 172 Split the release tests across parallel runners

As the user, I want the release run's tests split across parallel runners, so that a new build reaches me minutes sooner.

Category: ci

## What

In .github/workflows/release.yml the macOS job runs the full dotnet test suite (395 s on run 45726150873) and the Windows job runs it again (287 s); the builds and packing take only 15-20 s. Split each OS's test run into two (or more) parallel jobs, for example by test class or namespace filter, so that every test still runs exactly once per OS. The build, pack and release jobs only start when every test part has passed, as now.

## Why

The tests are almost the whole release run (7 min 45 s). The run waits for its slowest job, so running the parts at the same time should save about 2-3 minutes per run.

## Notes

A guard (test or check) should fail if a test class lands in no part, so new tests can't be silently skipped. Keep the SHA-256 checks from #169.
