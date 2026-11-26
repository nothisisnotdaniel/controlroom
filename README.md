# ControlRoom

ControlRoom is a control room for the software factory. A small server on your Mac or Windows PC reads the factory root
(the folder that holds `ControlRoom/`) and serves one web app. The app shows every project's backlog and the
factory's jobs, and lets you edit backlogs. You use the same app in a browser on the Mac and on the iPhone,
where you can add it to the home screen.

The server listens on `127.0.0.1:8100` only. To reach it from the iPhone, use `tailscale serve`, which gives
you HTTPS on your own tailnet and forwards to the local port.

The server answers only requests addressed to `127.0.0.1:8100`, `localhost:8100` or the tailnet name you give
it, so a web page can't reach it through a borrowed domain name (DNS rebinding). Any other address gets
`421 {"error":"bad_host"}`. Before the iPhone works through Tailscale, start the server with your Mac's tailnet
name, the host part of the `https://<mac>.<tailnet>.ts.net` address `tailscale serve` shows (no port):

```sh
dotnet run --project src/ControlRoom.Server -- --TailnetHost mac.tail1234.ts.net
```

When another app already uses port 443 of that name, serve this one on another HTTPS port
(`tailscale serve --bg --https=8443 http://127.0.0.1:8100`) and add `--TailnetPort 8443`. The name is then also
accepted with exactly that port.

## Windows x64

The x64 release includes the .NET runtime. Install Git for Windows for backlog edits and GitHub CLI for release downloads and attestation verification. If this is your first time using GitHub CLI, run `gh auth login` in PowerShell. Then download the latest stable release into an empty directory. Set `$tag` to an offered prerelease tag to install that version.

```powershell
$repo = 'nothisisnotdaniel/controlroom'
$tag = gh release view --repo $repo --json tagName --jq .tagName
if ($LASTEXITCODE -ne 0) { throw 'Could not find the release.' }
$zip = "controlroom-$($tag.Substring(1))-win-x64.zip"
gh release download $tag --repo $repo --pattern $zip --pattern SHA256SUMS
if ($LASTEXITCODE -ne 0) { throw 'Download failed.' }
gh attestation verify $zip --repo $repo
if ($LASTEXITCODE -ne 0) { throw 'Provenance verification failed.' }
$pattern = '^(\S+)\s+\*?' + [regex]::Escape($zip) + '$'
$hashes = @(Get-Content -LiteralPath SHA256SUMS | ForEach-Object { if ($_ -cmatch $pattern) { $Matches[1] } })
if ($hashes.Count -ne 1 -or $hashes[0] -ne (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash) { throw 'ZIP checksum failed.' }
Unblock-File -LiteralPath $zip
Expand-Archive -LiteralPath $zip -DestinationPath ".\controlroom-$tag"
Set-Location -LiteralPath ".\controlroom-$tag"
.\controlroom.exe version
$root = 'C:\My Factory'
.\controlroom.exe --FactoryRoot $root
```

Use the physical directory containing your projects for `$root`. If you reach it through a junction or symbolic link, use the target directory for installation. For automatic updates, keep the job records on a local filesystem such as NTFS where Windows can coordinate reads with the factory worker.

Open http://127.0.0.1:8100. From a second PowerShell window in the extracted folder, run `.\controlroom.exe token` and enter the token in the app. The private file is `%LOCALAPPDATA%\ControlRoom\access-token`. Use `.\controlroom.exe token --new` to rotate it. For a custom token path, pass the same `--TokenFile <path>` to the foreground server and the token command, using a dedicated private directory.

Press Ctrl+C in the foreground server window before installing the sign-in task. From the extracted folder, run these commands as your usual Windows user. The execution policy setting applies to this PowerShell session.

```powershell
Set-ExecutionPolicy -Scope Process RemoteSigned
.\launch-agent.ps1 install -Release $tag -FactoryRoot $root
```

For Tailscale, append `-TailnetHost desk.example.ts.net` with your own host. Install starts the server immediately and creates a sign-in task and a recurring updater task for your account. Installed code lives in `%LOCALAPPDATA%\ControlRoom\server`. To receive verified updates, open **Jobs**, enable **Install updates automatically**, choose **Stable** or **Develop (prereleases)** and select the check interval. The updater waits for active factory jobs to finish. The Jobs page also shows a manual update command to run from the extracted folder.

For a manual update, status check or uninstall in a new PowerShell session, open the extracted folder and run `Set-ExecutionPolicy -Scope Process RemoteSigned` again before the script.

```powershell
.\launch-agent.ps1 status
```

To uninstall, run the following command.

```powershell
.\launch-agent.ps1 uninstall
```

Uninstall removes both tasks and the installed code. Your token and update settings stay in the data directory.

