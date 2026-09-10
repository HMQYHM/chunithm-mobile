# CHUNITHM Mobile — Unity migration

This is the new, independent Unity implementation. The existing Android/Kotlin app is deliberately untouched.

## Android target

- Unity 2022.3.62f3c1 LTS (China release; selected because the current regional CDN redirects Unity 6 downloads to missing files)
- Android 7.0 / API 24 minimum
- IL2CPP, ARM64 only
- Vulkan first, OpenGL ES 3 fallback
- Package id: `com.zhongermate.chunithmmobile` (does not replace the old APK)

The GLES3 fallback is intentional: Android 7 is the OS minimum, but Vulkan support is a separate GPU/driver capability. A Vulkan-only APK would exclude some otherwise compatible Android 7 devices.

## Cross-platform direction

Android remains the only active implementation target until gameplay is stable.
Shared chart, timing, judgement, rendering and data code is also intended for a
later Unity iOS/Metal export and a Tuanjie/OpenHarmony export for HarmonyOS NEXT.
Platform SDK access is isolated behind `PlatformServices` and
`PlatformStoragePaths`; see `docs/PORTABILITY.md`.

## Implemented migration foundation

- UTF-8 C2S parser with BPM, SFL, SLP, SLA and STP support
- double-precision chart and scroll timing, including negative/reverse speed
- SLP profile 0 controls the global course; profiles 1–16 independently move notes, ribbon sections and ALD decorations inside SLA time/lane assignment rectangles
- TAP / ExTAP / FLICK timing profile and independent long-note checkpoints
- AIR rules separated from ground lane position
- cabinet-style input layer: continuous 16-lane ground surface plus 6 vertical AIR sensors
- TAP can be crossed by a moving contact; FLICK detects horizontal motion even after a long hold
- AIR up/down graphics do not force input direction; AIR-ACTION requires vertical motion
- HOLD/SLIDE/AIR sustain checkpoints have no tail judgement and can reconnect after a MISS
- original `T_JUDGE_*` combo totals retained, including multiple judge points sharing one timestamp
- DSP-anchored chart clock whose judgement time remains monotonic during visual reverse sections
- cross-platform MP3 loading with a shared DSP-scheduled song/chart start
- playback resources released when gameplay ends or the session is destroyed
- pooled 3D chart renderer with independent head/body lifetime and profile-aware long-ribbon sampling
- reverse-safe visibility bounds, so a long ribbon stays visible when its middle is on screen even if both endpoints are outside
- Editor command that creates the bootstrap scene and applies all Android settings
- Editor self-test for independent global reverse and local SLA/SLP playback
- real-chart validator for finite reverse/zero/extreme-speed positions and exact judge-point totals

## Build

Install Unity 2022.3.62f3c1 with **Android Build Support**, **Android SDK & NDK Tools**, and **OpenJDK**. Open this directory once, then choose:

`CHUNITHM Mobile > Build Android APK`

Output: `Build/CHUNITHM-Mobile-0.2.4-unity-arm64.apk`

No songs or charts are embedded in the APK. For an early playback test, use ADB to copy a song folder containing a `.c2s` file to `/sdcard/Android/data/com.zhongermate.chunithmmobile/files/Songs/`; the development bridge loads the first chart and logs all global/local scroll counts. Android's app-specific external directory needs no broad storage permission.

All future imported songs use `audio/gameplay.mp3` for gameplay. Original
ACB/AWB files are preview import sources: the importer generates
`audio/preview.mp3`, which the song-selection screen plays before Start is
pressed. If no ACB exists, song selection falls back to `gameplay.mp3`; formal
gameplay always uses `gameplay.mp3`. The runtime no longer carries an AWB/HCA
decoder or performs per-play transcoding. The next migration slices are
the persistent song database/importer, complete UI/result screens, guide-sound
track generation, and exact material/effect matching. Game logic and rendering
continue to share one chart-space transform.
