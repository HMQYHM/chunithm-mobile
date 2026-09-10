$ErrorActionPreference = 'Stop'
$project = Split-Path -Parent $PSScriptRoot
$manifest = Get-Content -LiteralPath (Join-Path $project 'Packages\manifest.json') -Raw | ConvertFrom-Json
if (-not $manifest.dependencies) { throw 'Packages/manifest.json has no dependencies.' }

$required = @(
    'ProjectSettings\ProjectVersion.txt',
    'Assets\Editor\AndroidBuildConfigurator.cs',
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

$gameplayFiles = Get-ChildItem -LiteralPath (Join-Path $project 'Assets\Scripts') -Recurse -Filter '*.cs' |
    Where-Object {
        $_.FullName -notmatch '[\\/]Runtime[\\/]PlatformStoragePaths\.cs$'
    }
foreach ($file in $gameplayFiles) {
    if ((Get-Content -LiteralPath $file.FullName -Raw).Contains('AndroidJava')) {
        throw "Android Java leaked outside the storage adapter: $($file.FullName)"
    }
}
Write-Output 'Unity project structure and Android target settings verified.'
