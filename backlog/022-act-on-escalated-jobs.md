# 022 Act on escalated jobs

As the user, I want to approve or send back an escalated job from the phone, so that a job waiting for me doesn't stall until I'm at the Mac.

## What

A `needs-human` job shows its note and decision, plus Approve and Rework (with a note). The server never runs `claude` or the factory; instead it records the request in a fixed place the factory reads (for example `factory/jobs/<id>/human.json` with the action, note and time), and the next `/factory` session or `/factory auto` carries it out as `/factory approve <id>` or `/factory rework <id> <note>`. The spec designs the request file, how the factory picks it up and how it is cleared, and updates the write rules in CLAUDE.md. Full path, high risk.

## Why

An escalation is the one point where a run truly needs me, and today it waits until I open a session. Leaving the decision as a request that the factory carries out keeps the server from running anything, which is what makes #12 risky.
