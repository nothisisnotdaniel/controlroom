# 040 Hint to run status after installing the LaunchAgent

As the ControlRoom user, I want the install command to tell me how to check that the server is running, so that I know the next step without opening the README.

## What

After a successful `scripts/launch-agent.sh install`, print one more fixed line such as `Check it with: scripts/launch-agent.sh status`. Update the test that pins the install output and the README example.

## Why

The UX review of job 186 noted that install prints only the plist and log paths, so a user may not know that a `status` command exists to confirm the server came up.

## Notes

Proposed by job 186-start-at-login (review-ux round 1, nit 2).

Proposed by job 186-start-at-login (`factory/jobs/186-start-at-login/`).
