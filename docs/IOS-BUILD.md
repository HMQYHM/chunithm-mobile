# iOS / iPadOS build

The Android and iOS versions use the same Unity project. Do not copy the project
into a second platform-specific source tree. Native plugins are separated under
`Assets/Plugins/Android` and `Assets/Plugins/iOS`; generated artifacts are written
to `Build/Android` and `Build/iOS`.

## Requirements

- Unity `2022.3.62f3c1` installed through Unity Hub.
- Unity's **iOS Build Support** module for that exact editor version.
- A Mac supported by the selected Xcode version for the final compile and signing.
- Xcode and an Apple Developer account/team for device installation.

Windows Unity with iOS Build Support can be used to export the Xcode project. It
cannot compile, sign or install the final app because those steps require Xcode on
macOS. Exporting on the Mac remains the recommended path because it verifies the
native Objective-C plugin in the same environment used for signing.

## Export the Xcode project

1. Move or clone the whole Unity project onto the Mac without copying `Library`.
2. Open it with Unity `2022.3.62f3c1` and switch the active platform to iOS.
3. Run **CHUNITHM Mobile > Apply iOS Settings**.
4. Run **CHUNITHM Mobile > Build iOS Xcode Project**.
5. Open `Build/iOS/CHUNITHM-Mobile-iOS/Unity-iPhone.xcodeproj` in Xcode.
6. Select the `Unity-iPhone` target, choose a Team under Signing & Capabilities,
   connect the iPhone/iPad, and run or archive the app.

For an automated export, set `APPLE_DEVELOPMENT_TEAM` to the Apple team ID and run:

```bash
/Applications/Unity/Hub/Editor/2022.3.62f3c1/Unity.app/Contents/MacOS/Unity \
  -batchmode -quit -buildTarget iOS \
  -projectPath /path/to/chunithm-mobile-unity \
  -executeMethod ChunithmMobile.Editor.AppleBuildConfigurator.BuildXcodeProject \
  -logFile Build/ios-build.log
```

The generated project permits local-network access for room connections. Remote
multiplayer servers should use `wss://`; unrestricted clear-text Internet access is
not enabled in the iOS plist.

## Current Windows export

The verified export is available at:

`Build/iOS/CHUNITHM-Mobile-iOS/Unity-iPhone.xcodeproj`

It is an Xcode source project, not an installable IPA. Open it on a Mac, select the
`Unity-iPhone` scheme and an Apple development team, then build to a connected
iPhone/iPad or archive it for distribution.
