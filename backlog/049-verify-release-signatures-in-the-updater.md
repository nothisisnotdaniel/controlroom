# 049 Verify release signatures in the updater without gh

As the ControlRoom owner, I want the auto-updater to verify a release signature that does not depend on the gh CLI, so that a tampered GitHub release cannot pass just by shipping a matching SHA256SUMS.

Category: security

## What

Today scripts/updater.sh trusts SHA256SUMS from the same GitHub release over TLS (job 229 decision). Add a signature check that needs no gh: for example a minisign/signify or SSH-signature (ssh-keygen -Y verify) signature over SHA256SUMS made in release.yml, with the public key committed in the repo and installed with launch-agent.sh. The updater must refuse the swap when the signature is missing or invalid, and push a failed update result. Keep the outbound allowlist unchanged.

## Why

Raised by the job 229 security review (round 1 nit 2, still open in round 2). Without it, anyone who can publish a release on the repo can push code that the Mac installs unattended every 6 hours.

## Notes

Proposed by job 229-automatic-updates. The user chose to keep it out of the 229 rework because it is bigger.
