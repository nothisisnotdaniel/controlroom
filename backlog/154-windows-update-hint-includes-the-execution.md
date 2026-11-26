# 154 Windows update hint includes the execution-policy step

As a Windows user, I want the update hint to give me every step, so that the command runs in a fresh PowerShell session.

Category: ux

## What

In `src/ControlRoom.Server/wwwroot/board.js` (around line 667) the Windows update hint says to run the command in PowerShell from the extracted folder. Add the `Set-ExecutionPolicy -Scope Process RemoteSigned` step first, as the README says (or point to the README). Move the string into `BoardTexts` like the texts around it.

## Why

A fresh PowerShell session refuses to run the script without that step, so the hint as it stands fails. Found in the review of PR #3.

## Notes

From PR #3 review, nit 1.
