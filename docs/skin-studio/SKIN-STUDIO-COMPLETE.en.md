# Skin Studio: Complete Guide

This guide describes the features supported by the 0.3.4 Skin Studio and the game runtime. A skin is a data package containing JSON, PNG, supported audio files, and optional Live2D data. Missing optional fields keep the game's built-in presentation. Missing resources fall back to the built-in resource where the runtime supports a fallback.

## Package layout

```text
MySkin.zip
  skin.json
  ui/gameplay-ui.json
  README.txt
  notes/
  notes/                 # note images
  track/                 # track and long-note images
  effects/               # AIR and judgement effects
  ui/                    # gameplay and touch overlays
  audio/                 # supported UI and judgement audio
  model/                 # optional Live2D model and dependencies
```

`skin.json` and `ui/gameplay-ui.json` must be at the ZIP root. Use forward slashes and exact case. The importer checks paths, package size, image dimensions, and Live2D dependencies; it does not execute code from a skin.

## Metadata and UI

```json
{
  "formatVersion": 1,
  "id": "my-skin",
  "name": "My Skin",
  "author": "Author",
  "version": "1.0.0",
  "gameplaySkin": true
}
```

`x`, `y`, `width`, and `height` use normalized canvas coordinates. `x=0` is left, `y=0` is bottom, and `1` is the opposite edge. Colors accept `#RRGGBB` or `#RRGGBBAA`; opacity is `0..1`.

Supported element IDs include `songInfo`, `songCover`, `scoreHud`, `comboHud`, `judgement`, `judgementImage`, `pauseButton`, `pausePanel`, `progressBar`, `fps`, `countdown`, `touchpad`, `airZone`, `airBoundaryBottom`, `airBoundaryTop`, `airGuide`, `trackMask`, and `touchFeedback`.

Each element can use `visible`, `x`, `y`, `width`, `height`, `opacity`, `color`, `image`, `label`, `textSize`, and `textColor`. `fontSize` accepts 8..256. Numeric `digits` images are used only when all digits required by the current value exist; otherwise text is used. A skin without a song-cover override still displays the song package's original cover.

## Scene, track, notes, and effects

`scene.background.image` replaces the gameplay background. `scene.track.image` is the track texture, `laneImage` is the lane-line texture, and `judgementLineColor`, `laneColor`, `guideColor`, and `edgeColor` control the corresponding colors.

Note assets can provide `tap`, `extap`, `hold`, `slide`, `flick`, `dmg`, `air`, `air_up`, `air_down`, `ahtap`, `ahtapend`, `ahact`, and `ahsdw`. Long-note textures belong in `track/field/`, including `hold_joint.png`, `slide_joint.png`, and `air_hold_joint.png`. Use transparent, tileable textures for long-note bodies rather than a fixed-length image.

Effects in `effects/air/` can provide AIR actions, AIR long-note effects, mine effects, and judgement helper images. Hiding a visual element does not disable its gameplay judgement.

## Audio and touch overlay

The skin may replace `audio/ui_entry.mp3`, `audio/ui_results.mp3`, `audio/delay_ready.wav`, and `audio/se_touch_answer.wav` using MP3, OGG, or WAV. Song BGM remains part of the song package and is not replaced by a skin, preserving chart timing and online synchronization.

`touchOverlay.tablet`, `leftPaw`, and `rightPaw` accept an image, normalized size, offset, and `smoothing`. A larger smoothing value follows input more quickly; 0.08..0.35 is a practical range. The overlay changes appearance only; input regions remain controlled by the game.

## Live2D

```json
{
  "live2d": {
    "enabled": true,
    "model": "model/cat.model3.json",
    "x": 0.015, "y": 0.34, "width": 0.20, "height": 0.36,
    "leftXParameter": "CatParamStickRX",
    "leftXRange": { "min": 0.75, "max": 1.0 },
    "idleMotion": "CAT_motion",
    "leftPressMotion": "CAT_motion_lock",
    "rightPressMotion": "CAT_motion_lock"
  }
}
```

Keep the model3 JSON, MOC3, CDI3, textures, motions, and expressions together in `model/`. Parameter IDs and motion groups must exist in the model. `hiddenParts` may hide up to 32 parts. Without the required runtime support, the game logs a warning and disables only the model layer.

## Rules and safe authoring

Rules can react to combo, score, rating, JC/J/Attack/Miss, AIR, touch zones, play start/end, and note values. Image actions support `duration` (0.05..30 seconds), `fadeIn`, `fadeOut`, `scaleFrom`, `scaleTo` (0.1..4), rotation, opacity, and normalized position and size. A threshold rule fires once when crossed. At most 48 rules are saved and 24 effects are shown at once.

The importer rejects path traversal and executable/plugin files. Do not put C#, JavaScript, DLL, Shader, Prefab, or callbacks in a skin package.

## Workflow and AI prompt

Open the browser editor's `index.html`, import a skin or start from the default layout, edit fields in the visual panels, validate, and export the ZIP. Preview each language from the language switcher. Test the exported package in a disposable game profile before sharing it.

For AI-assisted authoring, provide this guide and ask the AI to output a complete directory tree, `skin.json`, `ui/gameplay-ui.json`, image sizes, coordinate and timing rationale, Live2D parameter ranges, and fallback behavior. Require the AI to use only the formats and fields documented here.

The complete example package is [`CHUNITHM-Mobile-SkinStudio-Complete-Example-0.3.4.zip`](../../examples/skins/CHUNITHM-Mobile-SkinStudio-Complete-Example-0.3.4.zip).
