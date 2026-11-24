# 159 Show which work items a collaborator is working on

As one of two people running the factory, I want ControlRoom to show on a backlog item that a job on another machine is working on it, so that we can see what the other person is building and don't pick the same item.

Category: feature

## What

After factory#54, job.py start claims each backlog item of a job on the project's origin: one ref `refs/factory/claims/<item>` per item, a commit whose message holds the Job, Machine, Project and Claimed lines. ControlRoom's server reads them for every project and for the factory: `git ls-remote origin refs/factory/claims/*`, and for each new or changed sha `git fetch --no-tags origin +refs/factory/claims/*:refs/factory/remote-claims/*` and `git log -1 --format=%B <ref>`, all with fixed argument lists and a time limit, at most once a minute per project, in the background and never inside a request. Only refs whose last part is a backlog number of that project count. Expose the result in the backlog response (item, job, machine, claimed time). The web app marks each claimed item with "In progress on <machine> since <time>"; a claim from this machine's own job shows as today. When origin is unreachable or there is no origin, show nothing and keep the last known state for at most 10 minutes, with its age. Update the safety rules in ControlRoom/CLAUDE.md: this is the server's first network call, read-only, to the project's own origin only, never a URL from a request, never a push. Add tests with a local bare repo as origin.

## Why

Two people run the factory on two machines. ControlRoom only reads this machine's job records, so a job running on the other machine is invisible until it merges, and both could plan or start the same item.

## Notes

Asked for on 24 Nov 2026. Needs the item claims from factory#54 (which replaced factory#53). High risk: a new network call and a change to the server's safety rules.
