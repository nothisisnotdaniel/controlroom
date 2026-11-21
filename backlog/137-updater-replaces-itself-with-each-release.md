# 137 Updater replaces itself with each release

As the user, I want the updater to install its own new version and LaunchAgent together with each new release, so that fixes to the updater reach my Mac without reinstalling by hand.

Category: feature

## What

When the updater installs a release, it also replaces `<support>/updater.sh` with the release's `updater.sh` (from the same checked and signature-verified download, 0700, written atomically) and rewrites the updater LaunchAgent plist the way `launch-agent.sh install --release` does, keeping the Tailnet host, factory root and channel. The new updater takes effect from its next run; the running one never re-executes itself mid-run. A release without `updater.sh`, or one that fails the checks, leaves the old updater as it is and reports a fixed reason. Rolling back the server also restores the previous updater. Tests with fake launchctl cover the swap, the plist rewrite, a release without updater.sh and the rollback.

## Why

The updater swaps the server but never itself, so #99 (early checks and retries) only reached the Mac after a manual reinstall on 2026-11-21.

## Notes

Found on 2026-11-21: `launch-agent.sh install --release` without a version picks the latest non-prerelease (v0.1.0), not the newest develop build, so a develop-channel reinstall without a tag would go back to an old version (it refused only because v0.1.0 failed the checks). The spec should decide whether install --release follows the channel setting.
