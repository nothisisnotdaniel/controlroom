# 159 Show which work items a collaborator is working on

As one of two people running the factory, I want ControlRoom to show on a backlog item that a job on another machine is working on it, so that we can see what the other person is building and don't pick the same item.

Category: feature

## What

Since job 399, job.py start puts a claim on each project's origin: the ref refs/factory/claim, a commit whose message holds Job, Machine, Project, Claimed and (after factory#53) Items lines. ControlRoom's server reads it for every project and for the factory: git ls-remote origin refs/factory/claim, and when its sha changed, git fetch --no-tags origin +refs/factory/claim:refs/factory/remote-claim and git log -1 --format=%B refs/factory/remote-claim, all with fixed argument lists and a time limit, at most once a minute per project, in the background and never inside a request. Expose the result in the backlog response (job, machine, claimed time, items). The web app marks each claimed item with "In progress on <machine> since <time>" and the project with the running job; a claim from this machine's own job shows as today. When origin is unreachable or there is no origin, show nothing and keep the last known state for at most 10 minutes, with its age. Update the safety rules in ControlRoom/CLAUDE.md: this is the server's first network call, read-only, to the project's own origin only, never a URL from a request, never a push. Add tests with a local bare repo as origin.

## Why

Two people run the factory on two machines. ControlRoom only reads this machine's job records, so a job running on the other machine is invisible until it merges, and both could plan or start the same item.

## Notes

Asked for on 24 Nov 2026. Needs the Items line from factory#53; without it only the project-level claim (job and machine) can be shown. High risk: a new network call and a change to the server's safety rules.
