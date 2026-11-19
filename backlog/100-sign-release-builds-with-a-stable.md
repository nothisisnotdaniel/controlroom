# 100 Sign release builds with a stable identity

As the user, I want each ControlRoom release signed with the same identity, so that macOS keeps the permissions I granted and automatic updates work without me clicking Allow after every update.

## What

Release builds are only ad-hoc signed, so macOS treats every update as a new app and asks again (privacy prompt for files on the external factory volume, and a background-item notice); the server then waits at startup until someone clicks Allow, and the updater rolls back. Sign `ControlRoom.Server` and its native libraries in `release.yml` (stable and develop builds) with one stable code-signing identity, with the hardened runtime and the entitlements .NET needs, and verify the signature in the workflow (`codesign --verify --strict`) and in `install --release` and the updater before swapping. The spec picks the identity (an Apple Developer ID with notarization, or a self-signed certificate kept in a GitHub secret whose designated requirement stays the same) and says what the user must set up once (secret names, keychain import). README says how to grant the permission once.

## Why

On 2026-11-19 both 0.1.1-develop.18 and a fresh download of 0.1.0 hung at startup under launchd: tccd was asking for permission for the newly downloaded ad-hoc-signed binary, and nothing answered until the user clicked Allow. Without a stable signature every update needs a person at the Mac, so automatic updates can never finish on their own.
