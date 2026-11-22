# 144 Settings file cap fits every valid interval

As the user, I want every valid settings file to fit the size cap, so that a large update interval never makes the updater treat updates as off.

Category: security

## What

The settings file cap is 64 bytes, but a four-key file with channel develop, autoUpdate false and a 6-digit updateInterval is 65 bytes and is refused by all three readers. Raise the cap to fit the longest valid file (or narrow the interval to the fixed choices) in all readers together, with a test for the longest valid file.

## Why

Review-security of job 376: the readers agree and fail safe, but a valid setting should never be refused.

## Notes

Proposed by job 376-update-interval (`factory/jobs/376-update-interval/`).
