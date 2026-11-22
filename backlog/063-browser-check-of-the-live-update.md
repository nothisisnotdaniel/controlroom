# 063 Browser check of the live-update notice

As the operator, I want the live-update notice checked in a real browser, so that its layout and contrast are known to work in light and dark mode.

Category: test

## What

Open the board in a browser, trigger a live change and check the .live-notice layout (720px cap, card background, border), its dark-mode contrast and that screen readers announce it once.

## Why

Review-code, review-ui and review-ux (job 249) could only read the CSS and JS; the layout and contrast were not seen or measured.

## Notes

Proposed by job 249-live-board-updates (`factory/jobs/249-live-board-updates/`).
