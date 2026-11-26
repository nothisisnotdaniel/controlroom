# 175 Fix the Windows updater locked last-check case

As the owner, I want the Windows release tests to pass, so that a green develop means the app builds.

Category: ci

## What

The Windows release test WindowsReleaseTests interval scenario still fails after #174 (run https://github.com/nothisisnotdaniel/controlroom/actions/runs/45768507350, develop b1bc5a5). The new diagnostics name the step: WindowsReleaseFixture.ps1 line 703, where the fixture holds last-check open with FileShare None ([IO.File]::Open(stamp, Open, Read, None)) and expects Expect-Update current (a locked, unreadable stamp counts as due); the updater answers not_due. Diagnostics: first-check FileNotFoundException, last-check IOException. Find why updater.ps1 (both copies, scripts/ and src/RoomKit/scripts/) treats a locked last-check as not due (an exception swallowed into not_due, or a cached read) and make an unreadable stamp count as due, as the macOS updater does; or, if the test expectation is wrong, say why and fix the test.

## Why

Develop is red on Windows, which blocks releases and auto runs; #174 fixed a different step.

## Notes

Fails in windows-tests (2) on both runs since #172 split the tests; check whether the split changed anything that affects this case (for example the file lock behaviour or the order of the steps before it).
