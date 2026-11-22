# 139 Phone start spike

As the user, I want to know what can safely start a factory run on the Mac when I ask from the phone, so that #12 is built on a plan that works on this Mac.

Category: feature

## What

Find out on this Mac which way can start `/factory auto --project <P>` on request from ControlRoom: the Claude desktop app (deep link, AppleScript, its session tools), Remote Control, or the `claude` CLI if it were installed. For each: whether it runs with the app tools a /factory auto run needs (AskUserQuestion cards, the usage check), which permission mode it would need unattended, how a run is stopped, and how ControlRoom would see that it is running. End with one concrete plan for #12 (or the finding that Remote Control is enough and #12 should be dropped).

## Why

Job 374 specced #12 as a LaunchAgent running `claude -p`, but there is no `claude` CLI on this Mac, a headless run has no app cards or usage check, and it would need an unattended permission mode. Building that would not work or would run work on the Mac without the limits #12 asks for.

## Notes

Spec of job 374 (factory/jobs/374-phone-start-runs/spec.md) has the first design and its open questions.
