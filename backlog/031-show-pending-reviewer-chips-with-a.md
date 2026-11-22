# 031 Show pending reviewer chips with a text marker

As the ControlRoom user on the iPhone, I want a pending reviewer chip to say it is pending in text, so that I can tell pending from other states without relying on colour.

Category: ui

## What

In board.js roundRow, give pending chips a visible text marker (for example `ux …` or `ux · pending`) in addition to the colour, in both light and dark mode. Update the WebAppTests assertions.

## Why

The UI review of job 183 found that pending and unknown chips differ only by colour, and the full state sits in a `title` that iPhone doesn't show, so colour-blind users and phone users can't tell them apart.

## Notes

Proposed by job 183-factory-dashboard (review-ui round 1 nit 2).

Proposed by job 183-factory-dashboard (`factory/jobs/183-factory-dashboard/`).
