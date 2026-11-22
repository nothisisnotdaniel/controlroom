# 130 Check the bottom tab bar on the iPhone

As the user, I want the bottom tab bar checked once on a real iPhone, so that I know it fits and reads well where I use it most.

Category: test

## What

On the iPhone (PWA from the home screen) and in a 375px-wide browser window: all tabs fit in one row with their labels, the bar sits above the home indicator and is not covered by it, it reads well in light and dark mode, a two-digit count (Inbox or Tasks) is not clipped, and the last item of a long list (Backlogs, History) scrolls fully into view above the bar. Note anything off as a new backlog item.

## Why

The tests read the CSS and markup as text; nobody has looked at the bar on a screen yet (#127, job 357).