## Run it

From `ControlRoom/`:

```sh
dotnet run --project src/ControlRoom.Server
```

Then open http://127.0.0.1:8100. The factory root is the parent of the folder that holds `ControlRoom.slnx`.
To use another one, add `-- --FactoryRoot <path>`.

## Get the access token

Every API call needs the access token. The app asks for it once and remembers it.

```sh
dotnet run --project src/ControlRoom.Server -- token        # prints the token (creates it when missing)
dotnet run --project src/ControlRoom.Server -- token --new  # replaces it
```

The token is stored in `~/Library/Application Support/ControlRoom/access-token`.

## See the version

```sh
dotnet run --project src/ControlRoom.Server -- version   # prints "ControlRoom dev" for a build from the sources
```

A release build prints its own version, for example `~/Library/Application Support/ControlRoom/server/ControlRoom.Server version`
prints `ControlRoom 1.2.3`. The app shows the same line at the foot of the Jobs view; it reads it from
`GET /api/version` (with the access token), which returns `{"version":"1.2.3"}`.

## Start it at login

`scripts/launch-agent.sh` installs a macOS LaunchAgent. The server then starts when you log in and starts
again if it stops.

```sh
scripts/launch-agent.sh install    # publishes the server and starts it
scripts/launch-agent.sh install --tailnet-host mac.tail1234.ts.net  # the same, for use through Tailscale
scripts/launch-agent.sh status     # shows installed: yes|no, running: yes|no and the log path
scripts/launch-agent.sh uninstall  # stops it and removes the agent and the published server
```

Run `install` again after you pull changes. It publishes the server again and restarts it.

### From a release (no .NET SDK needed)

```sh
scripts/launch-agent.sh install --release          # the latest release (on the develop channel: the newest develop build)
scripts/launch-agent.sh install --release v1.2.3   # that release
scripts/launch-agent.sh install --release --tailnet-host mac.tail1234.ts.net  # either one, for Tailscale
```

`install --release [vX.Y.Z]` downloads `controlroom-<version>-osx-arm64.tar.gz` (Apple silicon) and `SHA256SUMS`
from https://github.com/nothisisnotdaniel/controlroom/releases over HTTPS, checks the checksum, every entry of the archive
and its size (at most 200 MiB to download and 512 MiB unpacked), and only then replaces the server and restarts it. If anything doesn't check out, nothing is changed.
Run it again to update. A later plain `install` goes back to publishing from the sources.

It also installs the updater. When "Install updates automatically" is on (at the foot of the Jobs page), it checks
for a newer release about a minute after install or login, then every 30 minutes. Under "Check for updates" next to
the channel you can pick a longer interval (every hour, 2, 6 or 12 hours); it counts from the next check, with no
reinstall. While a factory job runs it never swaps
the server: it checks again every 15 min (up to 5 h 45 min) and installs the update once the job is done. Each check
adds a line to `~/Library/Logs/ControlRoom/updater.log` (launchd starts it every 15 min, but a run that is not due
yet ends at once and logs nothing).

Every release carries its own `updater.sh`. When the updater installs a release, it also installs that copy of itself
and rewrites its LaunchAgent plist, so updater fixes arrive without a reinstall. The new script runs from the next
check, the new plist counts from the next login or install. The old ones stay as `updater.sh.previous` and
`updater.plist.previous` in `~/Library/Application Support/ControlRoom/`, and a rollback puts them back. If the
release's copy is missing or doesn't check out, the updater keeps its own and logs `updater_kept`.

Every release also has a build-provenance attestation. To check a downloaded tarball by hand:

```sh
gh attestation verify controlroom-1.2.3-osx-arm64.tar.gz --repo nothisisnotdaniel/controlroom
```

## Releases

`.github/workflows/release.yml` makes the releases. Merging a pull request from `develop` into `main` titled
`release vX.Y.Z` (a prerelease such as `release v1.2.3-rc.1` is allowed) runs the tests on Windows x64 and
Apple silicon macOS. A first `version` job works out the version; then the tests run in two parts per OS before the
builds, all four parts in parallel. `.github/test-part-1.txt` lists the test classes of part 1; part 2 runs every other
test, so each test runs exactly once per OS and a new test class needs no edit to the list. The guard
(`TestPartsGuardTests`, in each test assembly) fails when a test would land in no part or in both, or when a listed
name matches no test. Then the Windows and macOS builds run in parallel after `version`, each in its own job once its
OS's tests have passed. CI checks the version of each executable and serves the packaged web assets
from the extracted Windows ZIP. Each build job reports the SHA-256 of the files it built. The final `release` job
checks the downloaded files' hashes against the hashes the build jobs reported, then sums, attests and publishes
them. The release on the merge commit includes the macOS tarball, Windows ZIP and RoomKit
package, with a SHA-256 digest and build attestation for each. GitHub generates the release notes from the merged pull requests.

