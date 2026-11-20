# 106 Project picker on Backlogs

As the user, I want to choose one project in a dropdown on Backlogs, so that I see one backlog at a time instead of scrolling through all of them.

Category: feature

## What

Add a Project select at the top of the Backlogs view listing every readable project (ProjectFinder order). Only the chosen project's section is shown; there is no All entry. The choice is kept per device in localStorage (`controlroom-backlog-project`, wrapped in try/catch) and falls back to the first project when the stored one is gone. Links to #story/<project>/<n> and the Next/Waits-for links still work; following a link into another project switches the picker. Live refresh keeps the chosen project.

## Why

With several projects the Backlogs page is one long list, which is slow to scan on the iPhone and makes edits in the wrong project easy.
