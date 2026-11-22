# 107 Collapsible backlog groups

As the user, I want every group in a backlog (Open, Waiting for approval, tasks, Done, Waiting) to fold and unfold, so that I can hide what I don't need right now.

Category: feature

## What

Make every group of a project section collapsible like Done already is (details/summary with the count in the heading). Done stays closed by default, the others open. The open or closed state of each group is kept per device (localStorage, try/catch) and survives live refreshes and reloads. Edit mode, forms and focus inside a group work as today.

## Why

Long Open and approval lists push the rest of the page far down; today only Done can be folded.
