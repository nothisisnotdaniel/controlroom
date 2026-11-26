# 157 Windows updater errors say what to do next

As a Windows user, I want each updater error to tell me what to try, so that I can fix a failed update myself.

Category: ux

## What

In `scripts/updater.ps1`, give messages like `ZIP digest refused.`, `Release verification failed.`, `Download failed.` and `Invalid response.` a short next step, for example "Check the network and run the install again". Keep tokens and URLs with secrets out of the messages.

## Why

The current messages name the failure but not the way out. Found in the review of PR #3.

## Notes

From PR #3 review, nit 4.
