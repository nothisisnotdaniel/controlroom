# 161 Clear not_develop error text for factory backlog edits

As the user, I want a failed factory backlog edit to tell me that the factory's develop folder is not its develop worktree, so that I know what to fix instead of seeing the vague project text.

Category: ux

## What

When POST /api/backlog/change for project factory returns 409 not_develop, edit.js shows the factory-specific fixed text (like app.js notDevelop: the factory's develop folder is not its develop worktree; move it away or set FACTORY_DEVELOP_DIR, then reload) instead of 'Not on develop. Nothing was changed.'. Projects keep today's text. Fixed texts only, no server change. Add a test for both texts.

## Why

The existing not_develop text in src/RoomKit/wwwroot/edit.js:48 fits a project checkout on another branch, but for the factory it is vague (ux review nit in job 402).

## Notes

From job 402, review-ux nit 1.
