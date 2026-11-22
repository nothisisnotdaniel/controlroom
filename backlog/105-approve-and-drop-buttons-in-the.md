# 105 Approve and Drop buttons in the story view of a [?] item

As the user, I want Approve and Drop buttons in the detail view of a proposed item, so that I can decide on it right after reading its story instead of going back to the inbox to find it again.

## What

In the story view (#story/<project>/<n>), when the item's backlog line is a [?] item, show Approve and Drop in the story bar, next to Edit story and Add note. They send the same approve and drop changes as the inbox (POST /api/backlog/change with the backlog version and line from /api/backlogs, through sendChange and its fixed error texts), Drop only after a confirm. After Approve the view stays and shows the item as open (no buttons); after Drop it goes back to where the user came from (or the inbox) because the story file is gone. Items that are not [?] show no buttons. The inbox count refreshes after either.

## Why

Today Approve and Drop exist only as a row in the Inbox. Reading a story first and then deciding means going back and finding the same row again, which is slow on the iPhone and easy to get wrong when the inbox has many items.
