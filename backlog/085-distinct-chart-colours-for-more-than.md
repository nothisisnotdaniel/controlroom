# 085 Distinct chart colours for more than six modes

As the operator, I want every mode in the History chart to have its own colour, so that I can tell modes apart without reading the legend.

## What

history.js historyColour cycles six colours, so a seventh mode repeats the first. Add enough colours (readable in light and dark mode) or another cue such as a pattern for modes past six.

## Why

Review-ux (job 264) noted the repeat; the legend and aria-label still name each mode, so it is a nicety, not a fault.

## Notes

Proposed by job 264-costs-and-history (`factory/jobs/264-costs-and-history/`).
