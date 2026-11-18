# 045 Release builds on GitHub

As the user, I want every ControlRoom release to come as a ready-built download on GitHub, so that I can install and run it without building from the sources.

## What

Add a GitHub Actions workflow (`.github/workflows/release.yml`) that runs when a `release vX.Y.Z` pull request from `develop` is merged into `main` (the PR `/factory release` opens): it runs `dotnet test`, publishes the server self-contained for `osx-arm64` with the version stamped in (`-p:Version=X.Y.Z`), packs it with `wwwroot` as `controlroom-X.Y.Z-osx-arm64.tar.gz` plus a `SHA256SUMS` file, tags `vX.Y.Z` and creates the GitHub Release with both files. Actions are pinned by commit SHA, the job has only the permissions it needs, and the spec says whether to add a build-provenance attestation. The server knows its own version (`dotnet run` from sources says `dev`) and shows it in the app; `GET /api/version` returns it. `scripts/launch-agent.sh install --release [vX.Y.Z]` downloads the latest (or the named) release from the public repo with `curl` over https, checks it against `SHA256SUMS`, installs it into the published server folder and restarts the agent, without needing the .NET SDK; plain `install` still publishes from the sources. README and CLAUDE.md say how. Full path.

## Why

Today the only way to run ControlRoom is from the sources with the .NET SDK, so every update means pulling and publishing by hand. A release download is also the base for update notices and automatic updates.