Every push to `develop` (except one that changes only `backlog.md` or `backlog/`) also makes a prerelease
`vX.Y.(Z+1)-develop.<run>`, one patch above the latest stable release, with the same checks, files and
attestation. A stable release then deletes the develop prereleases (and their tags) at or below its version.

To try those builds, choose **Develop (prereleases)** under "Update channel" at the foot of the Jobs page (under
"Install updates automatically"). The update notice, the updater and `install --release` without a tag then follow the
newest develop build or release; **Stable** (the default) ignores prereleases. A develop build can also be installed by hand:

```sh
scripts/launch-agent.sh install --release v1.2.4-develop.7
```

- Published server: `~/Library/Application Support/ControlRoom/server/`
- Agent: `~/Library/LaunchAgents/local.controlroom.server.plist`
- Log (output and errors): `~/Library/Logs/ControlRoom/server.log`

To follow the log:

```sh
tail -f ~/Library/Logs/ControlRoom/server.log
```

`uninstall` keeps the log and the access token.

## RoomKit

The parts of ControlRoom that are not about this factory's job format (sign-in, the `Host` check, loopback
listening, the web app shell, push, updates, backlogs, search, history and the Health view) live in the shared
library `src/RoomKit`. ControlRoom (`src/ControlRoom.Server`) is a thin app on top of it: its names, its job format
(`state.json`), its tabs and `wwwroot/room.js`. Every release also carries `RoomKit.X.Y.Z.nupkg`, listed in
`SHA256SUMS` and attested like the tarball, so another factory's app can use the same core. See
[docs/roomkit.md](docs/roomkit.md) for the plug-in API, the scripts and the local folder feed.

## Signing

Release builds can be signed with one self-signed code-signing certificate, so every build has the same identity
and macOS keeps the permission you grant once: automatic updates then finish without anyone clicking Allow. There is
no Apple Developer ID and no notarization. Signing is off until `signing-cert.sha1` is committed at the repo root;
until then builds are only ad-hoc signed (the workflow warns) and nothing checks signatures. Once it is committed,
the release workflow must sign every build (steps `Sign` and `Verify signature`), and `install --release` and the
updater refuse any build whose native files (the `ControlRoom.Server` executable, the `*.dylib` libraries and
`createdump`) are not signed with that certificate. This pins the app's identity; it is not tamper protection for
the whole build, because the managed `*.dll` files are not covered by any signature (the checksum and the
build-provenance attestation are what vouch for the tarball). A `signing-cert.sha1` that is present but not
exactly 40 hex characters (plus at most one line break) always fails; it never turns signing off.
If the `Sign` step fails with "The SIGNING_CERT_P12 secret holds no code-signing identity with the fingerprint in
signing-cert.sha1", the p12 secret is a different certificate or was exported without its private key: export it
again (step 2) or fix `signing-cert.sha1` (step 3).

One-time setup:

1. Create the certificate in Keychain Access: **Keychain Access > Certificate Assistant > Create a Certificate…**,
   name `ControlRoom Release`, Identity Type **Self-Signed Root**, Certificate Type **Code Signing**, and tick
   "Let me override defaults" to set the validity to about 10 years (3650 days).
2. Export it with its private key as a p12 (select the certificate under **My Certificates**, **File > Export
   Items…**, format Personal Information Exchange, with a strong password), then store both as repository secrets:

   ```sh
   base64 -i ControlRoom-Release.p12 | gh secret set SIGNING_CERT_P12 --repo nothisisnotdaniel/controlroom
   gh secret set SIGNING_CERT_PASSWORD --repo nothisisnotdaniel/controlroom   # paste the export password
   ```

3. Read the certificate's SHA-1 fingerprint and commit it as `signing-cert.sha1` on `develop`:

   ```sh
   security find-certificate -c "ControlRoom Release" -Z | awk '/SHA-1 hash:/ { print $3 }' > signing-cert.sha1
   git add signing-cert.sha1 && git commit -m "Sign release builds" && git push
   ```

   Then delete the exported p12 (`rm ControlRoom-Release.p12`); the key stays only in your keychain and the secret.
4. Once the first signed release is out, run `scripts/launch-agent.sh install --release` once, so the updater gets the
   fingerprint (it reads its copy `~/Library/Application Support/ControlRoom/signing-cert.sha1`).
5. Grant the permission once: in **System Settings > Privacy & Security > Files and Folders**, or click **Allow** on the
   first signed start. Later signed builds keep it.

A new certificate changes the identity, so it needs one more Allow. Releases made before signing was turned on are
refused by `install --release` once it is on.

