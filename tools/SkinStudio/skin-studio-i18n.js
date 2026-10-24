(() => {
  const maps = {
    'zh-TW': {
      '语言':'語言','选择语言':'選擇語言','打开皮肤文件夹':'開啟皮膚資料夾','导入皮肤 ZIP':'匯入皮膚 ZIP','导入 JSON':'匯入 JSON','撤回':'復原','取消撤回':'重做','保存':'儲存','导出 JSON':'匯出 JSON','导出皮肤 ZIP':'匯出皮膚 ZIP',
      '游玩界面布局':'遊玩介面配置','游玩中':'遊玩中','横屏游玩界面预览':'橫向遊玩介面預覽','预览状态':'預覽狀態','暂停':'暫停','目标值归零':'目標值歸零','目标值低于零':'目標值低於零','自定义事件':'自訂事件','分数 900000':'分數 900000','左上触控':'左上觸控','游玩结束':'遊玩結束','网格':'網格','定位特效':'定位特效','试播匹配规则':'試播符合規則','停止试播':'停止試播',
      '皮肤名称':'皮膚名稱','皮肤 ID':'皮膚 ID','元素图层':'元素圖層','添加元素':'新增元素','触控板':'觸控板','AIR 区域':'AIR 區域','AIR 下边界':'AIR 下邊界','AIR 上边界':'AIR 上邊界','AIR 引导线':'AIR 引導線','轨道遮罩':'軌道遮罩','输入区域':'輸入區域','编辑区域':'編輯區域','移除当前元素':'移除目前元素','只能导出游戏已支持的元素 ID；按钮只改变外观和位置，不会替换原有行为。':'只能匯出遊戲已支援的元素 ID；按鈕只會改變外觀與位置，不會替換原有行為。',
      '属性':'屬性','界面布局':'介面配置','规则积木':'規則積木','场景轨道':'場景軌道','请选择一个元素':'請選擇一個元素','元素 ID':'元素 ID','显示此元素':'顯示此元素','宽度':'寬度','高度':'高度','透明度':'透明度','颜色':'顏色','图片路径':'圖片路徑','选择 PNG 图片':'選擇 PNG 圖片','按钮/文字标签':'按鈕／文字標籤','文字大小':'文字大小','文字颜色':'文字顏色',
      '音符下方显示模式':'音符下方顯示模式','当前 Combo':'目前 Combo','剩余 Combo':'剩餘 Combo','当前分数':'目前分數','SSS+ 剩余分数':'SSS+ 剩餘分數','SSS 剩余分数':'SSS 剩餘分數','SS+ 剩余分数':'SS+ 剩餘分數','SS 剩余分数':'SS 剩餘分數','S+ 剩余分数':'S+ 剩餘分數','S 剩余分数':'S 剩餘分數','已损失分数':'已損失分數','距离最好成绩':'距離最佳成績','数字与文字样式':'數字與文字樣式','Combo 字号':'Combo 字號','分数字号':'分數字號','判定字号':'判定字號','Combo 颜色':'Combo 顏色','分数颜色':'分數顏色','判定颜色':'判定顏色','Combo 数字皮肤（0–9）':'Combo 數字皮膚（0–9）','分数数字皮肤（0–9）':'分數數字皮膚（0–9）','未设置':'未設定',
      '触控板与触控爪皮肤':'觸控板與觸控爪皮膚','启用触控板皮肤':'啟用觸控板皮膚','触控板 PNG':'觸控板 PNG','左爪 PNG':'左爪 PNG','右爪 PNG':'右爪 PNG','导入左爪 PNG':'匯入左爪 PNG','导入右爪 PNG':'匯入右爪 PNG','左爪宽度':'左爪寬度','左爪高度':'左爪高度','右爪宽度':'右爪寬度','右爪高度':'右爪高度','左爪偏移 X':'左爪偏移 X','左爪偏移 Y':'左爪偏移 Y','右爪偏移 X':'右爪偏移 X','右爪偏移 Y':'右爪偏移 Y','左爪平滑':'左爪平滑','右爪平滑':'右爪平滑',
      '可视化规则积木':'視覺化規則積木','添加规则':'新增規則','规则预设':'規則預設','添加预设':'新增預設','场景与轨道':'場景與軌道','导入场景 PNG':'匯入場景 PNG','游玩背景图片':'遊玩背景圖片','使用歌曲背景':'使用歌曲背景','背景颜色':'背景顏色','背景透明度':'背景透明度','轨道颜色':'軌道顏色','分道线颜色':'分道線顏色','引导线颜色':'引導線顏色','边缘颜色':'邊緣顏色','判定线颜色':'判定線顏色','轨道底图 PNG':'軌道底圖 PNG','使用内置轨道':'使用內建軌道','分道线 PNG':'分道線 PNG','使用内置分道线':'使用內建分道線',
      '启用 Live2D 模型':'啟用 Live2D 模型','导入模型文件夹':'匯入模型資料夾','BongoCat 参数':'BongoCat 參數','模型 .model3.json':'模型 .model3.json','选择已导入的模型':'選擇已匯入的模型','模型路径':'模型路徑','未导入模型':'尚未匯入模型','左右手参数映射':'左右手參數對應','动作组与隐藏部件':'動作組與隱藏部件','隐藏部件 ID（每行一个）':'隱藏部件 ID（每行一個）','模型背景 / 平板':'模型背景／平板','背景 PNG 路径':'背景 PNG 路徑','选择背景 PNG':'選擇背景 PNG',
      '个资源':'個資源','未修改':'未修改','本地资源 · 不执行包内代码':'本機資源 · 不執行套件內程式碼','未保存修改':'未儲存修改','打开新项目将丢失当前未保存的修改。':'開啟新專案會遺失目前未儲存的修改。','继续编辑':'繼續編輯','放弃修改并打开':'放棄修改並開啟'
    },
    ja: {
      '语言':'言語','选择语言':'言語を選択','打开皮肤文件夹':'スキンフォルダーを開く','导入皮肤 ZIP':'スキン ZIP を読み込む','导入 JSON':'JSON を読み込む','撤回':'元に戻す','取消撤回':'やり直す','保存':'保存','导出 JSON':'JSON を書き出す','导出皮肤 ZIP':'スキン ZIP を書き出す',
      '游玩界面布局':'プレイ画面レイアウト','游玩中':'プレイ中','横屏游玩界面预览':'横画面プレイプレビュー','预览状态':'プレビュー状態','暂停':'一時停止','目标值归零':'目標値がゼロ','目标值低于零':'目標値がゼロ未満','自定义事件':'カスタムイベント','分数 900000':'スコア 900000','左上触控':'左上タッチ','游玩结束':'プレイ終了','网格':'グリッド','定位特效':'ルール位置','试播匹配规则':'ルールを試す','停止试播':'試聴を停止',
      '皮肤名称':'スキン名','皮肤 ID':'スキン ID','元素图层':'要素レイヤー','添加元素':'要素を追加','触控板':'タッチパッド','AIR 区域':'AIR エリア','AIR 下边界':'AIR 下端','AIR 上边界':'AIR 上端','AIR 引导线':'AIR ガイド','轨道遮罩':'レーンマスク','输入区域':'入力エリア','编辑区域':'エリアを編集','移除当前元素':'現在の要素を削除','只能导出游戏已支持的元素 ID；按钮只改变外观和位置，不会替换原有行为。':'ゲームが対応する要素 ID のみ書き出せます。外観と位置だけを変更し、動作は置き換えません。',
      '属性':'プロパティ','界面布局':'画面レイアウト','规则积木':'ルールブロック','场景轨道':'シーンとレーン','请选择一个元素':'要素を選択してください','元素 ID':'要素 ID','显示此元素':'この要素を表示','宽度':'幅','高度':'高さ','透明度':'不透明度','颜色':'色','图片路径':'画像パス','选择 PNG 图片':'PNG を選択','按钮/文字标签':'ボタン／文字ラベル','文字大小':'文字サイズ','文字颜色':'文字色',
      '音符下方显示模式':'ノーツ下の表示モード','当前 Combo':'現在の Combo','剩余 Combo':'残り Combo','当前分数':'現在のスコア','SSS+ 剩余分数':'SSS+ までの残り','SSS 剩余分数':'SSS までの残り','SS+ 剩余分数':'SS+ までの残り','SS 剩余分数':'SS までの残り','S+ 剩余分数':'S+ までの残り','S 剩余分数':'S までの残り','已损失分数':'失ったスコア','距离最好成绩':'ベストとの差','数字与文字样式':'数字と文字のスタイル','Combo 字号':'Combo サイズ','分数字号':'スコアサイズ','判定字号':'判定サイズ','Combo 颜色':'Combo 色','分数颜色':'スコア色','判定颜色':'判定色','Combo 数字皮肤（0–9）':'Combo 数字スキン（0–9）','分数数字皮肤（0–9）':'スコア数字スキン（0–9）','未设置':'未設定',
      '触控板与触控爪皮肤':'タッチパッドとタッチ爪スキン','启用触控板皮肤':'タッチパッドスキンを有効化','触控板 PNG':'タッチパッド PNG','左爪 PNG':'左爪 PNG','右爪 PNG':'右爪 PNG','导入左爪 PNG':'左爪 PNG を読み込む','导入右爪 PNG':'右爪 PNG を読み込む','左爪宽度':'左爪の幅','左爪高度':'左爪の高さ','右爪宽度':'右爪の幅','右爪高度':'右爪の高さ','左爪偏移 X':'左爪 X オフセット','左爪偏移 Y':'左爪 Y オフセット','右爪偏移 X':'右爪 X オフセット','右爪偏移 Y':'右爪 Y オフセット','左爪平滑':'左爪の平滑化','右爪平滑':'右爪の平滑化',
      '可视化规则积木':'ビジュアルルールブロック','添加规则':'ルールを追加','规则预设':'ルールプリセット','添加预设':'プリセットを追加','场景与轨道':'シーンとレーン','导入场景 PNG':'シーン PNG を読み込む','游玩背景图片':'プレイ背景画像','使用歌曲背景':'曲の背景を使用','背景颜色':'背景色','背景透明度':'背景の不透明度','轨道颜色':'レーン色','分道线颜色':'レーン分割線の色','引导线颜色':'ガイド線の色','边缘颜色':'縁の色','判定线颜色':'判定ラインの色','轨道底图 PNG':'レーン画像 PNG','使用内置轨道':'内蔵レーンを使用','分道线 PNG':'レーン分割線 PNG','使用内置分道线':'内蔵分割線を使用',
      '启用 Live2D 模型':'Live2D モデルを有効化','导入模型文件夹':'モデルフォルダーを読み込む','BongoCat 参数':'BongoCat 設定','模型 .model3.json':'モデル .model3.json','选择已导入的模型':'読み込み済みモデルを選択','模型路径':'モデルパス','未导入模型':'モデル未読み込み','左右手参数映射':'左右手パラメータ割り当て','动作组与隐藏部件':'モーションと非表示パーツ','隐藏部件 ID（每行一个）':'非表示パーツ ID（1 行 1 件）','模型背景 / 平板':'モデル背景／タブレット','背景 PNG 路径':'背景 PNG パス','选择背景 PNG':'背景 PNG を選択',
      '个资源':'リソース','未修改':'未変更','本地资源 · 不执行包内代码':'ローカルリソース · パッケージ内コードは実行しません','未保存修改':'未保存の変更','打开新项目将丢失当前未保存的修改。':'新しいプロジェクトを開くと未保存の変更が失われます。','继续编辑':'編集を続ける','放弃修改并打开':'変更を破棄して開く'
    },
    en: {
      '语言':'Language','选择语言':'Choose language','打开皮肤文件夹':'Open skin folder','导入皮肤 ZIP':'Import skin ZIP','导入 JSON':'Import JSON','撤回':'Undo','取消撤回':'Redo','保存':'Save','导出 JSON':'Export JSON','导出皮肤 ZIP':'Export skin ZIP',
      '游玩界面布局':'Gameplay layout','游玩中':'Playing','横屏游玩界面预览':'Landscape gameplay preview','预览状态':'Preview state','暂停':'Pause','目标值归零':'Target reaches zero','目标值低于零':'Target below zero','自定义事件':'Custom event','分数 900000':'Score 900000','左上触控':'Top-left touch','游玩结束':'Finished','网格':'Grid','定位特效':'Place rule effect','试播匹配规则':'Preview matching rule','停止试播':'Stop preview',
      '皮肤名称':'Skin name','皮肤 ID':'Skin ID','元素图层':'Element layers','添加元素':'Add element','触控板':'Touchpad','AIR 区域':'AIR area','AIR 下边界':'AIR lower boundary','AIR 上边界':'AIR upper boundary','AIR 引导线':'AIR guide','轨道遮罩':'Lane mask','输入区域':'Input region','编辑区域':'Edit region','移除当前元素':'Remove selected element','只能导出游戏已支持的元素 ID；按钮只改变外观和位置，不会替换原有行为。':'Only element IDs supported by the game can be exported. Buttons change appearance and position without replacing behavior.',
      '属性':'Properties','界面布局':'Layout','规则积木':'Rule blocks','场景轨道':'Scene and lanes','请选择一个元素':'Select an element','元素 ID':'Element ID','显示此元素':'Show this element','宽度':'Width','高度':'Height','透明度':'Opacity','颜色':'Color','图片路径':'Image path','选择 PNG 图片':'Choose PNG','按钮/文字标签':'Button / text label','文字大小':'Text size','文字颜色':'Text color',
      '音符下方显示模式':'Below-note display mode','当前 Combo':'Current Combo','剩余 Combo':'Remaining Combo','当前分数':'Current score','SSS+ 剩余分数':'Score to SSS+','SSS 剩余分数':'Score to SSS','SS+ 剩余分数':'Score to SS+','SS 剩余分数':'Score to SS','S+ 剩余分数':'Score to S+','S 剩余分数':'Score to S','已损失分数':'Lost score','距离最好成绩':'Distance to best score','数字与文字样式':'Number and text style','Combo 字号':'Combo size','分数字号':'Score size','判定字号':'Judgement size','Combo 颜色':'Combo color','分数颜色':'Score color','判定颜色':'Judgement color','Combo 数字皮肤（0–9）':'Combo digit skin (0–9)','分数数字皮肤（0–9）':'Score digit skin (0–9)','未设置':'Not set',
      '触控板与触控爪皮肤':'Touchpad and touch-claw skin','启用触控板皮肤':'Enable touchpad skin','触控板 PNG':'Touchpad PNG','左爪 PNG':'Left claw PNG','右爪 PNG':'Right claw PNG','导入左爪 PNG':'Import left claw PNG','导入右爪 PNG':'Import right claw PNG','左爪宽度':'Left claw width','左爪高度':'Left claw height','右爪宽度':'Right claw width','右爪高度':'Right claw height','左爪偏移 X':'Left claw X offset','左爪偏移 Y':'Left claw Y offset','右爪偏移 X':'Right claw X offset','右爪偏移 Y':'Right claw Y offset','左爪平滑':'Left claw smoothing','右爪平滑':'Right claw smoothing',
      '可视化规则积木':'Visual rule blocks','添加规则':'Add rule','规则预设':'Rule preset','添加预设':'Add preset','场景与轨道':'Scene and lanes','导入场景 PNG':'Import scene PNG','游玩背景图片':'Gameplay background image','使用歌曲背景':'Use song background','背景颜色':'Background color','背景透明度':'Background opacity','轨道颜色':'Lane color','分道线颜色':'Lane divider color','引导线颜色':'Guide line color','边缘颜色':'Edge color','判定线颜色':'Judgement line color','轨道底图 PNG':'Lane image PNG','使用内置轨道':'Use built-in lane','分道线 PNG':'Lane divider PNG','使用内置分道线':'Use built-in divider',
      '启用 Live2D 模型':'Enable Live2D model','导入模型文件夹':'Import model folder','BongoCat 参数':'BongoCat settings','模型 .model3.json':'Model .model3.json','选择已导入的模型':'Choose imported model','模型路径':'Model path','未导入模型':'No model imported','左右手参数映射':'Left/right hand mapping','动作组与隐藏部件':'Motions and hidden parts','隐藏部件 ID（每行一个）':'Hidden part IDs (one per line)','模型背景 / 平板':'Model background / tablet','背景 PNG 路径':'Background PNG path','选择背景 PNG':'Choose background PNG',
      '个资源':'resources','未修改':'Not modified','本地资源 · 不执行包内代码':'Local assets · package code is never executed','未保存修改':'Unsaved changes','打开新项目将丢失当前未保存的修改。':'Opening a new project will discard unsaved changes.','继续编辑':'Continue editing','放弃修改并打开':'Discard and open'
    }
  };
  const extra = {
    'zh-TW': {'歌曲信息':'歌曲資訊','歌曲封面':'歌曲封面','分数 HUD':'分數 HUD','Combo HUD':'Combo HUD','判定区域':'判定區域','皮肤设置':'皮膚設定','左手 X':'左手 X','左手 Y':'左手 Y','左手显示':'左手顯示','左手按下':'左手按下','右手 X':'右手 X','右手 Y':'右手 Y','右手显示':'右手顯示','右手按下':'右手按下','左手 X 起点':'左手 X 起點','左手 X 终点':'左手 X 終點','左手 Y 起点':'左手 Y 起點','左手 Y 终点':'左手 Y 終點','右手 X 起点':'右手 X 起點','右手 X 终点':'右手 X 終點','右手 Y 起点':'右手 Y 起點','右手 Y 终点':'右手 Y 終點','匹配 0 条规则':'符合 0 條規則','个元素':'個元素','已保存 / 未修改':'已儲存／未修改'},
    ja: {'歌曲信息':'曲情報','歌曲封面':'ジャケット','分数 HUD':'スコア HUD','Combo HUD':'Combo HUD','判定区域':'判定エリア','皮肤设置':'スキン設定','左手 X':'左手 X','左手 Y':'左手 Y','左手显示':'左手表示','左手按下':'左手押下','右手 X':'右手 X','右手 Y':'右手 Y','右手显示':'右手表示','右手按下':'右手押下','左手 X 起点':'左手 X 開始','左手 X 终点':'左手 X 終了','左手 Y 起点':'左手 Y 開始','左手 Y 终点':'左手 Y 終了','右手 X 起点':'右手 X 開始','右手 X 终点':'右手 X 終了','右手 Y 起点':'右手 Y 開始','右手 Y 终点':'右手 Y 終了','匹配 0 条规则':'一致するルール 0 件','个元素':'要素','已保存 / 未修改':'保存済み／変更なし'},
    en: {'歌曲信息':'Song info','歌曲封面':'Song cover','分数 HUD':'Score HUD','Combo HUD':'Combo HUD','判定区域':'Judgement area','皮肤设置':'Skin settings','左手 X':'Left hand X','左手 Y':'Left hand Y','左手显示':'Left hand visible','左手按下':'Left hand pressed','右手 X':'Right hand X','右手 Y':'Right hand Y','右手显示':'Right hand visible','右手按下':'Right hand pressed','左手 X 起点':'Left hand X start','左手 X 终点':'Left hand X end','左手 Y 起点':'Left hand Y start','左手 Y 终点':'Left hand Y end','右手 X 起点':'Right hand X start','右手 X 终点':'Right hand X end','右手 Y 起点':'Right hand Y start','右手 Y 终点':'Right hand Y end','匹配 0 条规则':'0 matching rules','个元素':'elements','已保存 / 未修改':'Saved / unchanged'}
  };
  Object.entries(extra).forEach(([language, values]) => Object.assign(maps[language], values));
  // Keep a reverse index so switching away from Chinese and back can recover
  // the original source label instead of treating the previous translation
  // as a new source string.
  const reverseMaps = Object.fromEntries(Object.entries(maps).map(([language, values]) => {
    const reverse = {};
    Object.entries(values).forEach(([source, translated]) => { reverse[translated] = source; });
    return [language, reverse];
  }));
  // A node can currently contain a translation from any previously selected
  // language. Keep one global index so switching back to Simplified Chinese
  // can recover the original source label as well.
  const allReverse = {};
  Object.values(reverseMaps).forEach(reverse => Object.assign(allReverse, reverse));
  const sourceNodes = new WeakMap();
  let language = 'zh-CN';
  const tr = value => maps[language]?.[value] || value;
  function translateText(node) {
    if (!node?.nodeValue?.trim()) return;
    const raw = node.nodeValue.trim(), leading = node.nodeValue.slice(0, node.nodeValue.indexOf(raw)), trailing = node.nodeValue.slice(node.nodeValue.indexOf(raw) + raw.length);
    const summary = raw.match(/^(\d+) 个资源(?: · (\d+) 个元素)?$/);
    if (summary && language !== 'zh-CN') {
      const translatedSummary = language === 'zh-TW'
        ? `${summary[1]} 個資源${summary[2] ? ` · ${summary[2]} 個元素` : ''}`
        : language === 'ja'
          ? `${summary[1]} リソース${summary[2] ? ` · ${summary[2]} 要素` : ''}`
          : `${summary[1]} resources${summary[2] ? ` · ${summary[2]} elements` : ''}`;
      if (node.nodeValue !== leading + translatedSummary + trailing) node.nodeValue = leading + translatedSummary + trailing;
      return;
    }
    let source = sourceNodes.get(node);
    const recovered = reverseMaps[language]?.[raw] || allReverse[raw];
    if (recovered) source = recovered;
    if (!source) source = raw;
    sourceNodes.set(node, source);
    const translated = leading + tr(source) + trailing;
    if (node.nodeValue !== translated) node.nodeValue = translated;
  }
  function translateAttribute(element, name) {
    if (!element.hasAttribute(name)) return;
    const key = `studioI18n${name.replace(/[^a-z]/gi, '')}`;
    const source = element.dataset[key] || element.getAttribute(name);
    element.dataset[key] = source;
    const translated = tr(source);
    // Avoid writing the same attribute value back. MutationObserver watches
    // these attributes, so unconditional setAttribute creates a microtask loop.
    if (element.getAttribute(name) !== translated) element.setAttribute(name, translated);
  }
  function scan(root) {
    if (!root) return;
    if (root.nodeType === Node.TEXT_NODE) return translateText(root);
    if (root.nodeType !== Node.ELEMENT_NODE && root.nodeType !== Node.DOCUMENT_NODE) return;
    if (root.nodeType === Node.ELEMENT_NODE) ['title','aria-label','placeholder'].forEach(name => translateAttribute(root, name));
    const walker = document.createTreeWalker(root, NodeFilter.SHOW_TEXT);
    let node; while ((node = walker.nextNode())) translateText(node);
    root.querySelectorAll?.('[title],[aria-label],[placeholder]').forEach(element => ['title','aria-label','placeholder'].forEach(name => translateAttribute(element, name)));
  }
  function setLanguage(next) {
    language = maps[next] ? next : 'zh-CN';
    document.documentElement.lang = language;
    localStorage.setItem('chunithm-skin-studio-language', language);
    const select = document.getElementById('studioLanguageSelect');
    if (select) select.value = language;
    scan(document.body);
  }
  const select = document.getElementById('studioLanguageSelect');
  const stored = localStorage.getItem('chunithm-skin-studio-language');
  const detected = (navigator.languages || [navigator.language || 'zh-CN']).map(value => value.toLowerCase()).find(value => value.startsWith('zh-tw') || value.startsWith('zh-hant') || value.startsWith('ja') || value.startsWith('en') || value.startsWith('zh')) || 'zh-cn';
  const initial = stored || (detected.startsWith('zh-tw') || detected.startsWith('zh-hant') ? 'zh-TW' : detected.startsWith('ja') ? 'ja' : detected.startsWith('en') ? 'en' : 'zh-CN');
  select?.addEventListener('change', event => setLanguage(event.target.value));
  setLanguage(initial);
  new MutationObserver(records => records.forEach(record => {
    if (record.type === 'characterData') translateText(record.target);
    if (record.type === 'childList') record.addedNodes.forEach(node => scan(node));
    if (record.type === 'attributes') translateAttribute(record.target, record.attributeName);
  })).observe(document.body, { childList: true, subtree: true, characterData: true, attributes: true, attributeFilter: ['title','aria-label','placeholder'] });
  window.SkinStudioI18n = { setLanguage, get language() { return language; } };
})();
