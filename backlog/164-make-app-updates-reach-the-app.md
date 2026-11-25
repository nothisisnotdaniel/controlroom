# 164 Make app updates reach the app: bump the shell cache and load app files network-first

Category: bug

## What

After an update the web app keeps running old files from the service worker's shell cache. The cache name (`controlroom-shell-` + CacheVersion in room.js, still v23) didn't change when job 407 changed board.js, and sw.js serves the shell stale-while-revalidate. So the old board.js still rejects the new uuid-suffixed job ids, and clicking a job shows "Can't load this job" without asking the server, even after the app is closed and reopened. Bump CacheVersion. Serve the app shell network-first, with the cache only as a fallback when the server can't be reached. Delete old shell caches on activate. Make sure a new service worker takes over at once (skipWaiting and clients.claim), and add a test that fails when a shell file changes without a CacheVersion bump, or derive the version from the shell files' content so it can't be forgotten.

## Why

Users see a broken job page after every update that changes app files until the cache happens to refresh, and closing the app doesn't help. An update must reach the app the next time it loads.
