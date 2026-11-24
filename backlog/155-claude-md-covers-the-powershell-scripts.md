# 155 CLAUDE.md covers the PowerShell scripts' network calls

As a reviewer, I want CLAUDE.md to list every outbound call the scripts make, so that I can check new changes against it.

Category: docs

## What

Extend the Scripts and outbound-call rules in `CLAUDE.md` to cover `scripts/launch-agent.ps1` and `scripts/updater.ps1`. `updater.ps1` reads `releases?per_page=100&page=N` (up to 10 pages) on both channels and follows up to 6 redirects to `github.com` (same path only), `release-assets.githubusercontent.com` and `objects.githubusercontent.com`, using `HttpWebRequest` with the system proxy.

## Why

The rule lists only the shell scripts, so the Windows updater's network access is undocumented and reviews can't check it. Found in the review of PR #3.

## Notes

From PR #3 review, nit 2.
