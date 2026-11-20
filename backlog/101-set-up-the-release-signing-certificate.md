# 101 Set up the release signing certificate

As the user, I want to create the release signing certificate and hand it to the release workflow, so that ControlRoom builds keep one identity and automatic updates no longer stop at a macOS Allow prompt.

## What

Follow ControlRoom/README.md, section Signing (steps 1 to 4): create the self-signed Code Signing certificate `ControlRoom Release` in Keychain Access and export it as `ControlRoom-Release.p12` with a password; store it with `base64 -i ControlRoom-Release.p12 | gh secret set SIGNING_CERT_P12 --repo nothisisnotdaniel/controlroom` and `gh secret set SIGNING_CERT_PASSWORD --repo nothisisnotdaniel/controlroom`; commit its SHA-1 fingerprint as `signing-cert.sha1` on develop; after the first signed release, run `scripts/launch-agent.sh install --release --tailnet-host mac-mini-von-daniel.tailb28929.ts.net` once and click Allow on the first signed start.

## Why

Job 301 (#100) built signing but left it off until this setup is done; until then every new build is ad-hoc signed, macOS asks again for each one, and the updater rolls back when nobody clicks Allow. Only you can create the private key and store the secrets.
