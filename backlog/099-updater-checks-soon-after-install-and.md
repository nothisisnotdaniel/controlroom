# 099 Updater checks soon after install and retries after a skipped run

As the user, I want the updater to check for a new release soon after I install it and to try again soon when it skipped because a job was running, so that updates don't wait six hours or more.

Category: feature

## What

The updater LaunchAgent gets `RunAtLoad` (a first check a minute or so after install or login) and keeps the 6-hour `StartInterval`. When a run ends because a factory job is running (`job_running`), the updater asks for a retry in about 15 minutes instead of waiting for the next 6-hour tick (for example a second short-interval agent, or the updater sleeping and re-checking up to a bounded time; the spec picks one and says why). A retry never runs while another updater run is in progress. Tests with fake `launchctl` for both cases; README and CLAUDE.md say when updates happen.

## Why

Today the first check comes six hours after install, and every run that finds a factory job active waits another six hours; during a long auto run the Mac can go a day without updating.
