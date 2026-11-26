# 174 Fix the flaky Windows interval release test

As the owner, I want the Windows release tests to pass reliably, so that a green develop means the app builds.

Category: ci

## What

WindowsReleaseTests.Scripts_ManageOnlyTheFixtureAndReportConfirmedOutcomes(scenario: "interval") failed in windows-tests (2) on run https://github.com/nothisisnotdaniel/controlroom/actions/runs/45766316985 (develop 8ff1abb): WindowsReleaseFixture.ps1:89 threw "Expected [current], got [not_due]." Find why the interval scenario reports not_due on CI (clock, test ordering, or the new two-part split) and make it reliable.

## Why

It passed on every earlier run; a red release run blocks releases and auto runs.

## Notes

Seen right after #172 split the release tests into two parallel parts per OS.
