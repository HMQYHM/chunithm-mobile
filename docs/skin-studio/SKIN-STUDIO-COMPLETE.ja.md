# Skin Studio 完全ガイド

この文書は 0.3.4 の Skin Studio とゲーム実行時が現在対応している内容を説明します。スキンは JSON、PNG、対応音声、任意の Live2D データからなるデータパッケージです。スキン内のコードは実行されません。未指定の任意項目はゲーム内蔵の表示を使い、対応している素材が欠けた場合は内蔵素材へフォールバックします。

## パッケージ構成

```text
MySkin.zip
  skin.json
  ui/gameplay-ui.json
  README.txt
  notes/ effects/ track/ ui/ audio/ model/
```

`skin.json` と `ui/gameplay-ui.json` は ZIP のルートに置きます。パスは `/` を使い、大文字小文字を正確に合わせてください。インポーターはパス、パッケージサイズ、画像サイズ、Live2D 依存ファイルを検査し、コードを読み込みません。

## メタデータと UI

```json
{"formatVersion":1,"id":"my-skin","name":"My Skin","author":"Author","version":"1.0.0","gameplaySkin":true}
```

`formatVersion` は現在 `1`、`id` は一意な ID、`gameplaySkin` はゲームプレイスキンとして読み込むために `true` にします。`x`、`y`、`width`、`height` は 0..1 の正規化座標です。`x=0` は左、`y=0` は下です。色は `#RRGGBB` または `#RRGGBBAA`、透明度は 0..1 です。

対応する要素 ID は `songInfo`、`songCover`、`scoreHud`、`comboHud`、`judgement`、`judgementImage`、`pauseButton`、`pausePanel`、`progressBar`、`fps`、`countdown`、`touchpad`、`airZone`、`airBoundaryBottom`、`airBoundaryTop`、`airGuide`、`trackMask`、`touchFeedback` です。各要素には `visible`、座標、サイズ、`opacity`、`color`、`image`、`label`、`textSize`、`textColor` を指定できます。曲ジャケットの上書きがない場合は曲データの元ジャケットを表示します。

## シーン、ノーツ、ロングノーツ

`scene.background.image` はプレイ背景、`scene.track.image` はレーン背景、`laneImage` はレーン線です。`judgementLineColor`、`laneColor`、`guideColor`、`edgeColor` で対応する色を変更できます。ノーツは `tap`、`extap`、`hold`、`slide`、`flick`、`dmg`、`air`、`air_up`、`air_down`、`ahtap`、`ahtapend`、`ahact`、`ahsdw` に対応します。ロングノーツ素材は `track/field/` に置き、透明でタイル可能なテクスチャを使ってください。

## 音声、タッチ、Live2D

置き換え可能な音声は `audio/ui_entry.mp3`、`audio/ui_results.mp3`、`audio/delay_ready.wav`、`audio/se_touch_answer.wav` です。曲の BGM は曲パッケージ側にあり、スキンからは置き換えません。`touchOverlay` はタッチパネルと左右のタッチ画像、正規化サイズ、オフセット、`smoothing` を設定できます。外観だけを変更し、判定領域は変更しません。

Live2D は model3 JSON、MOC3、CDI3、テクスチャ、モーション、表情を `model/` にまとめます。パラメータ ID とモーショングループはモデルに存在する必要があります。`hiddenParts` は最大 32 個です。実行環境がない場合は警告を記録してモデル層だけを無効にします。

## ルール、安全性、ワークフロー

ルールは Combo、スコア、評価、JC/J/Attack/Miss、AIR、タッチ領域、プレイ開始・終了、ノーツ値を監視できます。画像アクションは 0.05..30 秒の `duration`、フェード、0.1..4 のスケール、回転、透明度、位置、サイズに対応します。保存できるルールは 48 件、同時表示できるエフェクトは 24 件です。パストラバーサルや実行ファイルは拒否されるため、C#、JavaScript、DLL、Shader、Prefab、コールバックをパッケージに入れないでください。

ブラウザで `index.html` を開き、既存スキンを読み込むか既定レイアウトから始め、視覚パネルで編集・検証して ZIP を書き出します。完全なサンプルは [`CHUNITHM-Mobile-SkinStudio-Complete-Example-0.3.4.zip`](../../examples/skins/CHUNITHM-Mobile-SkinStudio-Complete-Example-0.3.4.zip) です。AI に設計させる場合はこの文書を渡し、記載された形式だけでディレクトリ、JSON、画像サイズ、座標理由、Live2D 範囲、フォールバック動作を出力するよう指定してください。
