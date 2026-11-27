# 186 Set up the GitHub Packages login for RoomKit on this Mac

As the ControlRoom owner, I want a working GitHub Packages login on this Mac, so that RoomKit restores from the feed instead of a hand-seeded NuGet cache.

Category: ci

## What

Create a classic GitHub token with only read:packages. Add `export NuGetPackageSourceCredentials_github='Username=nothisisnotdaniel;Password=<token>'` to ~/.zshrc and open a new terminal. Check that the feed answers 200: `curl -s -o /dev/null -w '%{http_code}\n' -u 'nothisisnotdaniel:<token>' https://nuget.pkg.github.com/nothisisnotdaniel/index.json`. Then run `dotnet restore --no-cache --force` in ControlRoom/. Tick this line when the restore passes.

## Why

Job 472 got a 401 from GitHub Packages for RoomKit 0.2.0, because this Mac has neither the env var nor a github NuGet source; the builder seeded the cache from the release file instead. The next RoomKit bump would hit the same 401.
