# 028 Close the symlink race when reading story files

As the ControlRoom owner, I want story files to be opened without a gap between the symlink check and the open, so that a link swapped in at the wrong moment can't make the server read a file outside the project's backlog folder.

## What

Replace the check-then-open on the `backlog` folder and story file with an open that refuses symlinks atomically (for example `open` with `O_NOFOLLOW|O_DIRECTORY` and `openat` through native interop on macOS/Linux, and the matching Windows API). Keep the current error responses and add tests for a symlinked folder and a symlinked file.

## Why

The security review found a TOCTOU race: the folder is checked for symlinks and then opened separately, so a symlink swapped in between could expose another file. The risk is low on a single-user Mac, but the fix needs native code, so it was left out of job 181.

## Notes

Proposed by job 181-numbered-backlog-stories (review-security round 1 nit 3, round 2 nit 1).

Proposed by job 181-numbered-backlog-stories (`factory/jobs/181-numbered-backlog-stories/`).
