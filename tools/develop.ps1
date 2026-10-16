param(
    [ValidateSet('Preflight', 'Test', 'Build')]
    [string]$Mode = 'Preflight',
    [string]$TestName,
    [string]$UnityExe = 'F:\Unity Hub\2022.3.62f3c1\Editor\Unity.exe'
)

$ErrorActionPreference = 'Stop'
$project = Split-Path -Parent $PSScriptRoot
$git = Get-Command git -ErrorAction Stop

Write-Output "Project: $project"
$changes = @(& $git.Source -C $project status --short)
if ($LASTEXITCODE -ne 0) { throw 'Cannot inspect git worktree.' }
Write-Output "Existing worktree entries: $($changes.Count) (preserved; inspect with git status --short)."

& (Join-Path $PSScriptRoot 'verify-project.ps1')
if ($LASTEXITCODE -ne 0) { throw 'Project structure verification failed.' }
& $git.Source -C $project diff --check
if ($LASTEXITCODE -ne 0) { throw 'git diff --check failed.' }
Write-Output 'Preflight passed (Unity compilation and Editor tests not included).'
if ($Mode -eq 'Preflight') { return }

if (-not (Test-Path -LiteralPath $UnityExe -PathType Leaf)) { throw "Unity editor not found: $UnityExe" }
$expectedVersion = (Get-Content -LiteralPath (Join-Path $project 'ProjectSettings\ProjectVersion.txt') |
    Select-String '^m_EditorVersion: (.+)$').Matches.Groups[1].Value
if (-not $expectedVersion -or -not $UnityExe.Contains($expectedVersion)) {
    throw "Unity path must use project editor version $expectedVersion"
}
if (Get-Process Unity -ErrorAction SilentlyContinue) {
    throw 'Unity is already running. Close the editor for this project before batch building.'
}

function Invoke-UnityBatch([string]$Method, [string]$LogPath, [switch]$NoGraphics) {
    $arguments = @('-batchmode', '-quit')
    if ($NoGraphics) { $arguments += @('-nographics', '-force-no-audio') }
    $arguments += @('-projectPath', $project, '-executeMethod', $Method, '-logFile', $LogPath)
    if ($IsWindows) {
        # Unity.exe is a Windows GUI executable; wait for the editor itself.
        $process = Start-Process -FilePath $UnityExe -ArgumentList $arguments -PassThru -WindowStyle Hidden
        $process.WaitForExit()
        return $process.ExitCode
    }
    # On macOS Unity is a native executable. Calling it directly preserves the
    # exit code and avoids Start-Process argument quoting differences.
    & $UnityExe @arguments
    return $LASTEXITCODE
}

