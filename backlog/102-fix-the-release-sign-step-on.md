# 102 Fix the release Sign step on the macOS runner

As the user, I want the release workflow to sign builds with the certificate I set up, so that develop and main releases build again and the updater can install them without an Allow prompt.

## What

Since signing-cert.sha1 was committed (2b6c660), every Release run fails in the Sign step with "error: The specified item could not be found in the keychain." (run 44803707511, runner image macos-15-arm64, twice, with a p12 that does contain its private key). Likely cause: codesign on the runner only finds identities in keychains on the user search list, and the step creates its temporary keychain without adding it. Add the temporary keychain to the user search list (keeping the existing ones) right after unlocking it, and remove it from the list again in "Remove signing keychain", which runs even when a step fails. Make the step fail with a clear message if the pinned identity is not in the keychain after import (for example security find-identity -p codesigning on that keychain), so a wrong or key-less p12 is told apart from a codesign failure. No secret value, keychain password or key material may reach the log (the repo is public).

## Why

Signing is turned on but no release can be built, so neither develop nor main ships, and the Mac stays on develop.18 with the updater rolling back unsigned builds.

## Notes

Verification needs a real run: after the merge the factory CI wait watches the Release run for the merge commit; the Sign and Verify signature steps must pass and v0.1.1-develop.N must be published. Then the user runs launch-agent.sh install --release once (#101 step 4).
