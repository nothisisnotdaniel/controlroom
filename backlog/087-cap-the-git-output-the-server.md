# 087 Cap the git output the server reads

As the operator, I want the server to stop reading git output past a fixed size, so that a huge untracked tree can't make it use a lot of memory.

Category: security

## What

ProjectGit.Run reads git's whole stdout into memory; DoctorCheck.Uncommitted runs git status --porcelain --untracked-files=all over factory/jobs. Add a byte cap to ProjectGit.Run (kill the process and report the result as cut off past the cap), show a cut-off finding in the Health view, and add tests.

## Why

Review-security (job 266) noted that the output has no upper bound; a stray large folder could make every /api/doctor call costly.

## Notes

Proposed by job 266-doctor-view (`factory/jobs/266-doctor-view/`).
