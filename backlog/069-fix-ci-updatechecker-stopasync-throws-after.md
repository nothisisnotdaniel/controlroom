# 069 Fix CI: UpdateChecker.StopAsync throws after Dispose

As the maintainer, I want the develop CI build to pass reliably, so that every push makes a working prerelease.

## What

Develop CI run 44468908939 failed in step Test: HttpSafetyTests.OtherMethods_AreRefusedAndWriteNothing(PATCH, /api/story?...) threw System.ObjectDisposedException (The CancellationTokenSource has been disposed) from UpdateChecker.StopAsync (src/ControlRoom.Server/UpdateCheck.cs:278, stopping.Cancel()) during WebApplicationFactory.Dispose. Make StopAsync and Dispose safe in any order and when called twice (no throw after Dispose), and add a test that calls Dispose then StopAsync, and StopAsync twice. 1383 of 1384 tests passed; this is a shutdown race, flaky locally.

## Why

Working builds are a top priority: a red develop CI means no prerelease for that push, and a flaky shutdown test hides real failures.
