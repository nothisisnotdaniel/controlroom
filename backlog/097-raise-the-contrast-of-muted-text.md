# 097 Raise the contrast of muted text

As the user, I want the muted grey text in ControlRoom to be easy to read in light and dark mode, so that notes and hints are not hard to see on the phone.

Category: ui

## What

Change the theme's `--muted` colour in `wwwroot/app.css` for light and dark mode so that muted text on the page background reaches at least 4.5:1 contrast, check every place that uses it still looks right, and bump the service worker cache version.

## Why

The muted colour is shared app-wide and the new waiting-edits note uses it; its contrast is low, which makes hints hard to read outdoors on the iPhone (UX review nit 2 of job 299).

## Notes

Proposed by job 299-edit-backlog-during-jobs (`factory/jobs/299-edit-backlog-during-jobs/`).

Done: the muted-text colour in app.css already passes the contrast check (backlog audit, 2026-11-26).
