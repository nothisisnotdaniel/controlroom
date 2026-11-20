# 059 Behaviour tests for the web app's JavaScript

As the ControlRoom maintainer, I want the web app's JavaScript tested by running it, not by matching its source text, so that a refactor that keeps the strings but breaks the behaviour is caught.

Category: test

## What

WebAppTests checks board.js and the other scripts with text assertions that pin source strings. Find a way to run the page scripts in tests (for example a small DOM test harness with no new package, or a reasoned package choice in the spec) and move the job page and board tests onto it.

## Why

review-code 1 (job 248): text assertions pin strings, not behaviour, so real regressions in rendering can pass the tests.

## Notes

Proposed by job 248-job-detail-gaps (`factory/jobs/248-job-detail-gaps/`).
