# 149 Strict mode for sw.js

As a ControlRoom maintainer, I want sw.js to run in strict mode, so that sloppy-mode globals and silent errors cannot creep into the service worker.

Category: tidy-up

## What

Wrap sw.js in a strict IIFE or add use strict after importScripts("/room.js"), keep importScripts first, check that the worker still reads the values from room.js, bump the cache version, and update ServiceWorkerTests.

## Why

review-code 1 and review-security 3 in job 386: sw.js is no longer strict after the RoomKit split, and changing that changes the worker code, so it needs its own job.

## Notes

Proposed by job 386-roomkit-shared-core (`factory/jobs/386-roomkit-shared-core/`).
