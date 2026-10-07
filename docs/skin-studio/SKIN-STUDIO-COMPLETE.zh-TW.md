# Skin Studio 完整說明

本文件對應 0.3.4 版 Skin Studio 與遊戲執行時。皮膚是由 JSON、PNG、支援的音訊與可選 Live2D 資料組成的資料包，不會執行皮膚內的程式碼。未填寫的可選欄位保留遊戲內建顯示，支援回退的資源缺少時會使用內建資源。

## 目錄結構

```text
MySkin.zip
  skin.json
  ui/gameplay-ui.json
  README.txt
  notes/ effects/ track/ ui/ audio/ model/
```

`skin.json` 與 `ui/gameplay-ui.json` 必須位於 ZIP 根目錄。路徑使用 `/`，Android 上大小寫必須完全一致。匯入器會檢查路徑、包大小、圖片尺寸與 Live2D 相依檔案；不會載入程式碼。

## `skin.json` 與座標

```json
{"formatVersion":1,"id":"my-skin","name":"我的皮膚","author":"作者","version":"1.0.0","gameplaySkin":true}
```

`formatVersion` 目前填 `1`，`id` 是唯一識別，`gameplaySkin` 必須是 `true` 才會作為遊玩皮膚匯入。`x`、`y`、`width`、`height` 使用 0 到 1 的遊玩畫布比例座標；`x=0` 是左側，`y=0` 是底部。顏色使用 `#RRGGBB` 或 `#RRGGBBAA`，透明度為 0 到 1。

可覆蓋的元素包括 `songInfo`、`songCover`、`scoreHud`、`comboHud`、`judgement`、`judgementImage`、`pauseButton`、`pausePanel`、`progressBar`、`fps`、`countdown`、`touchpad`、`airZone`、`airBoundaryBottom`、`airBoundaryTop`、`airGuide`、`trackMask`、`touchFeedback`。每個元素可設定 `visible`、位置、尺寸、`opacity`、`color`、`image`、`label`、`textSize`、`textColor`。缺少歌曲封面覆蓋圖時，仍會顯示歌曲資料中的原封面。

## 場景、音符與長條

`scene.background.image` 替換遊玩背景；`scene.track.image` 是軌道圖，`laneImage` 是分道線圖，`judgementLineColor`、`laneColor`、`guideColor`、`edgeColor` 分別控制對應顏色。音符可提供 `tap`、`extap`、`hold`、`slide`、`flick`、`dmg`、`air`、`air_up`、`air_down`、`ahtap`、`ahtapend`、`ahact`、`ahsdw`。長條圖片放在 `track/field/`，應使用透明且可平鋪的主體紋理，避免使用固定長度整張圖片。

## 音訊、觸控板與 Live2D

可替換的音訊是 `audio/ui_entry.mp3`、`audio/ui_results.mp3`、`audio/delay_ready.wav`、`audio/se_touch_answer.wav`。歌曲 BGM 仍由歌曲包提供，不由皮膚替換。`touchOverlay` 可設定觸控板與左右觸控爪圖片、比例尺寸、偏移和 `smoothing`；它只改變外觀，不會改變判定區域。

Live2D 必須把 model3 JSON、MOC3、CDI3、貼圖、動作與表情放在 `model/`。參數 ID 和動作組必須存在；`hiddenParts` 最多 32 個。缺少執行環境時只停用模型層，其他皮膚內容仍可使用。

## 規則、限制與流程

規則可監聽 Combo、分數、評級、JC/J/Attack/Miss、AIR、觸控區域、遊玩開始/結束和音符值。圖片動作支援持續時間 0.05 到 30 秒、淡入淡出、縮放 0.1 到 4、旋轉、透明度、位置和尺寸。最多儲存 48 條規則，同時顯示 24 個特效。匯入器會拒絕路徑穿越和可執行檔，不要加入 C#、JavaScript、DLL、Shader、Prefab 或回呼。

在瀏覽器開啟 `index.html`，匯入現有皮膚或使用預設布局，在視覺化面板編輯並驗證後匯出 ZIP。完整示例為 [`CHUNITHM-Mobile-SkinStudio-Complete-Example-0.3.4.zip`](../../examples/skins/CHUNITHM-Mobile-SkinStudio-Complete-Example-0.3.4.zip)。提供本文件給 AI 時，要求它只輸出此文件列出的格式與欄位，並同時輸出目錄樹、座標理由、圖片尺寸、Live2D 範圍與缺少資源時的回退行為。
