# 168 Update the factory's claim marks live

Category: feature

## What

Project backlogs update live through `/api/changes` when their claims change, but the factory's own entry has no fingerprint there, so new or released claims on factory items only show after a reload. Add the factory's claims to the `/api/changes` fingerprint.

## Why

Claims on factory items should show up as soon as the server sees them, the same as for projects.
