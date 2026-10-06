# Platform architecture

Android and iOS/iPadOS are supported from the same Unity project. HarmonyOS NEXT
remains a later port, not a parallel feature branch.

## Shared code

The following code remains identical on all platforms:

- C2S parsing and imported song metadata
- BPM, stop, global/local speed and reverse playback mapping
- judgement checkpoints, combo, score and rank
- ground/AIR input semantics after raw contacts are normalized
- chart-space geometry, meshes, effects and UI state
- save-data schema and imported song folder layout

## Platform adapters

Only these boundaries may contain platform SDK calls:

- application sandbox paths and document/archive picker
- low-latency audio output and codec conversion
- raw touch/lifecycle/safe-area information
- log sharing/export
- store signing and package metadata

`PlatformServices` owns platform identity, the safe area and the high precision
audio clock. `PlatformStoragePaths` owns mobile storage, document picker and file
sharing bridges. Gameplay code must not call Android Java, iOS native APIs, ArkTS
or Node-API directly.

## Build order

1. Android: Unity, IL2CPP ARM64, Vulkan with GLES3 fallback, API 24+.
2. Apple: Unity iOS export, Xcode, Metal, IL2CPP ARM64, iOS 12+, native document
   picker and file sharing. Final compilation and signing still require macOS/Xcode.
3. HarmonyOS NEXT: import the shared Unity project into Tuanjie, export the
   OpenHarmony UAAL project, then sign and integrate platform capabilities in
   DevEco Studio.

No song or chart is embedded in any platform package. All three targets use the
same imported song directory format and UTF-8 metadata.
