# 070 Guard UpdateChecker.StartAsync after Dispose

As the maintainer, I want hosted services to refuse to start once disposed, so that no timer or check runs on a disposed object.

Category: bug

## What

UpdateChecker.StartAsync called after Dispose would still create a timer and start a check. Make it a no-op after Dispose, and check PushWatcher for the same StopAsync/Dispose ordering issue that job 252 fixed in UpdateChecker.

## Why

Review-code (job 252) noted the gap; job 252 kept to StopAsync and Dispose. A late start after shutdown could throw or leak a timer, the same kind of race that broke develop CI.

## Notes

Proposed by job 252-updatechecker-stop-dispose (`factory/jobs/252-updatechecker-stop-dispose/`).
