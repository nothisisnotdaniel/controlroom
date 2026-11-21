# 127 Bottom tab bar with icons

As the user, I want the tabs in a bar that stays at the bottom of the screen, with an icon and a tiny label for each, so that every view is always in reach of my thumb.

Category: ux

## What

Move the main navigation tabs into a bar fixed to the bottom of the viewport, above the content, on every view and at every width. Each tab shows an icon with a tiny label under it (like an iOS tab bar), all tabs fit in one row at phone width (375px) without scrolling, and the current tab is marked. The bar respects the iPhone safe area (env(safe-area-inset-bottom)) and works in light and dark mode. Every view gets bottom padding at least the bar's height plus the safe area, so the last item of any list can be scrolled fully into view above the bar; no content sits under the bar at the end of a scroll. Icons are inline SVG or CSS (no new package or font), each tab keeps an accessible name and stays keyboard reachable. Counts or badges the tabs show today (for example the inbox count) stay visible. Tests check the bar on every view, the padding, and that the last item is reachable.

## Why

Today the tabs sit at the top, so on the iPhone reaching another view means stretching to the top of the screen. Text labels also take too much width for all tabs to fit in one row.

## Notes

The user chose icons with a tiny label under each (not icons only).
