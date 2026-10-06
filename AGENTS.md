# Repository working rules

- Read `docs/DEVELOPMENT-WORKFLOW.md` and the affected module before editing.
- Preserve unrelated worktree changes. Never reset, restore, or delete them to make a build pass.
- Keep edits inside the owning module. Gameplay timing uses the DSP chart clock, not render frames.
- Explain new public contracts and non-obvious timing, input, rendering, resource ownership, or network state transitions in adjacent code comments. Update `CODEBASE_ANNOTATION_GUIDE.md` when module boundaries change. Do not add comments that merely repeat the statement below them.
- Add or update a focused Editor self-test for behavioral changes. A source comment alone is not verification.
- Run `tools/develop.ps1 -Mode Preflight` and relevant `-Mode Test -TestName <Class>` Editor self-tests before calling a change verified. Use `-Mode Build` when an APK is requested.
- Never claim an APK was built because the Unity process returned zero alone. Check the Unity log and the fresh APK. Installation and server deployment require a separate explicit request.
