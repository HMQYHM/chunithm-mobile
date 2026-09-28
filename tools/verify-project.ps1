$ErrorActionPreference = 'Stop'
$expectedAppleBuildNumber = '222'
$project = Split-Path -Parent $PSScriptRoot
$manifest = Get-Content -LiteralPath (Join-Path $project 'Packages\manifest.json') -Raw | ConvertFrom-Json
if (-not $manifest.dependencies) { throw 'Packages/manifest.json has no dependencies.' }

$required = @(
    'ProjectSettings\ProjectVersion.txt',
    'Assets\Editor\AndroidBuildConfigurator.cs',
    'Assets\Editor\AppleBuildConfigurator.cs',
    'Assets\Plugins\iOS\ChunithmDocumentPicker.mm',
    'Assets\Plugins\iOS\ChunithmWebSocket.mm',
    'Assets\Plugins\iOS\ChunithmSceneDelegate.mm',
    'Assets\Plugins\iOS\ChunithmSceneDelegate.mm.meta',
    'Assets\Scripts\Runtime\DdsTextureDecoder.cs',
    'Assets\Scripts\Runtime\DdsTextureDecoder.cs.meta',
    'Assets\Editor\DdsTextureDecoderSelfTest.cs',
    'Assets\Editor\DdsTextureDecoderSelfTest.cs.meta',
    'Assets\Editor\OriginalPlaybackSelfTest.cs',
    'Assets\Scripts\Chart\C2sParser.cs',
    'Assets\Scripts\Timing\OriginalPlaybackMap.cs',
    'Assets\Scripts\Timing\ScrollPositionMapper.cs',
    'Assets\Scripts\Input\ArcadeTouchInput.cs',
    'Assets\Scripts\Input\AirSensorInput.cs',
    'Assets\Scripts\Judgement\JudgePointScheduler.cs',
    'Assets\Scripts\Judgement\GameplayJudgeController.cs',
    'Assets\Scripts\Platform\PlatformServices.cs',
    'Assets\Scripts\Rendering\ChartSpace.cs',
    'Assets\Scripts\Rendering\ChartPlaybackRenderer.cs',
    'Assets\Scripts\Runtime\LocalChartPlaybackSession.cs',
    'Assets\Scripts\Runtime\AppBootstrap.cs',
    'Assets\Scripts\Runtime\RuntimeLoadingOverlay.cs',
    'Assets\Scripts\Runtime\SongAudioFiles.cs',
    'Assets\Scripts\Runtime\SongSelectionPreviewPlayer.cs',
    'Assets\Scripts\Runtime\SongCatalog.cs',
    'Assets\Scripts\Runtime\SongSelectionScreen.cs',
    'Assets\Scripts\Runtime\AppUiModels.cs',
    'Assets\Scripts\Runtime\AppUiShell.cs',
    'Assets\Scripts\Runtime\UiMarqueeText.cs',
    'Assets\Scripts\Runtime\LocalCoverLoader.cs',
    'Assets\Resources\Fonts\HarmonyOS_Sans_SC_Regular.ttf',
    'Assets\Resources\Fonts\HarmonyOS_Sans_SC_Bold.ttf',
    'Assets\Resources\Fonts\HarmonyOS_Sans_LICENSE.txt',
    'docs\PORTABILITY.md'
)
foreach ($item in $required) {
    if (-not (Test-Path -LiteralPath (Join-Path $project $item) -PathType Leaf)) {
        throw "Missing Unity migration file: $item"
    }
}

$config = Get-Content -LiteralPath (Join-Path $project 'Assets\Editor\AndroidBuildConfigurator.cs') -Raw
foreach ($needle in @('AndroidApiLevel24', 'AndroidArchitecture.ARM64', 'GraphicsDeviceType.Vulkan', 'GraphicsDeviceType.OpenGLES3', 'IL2CPP')) {
    if (-not $config.Contains($needle)) { throw "Android build setting not found: $needle" }
}

$appleConfig = Get-Content -LiteralPath (Join-Path $project 'Assets\Editor\AppleBuildConfigurator.cs') -Raw
if (-not $appleConfig.Contains(('"' + $expectedAppleBuildNumber + '"'))) { throw 'iOS build number is not 222.' }
if (-not $appleConfig.Contains('CODE_SIGN_STYLE')) { throw 'iOS signing mode is not configured.' }
if (-not $appleConfig.Contains('Apple Distribution')) { throw 'iOS Distribution identity is not configured.' }
foreach ($needle in @('NamedBuildTarget.iOS', 'Build/iOS/CHUNITHM-Mobile-iOS', 'iOSTargetDevice.iPhoneAndiPad', 'IL2CPP', '"222"', 'UniformTypeIdentifiers.framework', 'UIApplicationSceneManifest')) {
    if (-not $appleConfig.Contains($needle)) { throw "iOS build setting not found: $needle" }
}

$androidBridges = @(
    'Runtime/AppUiShell.cs', # Log export bridge; keep the rest of the UI platform-neutral.
    'Runtime/MultiplayerClient.cs', # Device ID lookup.
    'Runtime/PlatformMemoryInfo.cs',
    'Runtime/PlatformStoragePaths.cs'
)
$scriptsRoot = Join-Path $project 'Assets\Scripts'
foreach ($file in (Get-ChildItem -LiteralPath $scriptsRoot -Recurse -Filter '*.cs')) {
    $relative = [System.IO.Path]::GetRelativePath($scriptsRoot, $file.FullName).Replace('\', '/')
    $guards = [System.Collections.Generic.List[bool]]::new()
    $lineNumber = 0
    foreach ($line in (Get-Content -LiteralPath $file.FullName)) {
        $lineNumber++
        if ($line -match '^\s*#if\s+(.+?)\s*$') {
            $guards.Add($Matches[1] -eq 'UNITY_ANDROID && !UNITY_EDITOR')
        } elseif ($line -match '^\s*#elif\s+(.+?)\s*$') {
            if ($guards.Count -eq 0) { throw "Unmatched #elif in ${relative}:$lineNumber" }
            $guards[$guards.Count - 1] = $Matches[1] -eq 'UNITY_ANDROID && !UNITY_EDITOR'
        } elseif ($line -match '^\s*#else\s*$') {
            if ($guards.Count -eq 0) { throw "Unmatched #else in ${relative}:$lineNumber" }
            $guards[$guards.Count - 1] = $false
        } elseif ($line -match '^\s*#endif\s*$') {
            if ($guards.Count -eq 0) { throw "Unmatched #endif in ${relative}:$lineNumber" }
            $guards.RemoveAt($guards.Count - 1)
        }
        if ($line -notmatch '\bAndroidJava(?:Class|Object)\b') { continue }
        if ($relative -notin $androidBridges) {
            throw "Android Java belongs in an approved Runtime bridge, not ${relative}:$lineNumber"
        }
        if (-not $guards.Contains($true)) {
            throw "Android Java must be guarded by #if UNITY_ANDROID && !UNITY_EDITOR in ${relative}:$lineNumber"
        }
    }
    if ($guards.Count -ne 0) { throw "Unclosed preprocessor guard in $relative" }
}
Write-Output 'Unity project structure and Android/iOS target settings verified.'