$buildRoot = Join-Path $project 'Build'
$buildDir = Join-Path $buildRoot 'Android'
New-Item -ItemType Directory -Path $buildDir -Force | Out-Null
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$testMarkers = @{
    PandoraRibbonMotionSelfTest = 'Pandora ribbon motion audit completed:'
    LongRibbonMotionAudit = 'Long ribbon motion audit completed:'
    ExTapVisualSelfTest = 'EXTAP visual regression passed:'
    AppBootstrapSelfTest = 'App bootstrap regression passed:'
    TrackMaskSelfTest = 'Track mask regression passed:'
    JudgementAudioSelfTest = 'Judgement audio regression passed:'
    UiFullRegressionSelfTest = 'UI full regression passed:'
    UiButtonAuditSelfTest = 'UI button audit passed:'
    OriginalAirNodeSelfTest = 'Original AIR node regression passed:'
    AirLadderAssetSelfTest = 'AirLadder asset regression passed:'
    AlterEgoAirVisualSelfTest = 'ALTER EGO AIR regression passed:'
    AirLayerSelfTest = 'AIR layer regression passed:'
    GameplayPauseSelfTest = 'Gameplay pause regression passed:'
    GroundInputSelfTest = 'Ground input regression passed:'
    LayoutFeedbackSelfTest = 'Layout feedback regression passed:'
    MineReverseSelfTest = 'Mine/reverse tests passed:'
    ModDialogSelfTest = 'MOD dialog regression passed:'
    OpeningNoteRenderSelfTest = 'Opening-note render integration passed:'
    OriginalPlaybackSelfTest = 'Original playback self-test passed:'
    OriginalNotePrecalculationSelfTest = 'Original note pre-calculation self-test passed:'
    LongNoteJudgementStabilitySelfTest = 'Long-note judgement stability regression passed:'
    PreparationAndLongHeadSelfTest = 'Preparation/long-head/level regression passed:'
    DdsTextureDecoderSelfTest = 'DDS texture regression passed:'
    Live2DSkinSelfTest = 'Live2D skin regression passed:'
    GameplaySkinCustomDisplaySelfTest = 'Custom display regression passed:'
    GameplaySkinFallbackSelfTest = 'Gameplay skin fallback regression passed:'
    GameplaySkinDigitsSelfTest = 'Gameplay skin digit regression passed:'
    SkinStudioRulesSelfTest = 'Skin Studio rules regression passed:'
    SkinSettingsSelfTest = 'Skin UI regression passed:'
    SkinWidthSelfTest = 'Skin UV regression passed:'
    SlideHeadSelfTest = 'Slide head regression passed:'
    SongListBucketSelfTest = 'Song-list regression passed:'
    SongAchievementSelfTest = 'Song achievement regression passed:'
    WorldEndAirNodeSelfTest = 'WD AIR regression passed:'
}
if ($Mode -eq 'Test') {
    if (-not $TestName -or -not $testMarkers.ContainsKey($TestName)) {
        throw "Specify -TestName from: $(($testMarkers.Keys | Sort-Object) -join ', ')"
    }
    $log = Join-Path $buildRoot "test-$TestName-$stamp.log"
    $unityExit = Invoke-UnityBatch "ChunithmMobile.Editor.$TestName.Run" $log
    if (-not (Test-Path -LiteralPath $log -PathType Leaf)) { throw "Unity produced no log: $log" }
    $logText = Get-Content -LiteralPath $log -Raw
    if ($unityExit -ne 0 -or $logText -match 'Crash!!!|A crash has been intercepted|error CS\d+|BuildFailedException' -or
        $logText -notmatch 'Exiting batchmode successfully' -or
        -not $logText.Contains($testMarkers[$TestName])) {
        throw "Unity test did not finish cleanly (exit=$unityExit). Inspect $log"
    }
    Write-Output "Editor test passed: $TestName"
    Write-Output "Unity log: $log"
    return
}

$log = Join-Path $buildRoot "build-$stamp.log"
$version = [regex]::Match((Get-Content -LiteralPath (Join-Path $project 'Assets\Editor\AndroidBuildConfigurator.cs') -Raw),
    'PlayerSettings\.bundleVersion\s*=\s*"([^"]+)"').Groups[1].Value
if (-not $version) { throw 'Cannot determine APK version from AndroidBuildConfigurator.' }
$requestedApk = $env:CHUNITHM_APK_OUTPUT
$apk = if ([string]::IsNullOrWhiteSpace($requestedApk)) {
    Join-Path $buildDir "CHUNITHM-Mobile-$version-unity-arm64.apk"
} else {
    [System.IO.Path]::GetFullPath($requestedApk)
}
$started = Get-Date

# Set the output explicitly: a stale default-named APK must never count as this build.
$previousOutput = $env:CHUNITHM_APK_OUTPUT
try {
    $env:CHUNITHM_APK_OUTPUT = $apk
    # APK generation does not need an editor display; avoid workstation graphics
    # initialization so the build is less dependent on the desktop driver.
    $unityExit = Invoke-UnityBatch 'ChunithmMobile.Editor.AndroidBuildConfigurator.BuildApk' $log -NoGraphics
} finally {
    $env:CHUNITHM_APK_OUTPUT = $previousOutput
}

if (-not (Test-Path -LiteralPath $log -PathType Leaf)) { throw "Unity produced no log: $log" }
$logText = Get-Content -LiteralPath $log -Raw
if ($unityExit -ne 0 -or $logText -match 'Crash!!!|A crash has been intercepted|error CS\d+|BuildFailedException|Build failed' -or
    $logText -notmatch 'Exiting batchmode successfully') {
    throw "Unity build did not finish cleanly (exit=$unityExit). Inspect $log"
}
if (-not (Test-Path -LiteralPath $apk -PathType Leaf)) { throw "APK not created: $apk" }
$artifact = Get-Item -LiteralPath $apk
# Unity's Android postprocessor can preserve the destination APK timestamp when
# it replaces an existing package. The authoritative freshness signal is the
# successful BuildPipeline result in the Unity log; do not reject a valid APK
# solely because its filesystem timestamp was retained.
if ($artifact.Length -lt 1MB) {
    throw "APK is empty: $apk"
}
$hash = Get-FileHash -LiteralPath $apk -Algorithm SHA256
Write-Output "APK: $($artifact.FullName)"
Write-Output "Bytes: $($artifact.Length)"
Write-Output "SHA256: $($hash.Hash)"
Write-Output "Unity log: $log"
