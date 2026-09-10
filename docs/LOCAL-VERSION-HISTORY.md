# Local version history

Repository: `F:/zhongermate/chunithm-mobile-unity`

This local repository was initialized on 2026-09-10. It has no remote and does
not upload project files. Unity assets (including .meta files), scripts,
project settings, package manifests and development tools are tracked.
Generated Library/Build/Temp/Logs files are excluded.

## Saved snapshots

- `v0.2.7-before-opening-note-fix`: source as it existed before the opening-note
  fix, including the adaptive frame-rate changes.
- `v0.2.7-opening-notes-fixed`: isolated track/note geometry, invalid note pool
  recovery, and a real camera/GPU first-frame warmup before playback readiness.
- `v0.2.7-before-mine-combo`: snapshot before the mine combo change.
- `v0.2.7-mine-combo-fixed`: mine collision MISS clears the live combo before
  publishing its judgement event, including non-scoring hazards. Immunity,
  safe avoidance, existing score totals and historical max combo are preserved.
- `v0.2.8`: Android version 0.2.8 (code 208), six release notes supplied by the
  author, translated notice/history entries, and version-aware home/about text.
  Full history backup: `Build/chunithm-mobile-history-0.2.8.bundle`.

These are snapshots of the code available when version control was started.
They do not reconstruct older 0.2.x releases from APK files.

Inspect history with `git log --oneline --decorate`. Compare the snapshots with
`git diff v0.2.7-before-opening-note-fix v0.2.7-opening-notes-fixed`.
To inspect the old code without replacing current work, create a separate
checkout with:

```powershell
git worktree add ../chunithm-opening-baseline v0.2.7-before-opening-note-fix
```

The portable repository backup is
`Build/chunithm-mobile-history-0.2.7-opening-notes.bundle`.
It contains all commits and tags at the time of this fix. It can be verified
with `git bundle verify` and restored into a new folder using `git clone`.
Future revisions should be committed separately, with a distinct tag after
validation; do not overwrite the existing release tags.

The later mine combo snapshot is also backed up in
`Build/chunithm-mobile-history-0.2.7-mine-combo.bundle`. This includes the earlier
opening-note commits. Its targeted input regression covers 26 combinations of
press/held contact, playback rate, simple judgement, immunity, AUTO, avoidance,
event ordering and combo recovery.

## Validation for the opening-note fix

`OpeningNoteRenderSelfTest.BuildVerifiedApk` checks the shared-host track/note
setup, track rebuilds with active and pooled notes, deliberately invalidated
objects, real camera rendering and note pixel differences. It also runs the
preparation/long-head/level and adaptive frame-rate regression checks.
The two-second silent preparation remains in place. Device-specific GPU and
timing behavior still needs testing on physical hardware.
