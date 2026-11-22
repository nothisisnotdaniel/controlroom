# 053 Keep focus on the channel select after saving

As a keyboard user of the ControlRoom board, I want focus to stay on the update-channel select after I change it, so that I don't lose my place while the setting saves.

Category: ux

## What

In board.js renderUpdateChannel the select is disabled during the POST, and some browsers drop focus then. Restore focus after the request in a way that does not open the native picker on iOS (calling focus() on a select can open it there).

## Why

review-ux 1 (job 234): losing focus makes the control harder to use by keyboard and screen reader.

## Notes

Proposed by job 234-develop-prereleases-channel (`factory/jobs/234-develop-prereleases-channel/`).
