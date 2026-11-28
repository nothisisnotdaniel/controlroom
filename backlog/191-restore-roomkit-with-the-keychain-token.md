# 191 Restore RoomKit with the Keychain token in agent shells

As the ControlRoom owner, I want builders and reviewers to restore RoomKit from GitHub Packages on this Mac, so that a RoomKit bump no longer needs a hand-seeded NuGet cache.

Category: ci

## What

Add `scripts/restore.sh`, modelled on PRWatch's: it reads the classic PAT (only `read:packages`) from the Keychain item `roomkit-nuget` (account `$USER`), sets `NuGetPackageSourceCredentials_github=Username=nothisisnotdaniel;Password=<token>` for that one `dotnet restore` only, never prints the token, and exits 1 with a clear message when the item is missing or the read is denied. In ControlRoom/CLAUDE.md, next to the restore-credential lines, add: before the first `dotnet build` or `dotnet test` on a Mac, and after a RoomKit bump, run `scripts/restore.sh` once. Update README "RoomKit" to match. A test checks the script never echoes the token and never exports it beyond the restore.

## Why

#186 set up the token and an export in ~/.zshrc, but Claude's shells (the factory builders included) don't source ~/.zshrc, so they still get a 401 from the feed and fall back to seeding the cache from the release file, as in jobs 472 and 477. PRWatch already solves this with its own restore script and keeps the token out of the global environment.

## Notes

The ~/.zshrc export stays for the user's own terminals; whether to drop it in favour of the script is the user's call.
