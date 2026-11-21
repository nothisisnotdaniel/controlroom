# 128 Approve or Drop goes back from the story view

As the user, I want Approve and Drop in the story view to close the view and take me back to where I came from, so that I can go on deciding the next item at once.

Category: ux

## What

In the story view of a [?] item, after a successful Approve the view closes and goes back to where the user came from (the inbox, the backlog list, or wherever the story was opened from; the inbox when there is no previous view), just as Drop already does (#105). A failed Approve or Drop stays in the story view and shows the error as today. The inbox count refreshes after either. Tests cover Approve and Drop from the inbox and from the backlog list, and a failed Approve.

## Why

#105 kept the story view open after Approve. When working through proposed items, the next step is always the next item, so staying in the view only costs an extra tap back.
