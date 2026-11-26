# 178 Redo PR #4 on develop: keep overlong rework notes editable, on RoomKit 0.2.0 (after: RoomKit#6)

As the operator, I want a pasted rework note to be kept in full or stay editable when it is too long, and pending decisions to stay consistent on reopen or late refresh, so that a rework never goes out without my final instructions.

Category: bug

## What

Redo nothisisnotdaniel/controlroom PR #4 (commit 7de26fc, by i-am-steve-oh) on current develop, using its diff as the reference: count Unicode scalar values after normalizing whitespace, save a note at the limit in full, keep an overlong draft visible and editable with the limit announced and Send disabled, and reconcile pending decisions (refreshOpenJob, recheckPendingCancel) so an obsolete Cancel cannot cancel its replacement. Pin RoomKit 0.2.0 in both csproj files and register through the RoomKit host hooks (RoomKit#5) instead of editing RoomKit scripts. Keep act.js under the byte-identity check (a new expected hash, not a filter), bump CacheVersion with a shell cache ledger line, and handle the review nits from the factory comment on PR #4: no overlapping refresh for the same entry, a status while checking or after a failed check, a message when another request replaced yours, an N-over hint, the 0.85rem hint and aria-invalid border, a read-only note style, the replacement table instead of the reverse-applied patch, and comments on the decision states. Do not claim the cross-tab Cancel case is fully covered.

## Why

PR #4 cannot merge: it conflicts with develop after the RoomKit move, two of its hooks live in RoomKit files ControlRoom no longer has, and it loosens the act.js byte check. Today a long pasted note can be cut at the textarea limit and queued without the operator noticing.

## Notes

When this merges, close PR #4 with a link to the merge commit. The PR review comment on #4 lists the nits in full.
