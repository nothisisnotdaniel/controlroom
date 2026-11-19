# 001 Installable web app

As the user, I want to install ControlRoom on my iPhone's home screen, so that it opens like an app, full screen, in one tap.

## What

A web app manifest, icons and a service worker that caches only the app shell (never backlog or job data), so the app can be added to the iPhone home screen and opens full screen; when the server can't be reached it says so instead of showing old data.

## Why

A web page in Safari is easy to lose among tabs and wastes screen space on browser chrome. Caching only the app shell keeps it fast to open while making sure I never mistake old backlog or job data for the current state.
