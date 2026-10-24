/*
 * The studio only writes the game's declarative skin format. It never loads or
 * executes package code; the ZIP writer stores JSON and user-selected PNG data.
 */
(() => {
  const ids = ["songInfo","songCover","scoreHud","comboHud","judgement","judgementImage","pauseButton","pausePanel","progressBar","fps","countdown","touchpad","airZone","airBoundaryBottom","airBoundaryTop","airGuide","trackMask","touchFeedback"];
  const labels = {songInfo:"歌曲信息",songCover:"歌曲封面",scoreHud:"分数 HUD",comboHud:"Combo HUD",judgement:"判定区域",judgementImage:"判定图片",pauseButton:"暂停按钮",pausePanel:"暂停面板",progressBar:"进度条",fps:"帧数",countdown:"倒计时",touchpad:"触控板",airZone:"AIR 区域",airBoundaryBottom:"AIR 下边界",airBoundaryTop:"AIR 上边界",airGuide:"AIR 引导线",trackMask:"轨道遮罩",touchFeedback:"触控反馈"};
  const defaults = {songInfo:[.03,.72,.28,.14],songCover:[.04,.73,.10,.12],scoreHud:[.77,.80,.21,.18],comboHud:[.80,.84,.16,.10],judgement:[.34,.42,.32,.20],judgementImage:[.40,.48,.20,.10],pauseButton:[.018,.88,.06,.08],pausePanel:[.34,.24,.32,.52],progressBar:[.05,.96,.90,.008],fps:[.02,.02,.14,.04],countdown:[.38,.36,.24,.30],touchpad:[.20,.04,.60,.34],airZone:[.05,.55,.90,.34],airBoundaryBottom:[.05,.52,.90,.008],airBoundaryTop:[.05,.88,.90,.008],airGuide:[.05,.70,.90,.008],trackMask:[.05,.72,.90,.20],touchFeedback:[0,0,1,1]};
  const metricText = {combo:"显示当前 Combo；分数区域显示正常分数。",remaining_combo:"显示距离满 Combo 还剩多少。",score:"显示从 0 增加的当前分数。", "sss+":"显示距离 SSS+ 还剩多少分。",sss:"显示距离 SSS 还剩多少分。","ss+":"显示距离 SS+ 还剩多少分。",ss:"显示距离 SS 还剩多少分。","s+":"显示距离 S+ 还剩多少分。",s:"显示距离 S 还剩多少分。",lost_score:"只累计显示已经损失的分数。",best_score:"显示 1,010,000 减去当前最好成绩后的目标差值。"};
  const state = {elements:[],selected:null,metric:"combo",customDisplays:[],scene:{},files:new Map(),preview:"playing",document:{},
    manifest:{id:"studio_"+crypto.randomUUID().replace(/-/g,""),name:"Skin Studio export",gameplaySkin:true,formatVersion:1},busy:false};
  const history = {undo:[],redo:[],editing:false,finishTimer:null};
  let stageUrls=[];
  let previewAnimations=[];
  let livePanel;
  let savedFingerprint=null;
  const fileIds=new WeakMap();let nextFileId=0;
  const $ = id => document.getElementById(id);
  const escapeHtml=value=>String(value??"").replace(/[&<>"']/g,char=>({"&":"&amp;","<":"&lt;",">":"&gt;",'"':"&quot;","'":"&#39;"}[char]));
  // Inline feedback stays usable in browser and desktop windows, without a
  // blocking native alert keeping the inspector/export controls locked.
  function notify(message,error=false){$("operationStatus").textContent=message;$("operationStatus").classList.toggle("invalid",error);}
  function clone(value){return JSON.parse(JSON.stringify(value));}
  function snapshot(){return {elements:clone(state.elements),selected:state.selected,selectedRule:state.selectedRule,metric:state.metric,customDisplays:clone(state.customDisplays),scene:clone(state.scene),document:clone(state.document),manifest:clone(state.manifest),files:[...state.files.entries()],live2d:clone(livePanel?.snapshot?.()||{})};}
  function fingerprint(){
    // File identity detects replacing a PNG with different bytes but the same
    // filename/size. Selection and preview changes never dirty the document.
    const value=snapshot();delete value.selected;delete value.selectedRule;
    value.files=value.files.map(([path,file])=>{if(!fileIds.has(file))fileIds.set(file,++nextFileId);return [path,fileIds.get(file)];}).sort((a,b)=>a[0].localeCompare(b[0]));
    return JSON.stringify(value);
  }
  const hasUnsavedChanges=()=>savedFingerprint!==null&&savedFingerprint!==fingerprint();
  function updateProjectUi(){const dirty=hasUnsavedChanges();$("saveStatus").textContent=dirty?"有未保存修改":"已保存 / 未修改";$("saveStatus").dataset.dirty=String(dirty);}
  function markSaved(revision=fingerprint()){savedFingerprint=revision;updateProjectUi();}
  async function confirmReplace(){
    if(!hasUnsavedChanges())return true;
    const dialog=$("replaceProjectDialog");dialog.returnValue="";
    const answer=new Promise(resolve=>dialog.addEventListener("close",()=>resolve(dialog.returnValue==="discard"),{once:true}));dialog.showModal();return answer;
  }
  $("cancelReplace").onclick=()=>$("replaceProjectDialog").close("cancel");$("discardProject").onclick=()=>$("replaceProjectDialog").close("discard");
  window.addEventListener("beforeunload",event=>{if(hasUnsavedChanges()){event.preventDefault();event.returnValue="";}});
  document.addEventListener("input",()=>queueMicrotask(updateProjectUi));
  function updateHistoryUi(){
    $("undoButton").disabled=!history.undo.length;$("redoButton").disabled=!history.redo.length;
    $("historyStatus").textContent=history.undo.length?`可撤回 ${history.undo.length}/20 次`:"未修改";
    updateProjectUi();
  }
  function beginEdit(){
    if(!history.editing){history.undo.push(snapshot());if(history.undo.length>20)history.undo.shift();history.redo=[];history.editing=true;updateHistoryUi();}
    clearTimeout(history.finishTimer);history.finishTimer=setTimeout(()=>{history.editing=false;},350);
  }
  function finishEdit(){history.editing=false;clearTimeout(history.finishTimer);updateHistoryUi();}
  function restore(value){
    state.elements=clone(value.elements);state.selectedRule=value.selectedRule;state.metric=value.metric;state.customDisplays=clone(value.customDisplays);state.scene=clone(value.scene||{});state.document=clone(value.document);state.manifest=clone(value.manifest);state.files.clear();for(const [path,file] of (value.files||[]))state.files.set(path,file);
    $("skinName").value=state.manifest.name||"";$("skinId").value=state.manifest.id||"";livePanel?.set(value.live2d);state.selected=find(value.selected)?.id||state.elements[0]?.id||null;render();
  }
  function undo(){if(!history.undo.length)return;history.redo.push(snapshot());restore(history.undo.pop());finishEdit();notify("已撤回");}
  function redo(){if(!history.redo.length)return;history.undo.push(snapshot());restore(history.redo.pop());finishEdit();notify("已取消撤回");}
  // Keep Live2D settings discoverable without scrolling past the UI inspector.
  function setSettingsTab(tab) {
    for(const [key,button,panel] of [["layout","layoutTab","layoutSettings"],["live2d","liveTab","liveSettings"],["rules","ruleTab","ruleSettings"],["scene","sceneTab","sceneSettings"]]){
      $(panel).hidden=key!==tab;$(button).setAttribute("aria-selected",String(key===tab));
    }
  }
  $("layoutTab").onclick=()=>setSettingsTab("layout");$("liveTab").onclick=()=>setSettingsTab("live2d");
  $("ruleTab").onclick=()=>setSettingsTab("rules");$("sceneTab").onclick=()=>setSettingsTab("scene");
  $("layerList").addEventListener("click",()=>setSettingsTab("layout"));
  $("stage").addEventListener("click",event=>{if(event.target.closest(".skin-element"))setSettingsTab("layout");});
  $("addElement").addEventListener("click",()=>setSettingsTab("layout"));
  const cloneDefault = id => { const r = defaults[id] || [.1,.1,.1,.1]; return {id,visible:true,x:r[0],y:r[1],width:r[2],height:r[3],opacity:1,color:"",image:"",label:""}; };
  const find = id => state.elements.find(e => e.id === id);
  const ensure = id => { let e=find(id); if(!e){e=cloneDefault(id);state.elements.push(e)} return e; };
  function selectElement(id,record=true){if(!find(id)||state.selected===id)return;if(record){beginEdit();}state.selected=id;render();if(record)finishEdit();}
  function renderLayers(){ const list=$("layerList"); list.innerHTML=""; state.elements.forEach(e=>{ const row=document.createElement("div");row.className="layer-row"+(state.selected===e.id?" active":"");const select=document.createElement("button");select.className="layer-select";select.textContent=labels[e.id]||e.id;select.onclick=()=>selectElement(e.id);const eye=document.createElement("button");eye.className="eye";eye.title=e.visible?"隐藏元素":"显示元素";eye.setAttribute("aria-pressed",String(e.visible));eye.textContent=e.visible?"●":"○";eye.onclick=event=>{event.stopPropagation();beginEdit();e.visible=!e.visible;render();finishEdit()};row.append(select,eye);list.appendChild(row)}); $("fileSummary").textContent=`${state.files.size} 个资源 · ${state.elements.length} 个元素`; }
  function previewEvent(){
    const events={combo100:{trigger:"combo",previous:99,current:100},metricZero:{trigger:"displayed_metric",previous:1,current:0,mode:state.metric},metricNegative:{trigger:"displayed_metric",previous:0,current:-1,mode:state.metric},miss:{trigger:"judgement",value:"miss"},air:{trigger:"air",value:"active"},playing:{trigger:"gameplay",value:"start"},score900000:{trigger:"score",previous:899999,current:900000},rankSSS:{trigger:"rank",value:"sss+"},touchLeft:{trigger:"touch",value:"lefttop"},stop:{trigger:"gameplay",value:"stop"}};
    if(state.preview!=="custom")return events[state.preview]||{};
    if(["combo","score","displayed_metric"].includes($("previewTrigger").value)&&!["previewPrevious","previewCurrent"].every(id=>$(id).value!==""&&$(id).validity.valid))return {invalid:true};
    return {trigger:$("previewTrigger").value,previous:Number($("previewPrevious").value),current:Number($("previewCurrent").value),value:$("previewValue").value,mode:$("previewMode").value,exit:$("previewEdge").value==="exit"};
  }
  const ruleMatchesPreview=rule=>window.SkinStudioRules.matches(rule,previewEvent());
  function cancelPreview(){
    // Detached previews must not retain a previous project's image resources.
    for(const animation of previewAnimations)animation.cancel();previewAnimations=[];$("stopPreview").disabled=true;
  }
  function replayPreview(){
    $("placeRule").checked=false;
    renderStage();
    try{window.SkinStudioRules.validate(state.customDisplays);}catch(error){notify("无法试播："+error.message,true);return;}
    const animations=[];
    for(const node of $("stage").querySelectorAll(".rule-preview")){
      const rule=state.customDisplays.find(item=>item.id===node.dataset.ruleId),timing=window.SkinStudioRules.animation(rule);
      node.style.opacity="0";
      const animation=node.animate(timing.frames,{duration:timing.duration,easing:"linear",fill:"none"});animations.push(animation);
      animation.finished.then(()=>{if(previewAnimations===animations&&animations.every(item=>item.playState==="finished"))$("stopPreview").disabled=true;}).catch(()=>{});
    }
    previewAnimations=animations;$("stopPreview").disabled=!animations.length;
  }
  function renderRulePreview(stage){
    cancelPreview();
    $("statusBadge").textContent=$("previewState").selectedOptions[0]?.textContent||"游玩中";
    const sceneConfig=scene();
    stage.style.backgroundColor=sceneConfig.background.color||"#0b1c28";
    stage.querySelectorAll(".scene-background").forEach(node=>node.remove());
    if(sceneConfig.background.image&&state.files.has(sceneConfig.background.image)){
      const image=document.createElement("img");image.className="scene-background";image.src=URL.createObjectURL(state.files.get(sceneConfig.background.image));stageUrls.push(image.src);image.style.opacity=String(sceneConfig.background.opacity??1);stage.prepend(image);
    }
    stage.querySelectorAll(".lane").forEach(lane=>{lane.style.backgroundColor=sceneConfig.track.color||"";lane.style.borderColor=sceneConfig.track.edgeColor||"";lane.style.backgroundImage="";if(sceneConfig.track.image&&state.files.has(sceneConfig.track.image)){const url=URL.createObjectURL(state.files.get(sceneConfig.track.image));stageUrls.push(url);lane.style.backgroundImage=`url("${url}")`;lane.style.backgroundSize="100% 100%";}});
    stage.querySelector(".goal-line").style.backgroundColor=sceneConfig.track.judgementLineColor||"";
    stage.querySelectorAll(".rule-preview").forEach(n=>n.remove());
    const event=previewEvent(),matched=state.customDisplays.filter(ruleMatchesPreview),motions=matched.filter(rule=>rule.action==="live2dMotion");
    const placement=$("placeRule").checked?state.customDisplays.find(rule=>rule.id===state.selectedRule&&rule.action!=="live2dMotion"):null;
    if(Number.isFinite(event.current))$("metricDemo").textContent=`${event.trigger==="displayed_metric"?metricNames[event.mode]||event.mode:event.trigger==="score"?"分数":"COMBO"} ${event.current}`;
    $("airDemo").style.display=event.trigger==="air"&&!event.exit?"block":"none";
    $("previewResult").textContent=event.invalid?"测试值需要为范围内的整数":`匹配 ${matched.length} 条规则`+(motions.length?` · Live2D 动作组：${motions.map(rule=>rule.motion||"未设置").join("、")}（游戏内播放）`:"");
    const displayed=matched.filter(rule=>rule!==placement).slice(0,placement?23:24);if(placement)displayed.push(placement);
    displayed.forEach(rule=>{
      const editing=rule===placement,hasImage=rule.image&&state.files.has(rule.image);
      if(rule.action==="live2dMotion"||!editing&&!hasImage)return;
      const n=document.createElement("div");n.className="rule-preview"+(editing?" rule-placement":"");n.dataset.ruleId=rule.id;
      Object.assign(n.style,{left:`${Number(rule.x||0)*100}%`,bottom:`${Number(rule.y||0)*100}%`,width:`${Number(rule.width||.2)*100}%`,height:`${Number(rule.height||.2)*100}%`,opacity:String(rule.opacity??1),transform:`rotate(${-Number(rule.rotation||0)}deg) scale(${Number(rule.scaleTo)||1})`});
      if(hasImage){const img=document.createElement("img");img.src=URL.createObjectURL(state.files.get(rule.image));stageUrls.push(img.src);n.appendChild(img);}else n.textContent="特效图层";
      if(editing){const handle=document.createElement("button");handle.type="button";handle.className="rule-resize";handle.title="调整特效尺寸";handle.setAttribute("aria-label","调整特效尺寸");n.appendChild(handle);}
      stage.appendChild(n);
    });
  }
  function renderStage(){ const stage=$("stage");stageUrls.forEach(URL.revokeObjectURL);stageUrls=[]; stage.querySelectorAll(".skin-element").forEach(n=>n.remove()); state.elements.forEach((e,index)=>{ const n=document.createElement("div"); n.className="skin-element"+(state.selected===e.id?" selected":""); n.dataset.id=e.id; n.style.zIndex=state.selected===e.id?100:index+1; n.textContent=e.label||labels[e.id]||e.id; n.style.left=`${e.x*100}%`;n.style.bottom=`${e.y*100}%`;n.style.width=`${e.width*100}%`;n.style.height=`${e.height*100}%`;n.style.opacity=e.visible?e.opacity:0;if(e.color)n.style.color=e.color;if(e.textColor)n.style.color=e.textColor;if(e.textSize)n.style.fontSize=e.textSize+"px";if(e.image&&state.files.has(e.image)){const img=document.createElement("img");img.src=URL.createObjectURL(state.files.get(e.image));stageUrls.push(img.src);n.textContent="";n.appendChild(img)}n.style.pointerEvents=e.visible?"auto":"none";stage.appendChild(n)}); $("airDemo").style.display=state.preview==="air"?"block":"none"; $("metricDemo").textContent=state.preview==="metricZero"?"SSS+ 剩余 0":state.preview==="miss"?"MISS":state.preview==="combo100"?"COMBO 100":"COMBO 0"; $("statusBadge").textContent={playing:"游玩中",pause:"暂停",miss:"MISS",combo100:"Combo 100",air:"AIR",metricZero:"目标值归零"}[state.preview]; document.querySelector(".stage-grid").style.display=$("showGrid").checked?"block":"none"; renderRulePreview(stage); }
  function hitTest(x,y){return state.elements.filter(e=>e.visible&&x>=e.x&&x<=e.x+e.width&&y>=e.y&&y<=e.y+e.height);}
  let drag=null;
  function stagePoint(event){const rect=$("stage").getBoundingClientRect();return {x:(event.clientX-rect.left)/rect.width,y:1-(event.clientY-rect.top)/rect.height};}
  $("stage").addEventListener("pointerdown",event=>{
    if(event.button!==0||drag)return;const point=stagePoint(event),ruleTarget=event.target.closest?.(".rule-placement");
    finishEdit();
    const hits=hitTest(point.x,point.y),selected=find(state.selected),target=event.target.closest?.(".skin-element");
    // A selected overlapping layer wins even if the DOM hit was its child or
    // another element. Rule placement uses a separate explicit editing mode.
    const rule=ruleTarget?state.customDisplays.find(item=>item.id===ruleTarget.dataset.ruleId):null;
    const chosen=rule||(selected&&hits.includes(selected)?selected:target?find(target.dataset.id):hits[hits.length-1]);if(!chosen)return;
    if(rule){state.selectedRule=chosen.id;setSettingsTab("rules");}else{if(state.selected!==chosen.id)beginEdit();state.selected=chosen.id;setSettingsTab("layout");}
    const resize=!!event.target.closest?.(".rule-resize");
    drag={id:chosen.id,kind:rule?"rule":"element",offsetX:point.x-chosen.x,offsetY:point.y-chosen.y,active:resize,changed:false,pointerId:event.pointerId};
    drag.resize=resize;
    const activate=()=>{if(!drag)return;drag.active=true;$("stage").dataset.dragging="true";try{$("stage").setPointerCapture?.(event.pointerId);}catch(_){/* synthetic tests have no active pointer */}};
    if(resize)activate();else drag.timer=setTimeout(activate,260);
    renderStage();if(rule)renderRuleEditor();else fillInspector();event.preventDefault();
  });
  document.addEventListener("pointermove",event=>{
    if(!drag||drag.pointerId!==event.pointerId||!drag.active)return;const point=stagePoint(event);
    const e=drag.kind==="rule"?state.customDisplays.find(rule=>rule.id===drag.id):find(drag.id);if(!e)return;
    const values=drag.resize?{width:Math.min(1-e.x,Math.max(.01,point.x-e.x)),height:Math.min(1-e.y,Math.max(.01,point.y-e.y))}:{x:Math.min(1-e.width,Math.max(0,point.x-drag.offsetX)),y:Math.min(1-e.height,Math.max(0,point.y-drag.offsetY))};
    if(Object.entries(values).some(([key,value])=>value!==e[key])){if(!drag.changed){beginEdit();drag.changed=true;}Object.assign(e,values);renderStage();if(drag.kind==="rule")renderRuleEditor();else fillInspector();updateProjectUi();}event.preventDefault();
  });
  document.addEventListener("pointerup",event=>{if(!drag||drag.pointerId!==event.pointerId)return;clearTimeout(drag.timer);const wasDragging=drag.active;drag=null;delete $("stage").dataset.dragging;if(wasDragging)finishEdit();else render();});
  document.addEventListener("pointercancel",event=>{if(drag&&drag.pointerId===event.pointerId){clearTimeout(drag.timer);drag=null;finishEdit();}});
  function fillInspector(){ const e=find(state.selected); $("emptyInspector").hidden=!!e;$("inspector").hidden=!e;if(!e)return; $("elementId").innerHTML=ids.map(id=>`<option value="${id}">${labels[id]}</option>`).join("");$("elementId").value=e.id;$("visible").checked=!!e.visible;["x","y","width","height","opacity","color","image","label","textSize","textColor"].forEach(k=>{$(k).value=e[k]??""});$("opacityValue").value=Number(e.opacity).toFixed(2);$("metricMode").value=state.metric;$("metricDescription").textContent=metricText[state.metric]||""; }
  function render(){renderLayers();renderStage();fillInspector();renderRules();renderScene();renderNumberStyles();renderTouchOverlay();livePanel?.render();updateProjectUi()}
  $("addRegion").onclick=()=>{beginEdit();const id=$("regionPreset").value;ensure(id);state.selected=id;setSettingsTab("layout");render();finishEdit();};
  function bindField(id,key,parse){const update=()=>{const e=find(state.selected);if(!e)return;const value=parse?parse($(id).value):$(id).value;if(Object.is(e[key],value))return;beginEdit();e[key]=value;if(key==="opacity")$("opacityValue").value=Number(e[key]).toFixed(2);renderStage()};$(id).addEventListener("input",update);$(id).addEventListener("change",()=>{update();finishEdit()})}
  ["x","y","width","height","opacity","textSize"].forEach(k=>bindField(k,k,Number));["color","image","label","textColor"].forEach(k=>bindField(k,k));
  $("visible").addEventListener("change",()=>{const e=find(state.selected);if(e){beginEdit();e.visible=$("visible").checked;render();finishEdit()}});
  $("elementId").addEventListener("change",()=>{const current=find(state.selected);const replacement=$("elementId").value;if(!current||find(replacement))return;beginEdit();current.id=replacement;state.selected=replacement;render();finishEdit()});
  $("metricMode").addEventListener("change",()=>{beginEdit();state.metric=$("metricMode").value;$("metricDescription").textContent=metricText[state.metric]||"";finishEdit()});
  // The rule editor deliberately maps one small set of blocks to the runtime
  // contract. It keeps skin creation approachable while avoiding arbitrary
  // scripts, shader code, or callbacks inside imported packages.
  const triggerNames={combo:"Combo",score:"分数",rank:"评级",judgement:"判定",air:"AIR",touch:"触控区域",gameplay:"游玩状态",displayed_metric:"当前显示模式"};
  const triggerValues={rank:[["sss+","SSS+"],["sss","SSS"],["ss+","SS+"],["ss","SS"],["s+","S+"],["s","S"],["aaa","AAA"]],judgement:[["justice_critical","JC"],["justice","JUSTICE"],["attack","ATTACK"],["miss","MISS"]],air:[["active","有 AIR"],["rising","上升 AIR"],["falling","下降 AIR"],["enter","进入 AIR"],["exit","离开 AIR"]],touch:[["any","任意区域"],["lefttop","左上"],["leftbottom","左下"],["righttop","右上"],["rightbottom","右下"]],gameplay:[["start","开始游玩"],["stop","结束游玩"]],displayed_metric:[["combo","Combo"],["remaining_combo","剩余 Combo"],["score","分数"],["sss+","SSS+ 剩余"],["sss","SSS 剩余"]]};
  triggerValues.rank=window.SkinStudioRules.values.rank.map(value=>[value,value.toUpperCase()]);
  triggerValues.air.push(...Array.from({length:6},(_,index)=>["sensor"+(index+1),"第 "+(index+1)+" 层 AIR"]));
  const triggerConditions={combo:[["greater_or_equal","达到"],["equal","等于"],["less","小于"],["less_or_equal","小于等于"]],score:[["greater_or_equal","达到"],["equal","等于"],["less","小于"],["less_or_equal","小于等于"]],displayed_metric:[["greater_or_equal","达到"],["equal","等于"],["less","小于"],["less_or_equal","小于等于"]],rank:[["active","进入评级"]],judgement:[["active","出现判定"]],air:[["active","进入状态"],["exit","离开状态"]],touch:[["active","按下"],["exit","松开"]],gameplay:[["active","状态变化"]]};
  const metricNames={any:"任意模式",...Object.fromEntries([...$("metricMode").options].map(option=>[option.value,option.textContent]))};
  function selectOptions(options,value){return options.map(([key,label])=>`<option value="${escapeHtml(key)}"${key===value?" selected":""}>${escapeHtml(label)}</option>`).join("");}
  $("previewTrigger").innerHTML=selectOptions(Object.entries(triggerNames),"combo");
  $("previewMode").innerHTML=selectOptions(window.SkinStudioRules.modes.filter(mode=>mode!=="any").map(mode=>[mode,metricNames[mode]||mode]),"sss+");
  function renderPreviewFields(){
    $("previewEventFields").hidden=state.preview!=="custom";
    const trigger=$("previewTrigger").value,numeric=["combo","score","displayed_metric"].includes(trigger);
    $("previewPreviousField").hidden=$("previewCurrentField").hidden=!numeric;
    $("previewValueField").hidden=numeric;$("previewModeField").hidden=trigger!=="displayed_metric";
    $("previewEdgeField").hidden=!["air","touch"].includes(trigger);
    $("previewValue").innerHTML=selectOptions(numeric?[]:triggerValues[trigger]||[],"");
  }
  $("previewTrigger").onchange=()=>{renderPreviewFields();renderStage();};
  for(const id of ["previewValue","previewMode","previewEdge","previewPrevious","previewCurrent"])$(id).addEventListener("input",renderStage);
  $("replayPreview").onclick=replayPreview;$("stopPreview").onclick=()=>renderStage();renderPreviewFields();
  $("placeRule").onchange=()=>{if($("placeRule").checked)setSettingsTab("rules");renderStage();};
  function newRule(){return {id:"rule_"+crypto.randomUUID().replaceAll("-",""),enabled:true,trigger:"combo",condition:"greater_or_equal",value:"",threshold:100,image:"",x:.40,y:.40,width:.20,height:.20,duration:1.2,fadeIn:.12,fadeOut:.35,scaleFrom:.8,scaleTo:1,rotation:0,rotationTo:0,animateRotation:false,opacity:1,mode:"any"};}
  function ruleResourceOptions(selected,empty="选择图片资源"){
    const files=[...state.files.keys()].filter(path=>/\.png$/i.test(path)).sort();return `<option value="">${escapeHtml(empty)}</option>`+files.map(path=>`<option value="${escapeHtml(path)}"${path===selected?" selected":""}>${escapeHtml(path)}</option>`).join("");
  }
  function renderRuleEditor(){
    const host=$("ruleEditor"),rule=state.customDisplays.find(item=>item.id===state.selectedRule);if(!host)return;
    host.hidden=!rule;if(!rule){host.innerHTML="";return;}
    const values=triggerValues[rule.trigger]||[];const conditions=triggerConditions[rule.trigger]||[];const numeric=["combo","score","displayed_metric"].includes(rule.trigger);
    host.innerHTML=`<div class="rule-block-title">当……</div><label class="check"><input data-rule="enabled" type="checkbox" ${rule.enabled!==false?"checked":""}>启用这条规则</label>
      <label class="field"><span>触发来源</span><select data-rule="trigger">${selectOptions(Object.entries(triggerNames).map(([key,label])=>[key,label]),rule.trigger)}</select></label>
      <label class="field"><span>条件</span><select data-rule="condition">${selectOptions(conditions,rule.condition)}</select></label>
      ${numeric?`<label class="field"><span>数值</span><input data-rule="threshold" type="number" value="${Number(rule.threshold)||0}" step="1"> </label>`:`<label class="field"><span>目标</span><select data-rule="value">${selectOptions(values,rule.value)}</select></label>`}
      ${rule.trigger==="displayed_metric"?`<label class="field"><span>显示模式</span><select data-rule="mode">${selectOptions(window.SkinStudioRules.modes.map(key=>[key,metricNames[key]||key]),rule.mode||"any")}</select></label>`:""}
      <div class="rule-block-title">然后……</div><label class="field"><span>动作</span><select data-rule="action">${selectOptions([["image","显示图片特效"],["live2dMotion","播放 Live2D 动作"]],rule.action||"image")}</select></label>
      ${rule.action==="live2dMotion"?`<label class="field"><span>模型动作组</span><input data-rule="motion" type="text" maxlength="64" list="liveMotionOptions" value="${escapeHtml(rule.motion)}"></label>`:`<label class="field"><span>图片资源</span><select data-rule="image">${ruleResourceOptions(rule.image)}</select></label><label class="button secondary upload">导入特效 PNG<input id="ruleImageFile" type="file" accept="image/png" hidden></label>`}
      <div class="rule-image-settings" ${rule.action==="live2dMotion"?"hidden":""}>
      <div class="field-grid"><label class="field"><span>X</span><input data-rule="x" type="number" min="0" max="1" step=".001" value="${Number(rule.x)||0}"></label><label class="field"><span>Y</span><input data-rule="y" type="number" min="0" max="1" step=".001" value="${Number(rule.y)||0}"></label><label class="field"><span>宽度</span><input data-rule="width" type="number" min=".01" max="1" step=".001" value="${Number(rule.width)||.2}"></label><label class="field"><span>高度</span><input data-rule="height" type="number" min=".01" max="1" step=".001" value="${Number(rule.height)||.2}"></label></div>
      <div class="field-grid"><label class="field"><span>持续秒数</span><input data-rule="duration" type="number" min=".05" max="30" step=".05" value="${Number(rule.duration)||1.2}"></label><label class="field"><span>透明度</span><input data-rule="opacity" type="number" min="0" max="1" step=".01" value="${Number(rule.opacity??1)}"></label><label class="field"><span>淡入秒数</span><input data-rule="fadeIn" type="number" min="0" max="30" step=".05" value="${Number(rule.fadeIn)||0}"></label><label class="field"><span>淡出秒数</span><input data-rule="fadeOut" type="number" min="0" max="30" step=".05" value="${Number(rule.fadeOut)||0}"></label><label class="field"><span>起始缩放</span><input data-rule="scaleFrom" type="number" min=".01" max="4" step=".05" value="${Number(rule.scaleFrom)||.8}"></label><label class="field"><span>结束缩放</span><input data-rule="scaleTo" type="number" min=".01" max="4" step=".05" value="${Number(rule.scaleTo)||1}"></label></div>
      <label class="check"><input data-rule="animateRotation" type="checkbox" ${rule.animateRotation?"checked":""}>播放旋转动画</label><div class="field-grid"><label class="field"><span>起始角度</span><input data-rule="rotation" type="number" min="-360" max="360" step="1" value="${Number(rule.rotation)||0}"></label><label class="field"><span>结束角度</span><input data-rule="rotationTo" type="number" min="-360" max="360" step="1" value="${Number(rule.rotationTo)||0}"></label></div></div>`;
  }
  function renderRules(){
    const list=$("ruleList");if(!list)return;list.innerHTML="";
    state.customDisplays.forEach((rule,index)=>{
      const row=document.createElement("div");row.className="rule-row"+(state.selectedRule===rule.id?" active":"");
      const target=["combo","score","displayed_metric"].includes(rule.trigger)?`${Number(rule.threshold)||0}`:(triggerValues[rule.trigger]||[]).find(item=>item[0]===rule.value)?.[1]||rule.value||"任意";
      const result=rule.action==="live2dMotion"?"Live2D: "+(rule.motion||"未设置动作"):(rule.image?.split("/").pop()||"未选择图片");
      row.innerHTML=`<button type="button" class="rule-select"><b>${index+1}. 当 ${escapeHtml(triggerNames[rule.trigger]||rule.trigger)}</b><span>${escapeHtml(triggerConditions[rule.trigger]?.find(item=>item[0]===rule.condition)?.[1]||"触发")} ${escapeHtml(target)} → ${escapeHtml(result)}</span></button><button type="button" class="rule-copy" title="复制规则">复制</button><button type="button" class="rule-delete danger" title="删除规则">删除</button>`;
      row.querySelector(".rule-select").onclick=()=>{state.selectedRule=rule.id;renderRules();renderStage()};
      row.querySelector(".rule-copy").onclick=()=>{if(state.customDisplays.length>=48){notify("最多 48 条规则",true);return;}beginEdit();const copy=clone(rule);copy.id="rule_"+crypto.randomUUID().replaceAll("-","");state.customDisplays.splice(index+1,0,copy);state.selectedRule=copy.id;renderRules();renderStage();finishEdit()};
      row.querySelector(".rule-delete").onclick=()=>{beginEdit();state.customDisplays=state.customDisplays.filter(item=>item.id!==rule.id);state.selectedRule=state.customDisplays[0]?.id||null;renderRules();renderStage();finishEdit()};list.appendChild(row);
    });renderRuleEditor();
  }
  $("addRule").onclick=()=>{if(state.customDisplays.length>=48){notify("最多 48 条规则",true);return;}beginEdit();const rule=newRule();state.customDisplays.push(rule);state.selectedRule=rule.id;renderRules();renderStage();finishEdit()};
  $("addRulePreset").onclick=()=>{
    if(state.customDisplays.length>=48){notify("最多 48 条规则",true);return;}
    beginEdit();const rule=newRule();const preset=$("rulePreset").value;
    if(preset==="target")Object.assign(rule,{trigger:"displayed_metric",mode:"sss+",condition:"less",threshold:0,x:0,y:0,width:1,height:1,opacity:.5});
    if(preset==="miss")Object.assign(rule,{trigger:"judgement",condition:"active",value:"miss"});
    if(preset==="air")Object.assign(rule,{trigger:"air",condition:"active",value:"active"});
    if(preset==="touch")Object.assign(rule,{trigger:"touch",condition:"active",value:"lefttop"});
    state.customDisplays.push(rule);state.selectedRule=rule.id;renderRules();renderStage();finishEdit();
  };
  $("ruleEditor").addEventListener("input",event=>{
    const field=event.target.dataset.rule,rule=state.customDisplays.find(item=>item.id===state.selectedRule);if(!field||!rule)return;
    beginEdit();rule[field]=event.target.type==="checkbox"?event.target.checked:event.target.type==="number"?Number(event.target.value):event.target.value;
    if(field==="trigger"){rule.value=triggerValues[rule.trigger]?.[0]?.[0]||"";if(rule.trigger==="displayed_metric")rule.value="";rule.condition=(triggerConditions[rule.trigger]||[])[0]?.[0]||"active";}
    renderStage();
  });
  $("ruleEditor").addEventListener("change",event=>{if(event.target.dataset.rule){if(["trigger","action"].includes(event.target.dataset.rule))renderRules();else renderStage();finishEdit();}});
  $("ruleEditor").addEventListener("change",async event=>{
    if(event.target.id!=="ruleImageFile"||!event.target.files[0])return;
    const rule=state.customDisplays.find(item=>item.id===state.selectedRule);if(!rule)return;
    try{const file=event.target.files[0];await livePanel.validateFile(file);beginEdit();const path=`ui/custom/${rule.id}.png`;state.files.set(path,file);rule.image=path;render();finishEdit();notify("规则图片已导入");}catch(error){notify(error.message,true);}
  });
  function scene(){if(!state.scene||typeof state.scene!=="object")state.scene={};if(!state.scene.background)state.scene.background={};if(!state.scene.track)state.scene.track={};return state.scene;}
  const sceneMap={sceneBackgroundColor:["background","color"],sceneBackgroundOpacity:["background","opacity"],sceneTrackColor:["track","color"],sceneLaneColor:["track","laneColor"],sceneGuideColor:["track","guideColor"],sceneEdgeColor:["track","edgeColor"],sceneJudgementLineColor:["track","judgementLineColor"],sceneBackgroundImage:["background","image"],sceneTrackImage:["track","image"],sceneLaneImage:["track","laneImage"]};
  function renderScene(){const current=scene();for(const [id,[group,key]] of Object.entries(sceneMap)){const node=$(id);if(!node)continue;if(node.tagName==="SELECT"){const chosen=current[group][key]||"";node.innerHTML=ruleResourceOptions(chosen,id==="sceneBackgroundImage"?"使用歌曲背景":id==="sceneTrackImage"?"使用内置轨道":"使用内置分道线");}else node.value=current[group][key]??(key==="opacity"?1:"");}}
  function updateSceneField(event,group,key){const value=event.target.type==="number"?Number(event.target.value):event.target.value;if(scene()[group][key]!==value){beginEdit();scene()[group][key]=value;}}
  for(const [id,[group,key]] of Object.entries(sceneMap))$(id)?.addEventListener("input",event=>{updateSceneField(event,group,key);renderStage();});
  for(const [id,[group,key]] of Object.entries(sceneMap))$(id)?.addEventListener("change",event=>{updateSceneField(event,group,key);renderScene();renderStage();finishEdit();});
  $("sceneImageFile").addEventListener("change",async event=>{
    try{const additions=new Map();for(const file of event.target.files){await livePanel.validateFile(file);const path=window.SkinStudioLive2D.path("ui/scene/"+file.name);if(!path||!/\.png$/i.test(path))throw Error("请选择安全的 PNG 文件名");additions.set(path,file);}beginEdit();for(const [path,file] of additions)state.files.set(path,file);renderScene();renderRules();finishEdit();notify("场景图片已导入");}catch(error){notify(error.message,true);}finally{event.target.value="";}
  });
  const numberParts={combo:["comboFontSize","comboColor"],score:["scoreFontSize","scoreColor"],judgements:["judgementFontSize","judgementColor"]};
  const digitParts={combo:"Combo",score:"分数"};
  function numberStyle(part){const value=state.document[part]&&typeof state.document[part]==="object"?state.document[part]:{};if(!state.document[part])state.document[part]=value;return value;}
  function renderDigitEditors(){const host=$("digitEditors");if(!host)return;host.innerHTML="";for(const [part,title] of Object.entries(digitParts)){const style=numberStyle(part),digits=Array.isArray(style.digits)?style.digits:[],section=document.createElement("div");section.className="digit-group";const heading=document.createElement("div");heading.className="subheading";heading.textContent=title+"数字皮肤（0–9）";section.appendChild(heading);const grid=document.createElement("div");grid.className="digit-grid";for(let digit=0;digit<=9;digit++){const item=digits.find(value=>String(value.digit)===String(digit)),label=document.createElement("label");label.className="digit-item";label.textContent=digit;const input=document.createElement("input");input.type="file";input.accept="image/png";input.dataset.part=part;input.dataset.digit=String(digit);const path=document.createElement("small");path.textContent=item?.image||"未设置";label.append(input,path);grid.appendChild(label);}section.appendChild(grid);host.appendChild(section);}}
  function renderNumberStyles(){for(const [part,[size,color]] of Object.entries(numberParts)){const value=numberStyle(part);$(size).value=value.fontSize||value.defaultSize||({combo:58,score:42,judgements:16}[part]);$(color).value=value.color||"#FFFFFF";}renderDigitEditors();}
  function touchOverlay(){
    if(!state.document.touchOverlay||typeof state.document.touchOverlay!=="object")state.document.touchOverlay={};
    const value=state.document.touchOverlay;
    if(!value.tablet)value.tablet={}; if(!value.leftPaw)value.leftPaw={}; if(!value.rightPaw)value.rightPaw={};
    return value;
  }
  const touchFields={
    touchTabletX:["tablet","x",.24],touchTabletY:["tablet","y",.08],touchTabletWidth:["tablet","width",.52],touchTabletHeight:["tablet","height",.30],
    touchLeftPawWidth:["leftPaw","width",.14],touchLeftPawHeight:["leftPaw","height",.18],touchRightPawWidth:["rightPaw","width",.14],touchRightPawHeight:["rightPaw","height",.18],
    touchLeftPawOffsetX:["leftPaw","offsetX",0],touchLeftPawOffsetY:["leftPaw","offsetY",0],touchRightPawOffsetX:["rightPaw","offsetX",0],touchRightPawOffsetY:["rightPaw","offsetY",0],
    touchLeftPawSmoothing:["leftPaw","smoothing",.18],touchRightPawSmoothing:["rightPaw","smoothing",.18]
  };
  function renderTouchOverlay(){
    // Rendering must not create empty paw objects in an imported document.
    // Only an edit should materialize the optional touchOverlay groups.
    const value=state.document.touchOverlay&&typeof state.document.touchOverlay==="object"?state.document.touchOverlay:{};
    const tablet=value.tablet||{},leftPaw=value.leftPaw||{},rightPaw=value.rightPaw||{};
    const groups={tablet,leftPaw,rightPaw};
    $("touchOverlayEnabled").checked=value.enabled===true;
    $("touchTabletImage").value=tablet.image||""; $("touchLeftPawImage").value=leftPaw.image||""; $("touchRightPawImage").value=rightPaw.image||"";
    for(const [id,[group,key,fallback]] of Object.entries(touchFields))$(id).value=groups[group][key]??fallback;
  }
  $("touchOverlayEnabled")?.addEventListener("change",event=>{beginEdit();touchOverlay().enabled=event.target.checked;finishEdit();});
  for(const id of Object.keys(touchFields))$(id)?.addEventListener("input",event=>{beginEdit();const [group,key]=touchFields[id];touchOverlay()[group][key]=Number(event.target.value);});
  for(const [id,group,key] of [["touchTabletImage","tablet","image"],["touchLeftPawImage","leftPaw","image"],["touchRightPawImage","rightPaw","image"]])$(id)?.addEventListener("input",event=>{beginEdit();touchOverlay()[group][key]=event.target.value;});
  async function importTouchImage(input,group){const file=input.files[0];if(!file)return;try{await livePanel.validateFile(file);beginEdit();const path=window.SkinStudioLive2D.path(`ui/touch/${file.name}`);state.files.set(path,file);touchOverlay()[group].image=path;renderTouchOverlay();finishEdit();notify("触控板图片已导入");}catch(error){notify("触控板图片读取失败："+error.message,true);}finally{input.value="";}}
  $("touchTabletFile")?.addEventListener("change",event=>importTouchImage(event.target,"tablet"));
  $("touchLeftPawFile")?.addEventListener("change",event=>importTouchImage(event.target,"leftPaw"));
  $("touchRightPawFile")?.addEventListener("change",event=>importTouchImage(event.target,"rightPaw"));
  for(const [part,[size,color]] of Object.entries(numberParts)){
    $(size).addEventListener("input",()=>{beginEdit();numberStyle(part).fontSize=Number($(size).value)||0});
    $(color).addEventListener("input",()=>{beginEdit();numberStyle(part).color=$(color).value});
  }
  $("digitEditors").addEventListener("change",async event=>{const input=event.target;if(input.tagName!=="INPUT"||input.type!=="file"||!input.files[0])return;try{const file=input.files[0];await livePanel.validateFile(file);beginEdit();const part=input.dataset.part,digit=input.dataset.digit,style=numberStyle(part),old=Array.isArray(style.digits)?style.digits.find(value=>String(value.digit)===digit):null;const path=`ui/numbers/${part}/${digit}.png`;if(old?.image&&old.image!==path&&!Object.values(state.document).some(value=>JSON.stringify(value).includes(old.image)))state.files.delete(old.image);state.files.set(path,file);style.digits=(Array.isArray(style.digits)?style.digits.filter(value=>String(value.digit)!==digit):[]).concat({digit,image:path});renderDigitEditors();finishEdit();notify(`${part} 数字 ${digit} 已设置`);}catch(error){notify("数字图片读取失败："+error.message,true);}finally{input.value="";}});
  $("previewState").addEventListener("change",e=>{state.preview=e.target.value;renderPreviewFields();renderStage()});$("showGrid").addEventListener("change",renderStage);
  $("addElement").onclick=()=>{const id=ids.find(x=>!find(x));if(id){beginEdit();ensure(id);state.selected=id;render();finishEdit()}};
  $("removeElement").onclick=()=>{if(state.selected){beginEdit();state.elements=state.elements.filter(e=>e.id!==state.selected);state.selected=state.elements[0]?.id||null;render();finishEdit()}};
  $("imageFile").addEventListener("change",async e=>{const file=e.target.files[0],selected=state.selected;if(!file||!selected||state.busy)return;setBusy(true);
    try{await livePanel.validateFile(file);const path=window.SkinStudioLive2D.path(`ui/studio/${file.name}`);if(!path||!/\.png$/i.test(path))throw Error("请选择 PNG 图片");beginEdit();state.files.set(path,file);find(selected).image=path;render();finishEdit();}
    catch(error){notify("图片读取失败："+error.message,true);}finally{setBusy(false);e.target.value="";}});
  // Preserve existing touchOverlay, digit styles and other game-owned fields
  // when opening a skin configuration; editing layout must not erase Live2D.
  function output(){window.SkinStudioRules.validate(state.customDisplays,true);const data={...state.document,belowNoteMetric:{mode:state.metric},elements:state.elements,customDisplays:state.customDisplays,scene:state.scene,live2d:livePanel.get()};checkDocument(data);return JSON.stringify(data,null,2)}
  async function download(name,data,type="application/octet-stream"){
    const blob=new Blob([data],{type});
    if(window.studioDesktop)return window.studioDesktop.saveFile(name,new Uint8Array(await blob.arrayBuffer()));
    const a=document.createElement("a");a.href=URL.createObjectURL(blob);a.download=name;a.click();setTimeout(()=>URL.revokeObjectURL(a.href),1000);
  }
  $("exportJson").onclick=async()=>{if(state.busy)return;setBusy(true);try{const result=await download("gameplay-ui.json",output(),"application/json");notify(result===false?"已取消保存":"已生成 gameplay-ui.json");}
    catch(error){notify("无法保存配置："+error.message,true);}finally{setBusy(false);}};
  function checkDocument(data){
    if(!data||typeof data!=="object"||Array.isArray(data))throw Error("配置必须是 JSON 对象");
    if(data.elements!=null&&!Array.isArray(data.elements))throw Error("elements 必须是数组");
    validateElements(data.elements||[]);
    if(data.belowNoteMetric!=null&&(!data.belowNoteMetric||typeof data.belowNoteMetric!=="object"||Array.isArray(data.belowNoteMetric)||!window.SkinStudioRules.modes.slice(1).includes(data.belowNoteMetric.mode)))throw Error("音符下方显示模式无效");
    if(data.live2d!=null&&(typeof data.live2d!=="object"||Array.isArray(data.live2d)))throw Error("Live2D 配置必须是对象");
    if(data.customDisplays!=null&&(!Array.isArray(data.customDisplays)||data.customDisplays.length>48))throw Error("customDisplays 最多 48 项");
    if(data.customDisplays){if(data.customDisplays.some(rule=>!rule||typeof rule!=="object"||Array.isArray(rule)))throw Error("规则必须是对象");window.SkinStudioRules.validate(data.customDisplays.map(rule=>Object.assign(newRule(),rule)));}
    checkScene(data.scene);window.SkinStudioLive2D.imageReferences(data);return data;
  }
  function validateElements(elements){
    // Reject an invalid candidate before replacing the open project. Browser
    // number inputs and imported JSON are not a trusted geometry boundary.
    if(elements.length>ids.length)throw Error("界面元素过多");const seen=new Set();
    const bounds={x:[0,1],y:[0,1],width:[.001,1],height:[.001,1],opacity:[0,1],textSize:[0,256]};
    for(const element of elements){
      if(!element||typeof element!=="object"||Array.isArray(element)||!ids.includes(element.id)||seen.has(element.id))throw Error("元素 ID 无效或重复");seen.add(element.id);
      for(const [key,[min,max]] of Object.entries(bounds))if(Object.hasOwn(element,key)&&(!Number.isFinite(element[key])||element[key]<min||element[key]>max))throw Error("元素数值越界："+key);
      const value={...cloneDefault(element.id),...element};
      if(value.x+value.width>1.001||value.y+value.height>1.001)throw Error("元素超出画布："+element.id);
      if(Object.hasOwn(element,"visible")&&typeof element.visible!=="boolean")throw Error("元素可见性需要布尔值");
      for(const key of ["color","textColor"])if(element[key]!=null&&element[key]!==""&&(typeof element[key]!=="string"||!/^#[0-9a-f]{6}([0-9a-f]{2})?$/i.test(element[key])))throw Error("元素颜色无效");
      if(element.label!=null&&(typeof element.label!=="string"||element.label.length>80))throw Error("元素标签过长或无效");
    }
  }
  function checkScene(value){
    if(value==null)return;if(typeof value!=="object"||Array.isArray(value))throw Error("场景必须是对象");
    for(const key of ["background","track"]){const part=value[key];if(part!=null&&(typeof part!=="object"||Array.isArray(part)))throw Error("场景分组无效");}
    if(value.background?.opacity!=null&&(!Number.isFinite(value.background.opacity)||value.background.opacity<0||value.background.opacity>1))throw Error("背景透明度需要为 0–1");
    for(const group of Object.values(value))if(group&&typeof group==="object")for(const [key,color] of Object.entries(group))if(/color$/i.test(key)&&color&&!/^#[0-9a-f]{6}([0-9a-f]{2})?$/i.test(color))throw Error("场景颜色需要使用 #RRGGBB 或 #RRGGBBAA");
  }
  function openDocument(data,saved=true){state.document=data;state.elements=(data.elements||[]).filter(x=>x&&ids.includes(x.id)).map(x=>Object.assign(cloneDefault(x.id),x));state.metric=data.belowNoteMetric?.mode||"combo";state.customDisplays=Array.isArray(data.customDisplays)?data.customDisplays.map(rule=>Object.assign(newRule(),rule)):[];state.scene=data.scene||{};livePanel.set(data.live2d);state.selected=state.elements[0]?.id||null;state.selectedRule=state.customDisplays[0]?.id||null;$("placeRule").checked=false;renderNumberStyles();render();history.undo=[];history.redo=[];savedFingerprint=saved?fingerprint():"";finishEdit();}
  $("importJson").addEventListener("change",async e=>{const file=e.target.files[0];if(!file||state.busy)return;setBusy(true);try{await livePanel.validateFile(file);const data=checkDocument(JSON.parse(await file.text()));if(!await confirmReplace()){notify("已取消打开");return;}openDocument(data,false);notify("配置已读取");}catch(err){notify("JSON 读取失败："+err.message,true);}finally{setBusy(false);e.target.value="";}});
  // A complete skin folder retains skin-relative paths and calibrated values.
  // Validate the entire candidate before replacing the current editor project.
  $("skinFolder").addEventListener("change",async event=>{if(state.busy)return;setBusy(true);try{
    const config=window.SkinStudioLive2D,files=new Map(),keys=new Set(),list=Array.from(event.target.files);
    if(list.length>2000)throw Error("文件数量超过 2000");
    for(const file of list){const p=config.path(file.webkitRelativePath.split("/").slice(1).join("/"));
      if(!p)throw Error("无效资源路径");if(!["skin.json","ui/gameplay-ui.json"].includes(p)&&!config.allowedAsset(p))continue;
      const key=p.toLowerCase();if(keys.has(key))throw Error("大小写冲突："+p);keys.add(key);
      await livePanel.validateFile(file);files.set(p,file);
    }
    if([...files.values()].reduce((sum,file)=>sum+file.size,0)>256*1024*1024)throw Error("资源总量超过 256 MB");
    if(!files.has("skin.json")||!files.has("ui/gameplay-ui.json"))throw Error("请选择包含 skin.json 和 ui/gameplay-ui.json 的皮肤根目录");
    const manifest=JSON.parse(await files.get("skin.json").text()),data=checkDocument(JSON.parse(await files.get("ui/gameplay-ui.json").text()));
    validateManifest(manifest);
    if(data.live2d?.enabled){const model=config.path(data.live2d.model);if(!files.has(model))throw Error("缺少模型文件："+model);
      const report=config.inspect(model,JSON.parse(await files.get(model).text()),files);if(report.missing.length)throw Error("缺少资源："+report.missing.join("、"));}
    const missing=config.imageReferences(data).filter(p=>!files.has(p));if(missing.length)throw Error("缺少图片："+missing.join("、"));
    if(!await confirmReplace()){notify("已取消打开");return;}
    files.delete("skin.json");files.delete("ui/gameplay-ui.json");state.files.clear();files.forEach((file,p)=>state.files.set(p,file));
    state.manifest=manifest;$("skinName").value=manifest.name;$("skinId").value=manifest.id;openDocument(data);notify("皮肤已打开："+manifest.name);
  }catch(error){notify("无法打开皮肤："+error.message,true);}finally{setBusy(false);event.target.value="";}});
  async function readZip(file){
    if(!window.DecompressionStream)throw Error("当前浏览器不支持 ZIP 导入，请使用桌面版");
    if(file.size>256*1024*1024)throw Error("ZIP 文件超过 256 MB");
    const bytes=new Uint8Array(await file.arrayBuffer()),view=new DataView(bytes.buffer),entries=[];
    let end=-1;for(let i=bytes.length-22;i>=Math.max(0,bytes.length-65557);i--)if(view.getUint32(i,true)===0x06054b50){end=i;break;}
    if(end<0)throw Error("ZIP 缺少结束目录");const count=view.getUint16(end+10,true),centralSize=view.getUint32(end+12,true),centralOffset=view.getUint32(end+16,true);if(count>2000||centralOffset+centralSize>end)throw Error("ZIP 目录无效");let pos=centralOffset,totalExpanded=0;
    const inflate=async(raw,limit)=>{const reader=new Blob([raw]).stream().pipeThrough(new DecompressionStream("deflate-raw")).getReader(),parts=[],length=0;while(true){const item=await reader.read();if(item.done)break;parts.push(item.value);length+=item.value.length;if(length>limit){reader.cancel();throw Error("ZIP 解压后文件超过 64 MB");}}const output=new Uint8Array(length);let offset=0;for(const part of parts){output.set(part,offset);offset+=part.length;}return output;};
    for(let index=0;index<count;index++){if(pos+46>bytes.length||view.getUint32(pos,true)!==0x02014b50)throw Error("ZIP 目录损坏");const flags=view.getUint16(pos+8,true),method=view.getUint16(pos+10,true),compressed=view.getUint32(pos+20,true),size=view.getUint32(pos+24,true),nameLen=view.getUint16(pos+28,true),extraLen=view.getUint16(pos+30,true),commentLen=view.getUint16(pos+32,true),localOffset=view.getUint32(pos+42,true),expectedCrc=view.getUint32(pos+16,true);if((flags&1)||compressed===0xffffffff||size===0xffffffff||localOffset===0xffffffff||compressed>64*1024*1024||size>64*1024*1024)throw Error("ZIP 条目超出安全限制");const name=new TextDecoder().decode(bytes.slice(pos+46,pos+46+nameLen));if(!name||name.endsWith("/")){pos+=46+nameLen+extraLen+commentLen;continue;}if(localOffset+30>bytes.length||view.getUint32(localOffset,true)!==0x04034b50)throw Error("ZIP 本地目录损坏："+name);const localFlags=view.getUint16(localOffset+6,true),localMethod=view.getUint16(localOffset+8,true),localName=view.getUint16(localOffset+26,true),localExtra=view.getUint16(localOffset+28,true),localNameText=new TextDecoder().decode(bytes.slice(localOffset+30,localOffset+30+localName));if(localNameText!==name||localMethod!==method||(localFlags&1))throw Error("ZIP 目录与文件头不一致："+name);const start=localOffset+30+localName+localExtra;if(start+compressed>bytes.length)throw Error("ZIP 文件越界："+name);let raw=bytes.slice(start,start+compressed);if(method===8)raw=await inflate(raw,64*1024*1024);else if(method!==0)throw Error("ZIP 压缩格式不支持："+name);if(raw.length!==size||crc32(raw)!==expectedCrc)throw Error("ZIP 文件校验失败："+name);totalExpanded+=raw.length;if(totalExpanded>256*1024*1024)throw Error("ZIP 解压后总大小超过 256 MB");entries.push([name,new File([raw],name)]);pos+=46+nameLen+extraLen+commentLen;}
    if(!entries.length)throw Error("ZIP 为空或格式不支持");return entries;
  }
  $("skinZip").addEventListener("change",async event=>{const file=event.target.files[0];if(!file||state.busy)return;setBusy(true);try{
    const entries=await readZip(file),root=await fsForZip(entries);if(!await confirmReplace()){notify("已取消打开");return;}state.files.clear();root.files.forEach((value,key)=>state.files.set(key,value));state.manifest=root.manifest;$("skinName").value=root.manifest.name;$("skinId").value=root.manifest.id;openDocument(root.data);notify("皮肤 ZIP 已打开："+root.manifest.name);
  }catch(error){notify("无法导入皮肤 ZIP："+error.message,true);}finally{setBusy(false);event.target.value="";}});
  async function fsForZip(entries){const map=new Map(),keys=new Set(),config=window.SkinStudioLive2D;
    for(const [rawName,file] of entries){const name=config.path(rawName.replace(/\\/g,"/"));if(!name)throw Error("ZIP 包含非法路径");const lower=name.toLowerCase();if(keys.has(lower))throw Error("ZIP 存在大小写冲突："+name);keys.add(lower);if(name!=="skin.json"&&name!=="ui/gameplay-ui.json"&&name!=="README.txt"&&!config.allowedAsset(name))throw Error("ZIP 包含不支持的文件："+name);if(name!=="README.txt"){await livePanel.validateFile(file);map.set(name,file);}}
    if(!map.has("skin.json")||!map.has("ui/gameplay-ui.json"))throw Error("ZIP 缺少 skin.json 或 ui/gameplay-ui.json");const manifest=JSON.parse(await map.get("skin.json").text()),data=checkDocument(JSON.parse(await map.get("ui/gameplay-ui.json").text()));validateManifest(manifest);const files=new Map(map);files.delete("skin.json");files.delete("ui/gameplay-ui.json");if(data.live2d?.enabled){const model=config.path(data.live2d.model);if(!files.has(model))throw Error("缺少模型文件："+model);const report=config.inspect(model,JSON.parse(await files.get(model).text()),files);if(report.missing.length)throw Error("缺少资源："+report.missing.join("、"));}const missing=config.imageReferences(data).filter(p=>!files.has(p));if(missing.length)throw Error("缺少图片："+missing.join("、"));return {manifest,data,files};}
  $("undoButton").onclick=undo;$("redoButton").onclick=redo;
  $("saveProject").onclick=async()=>{if(state.busy)return;finishEdit();setBusy(true);try{validateManifest(state.manifest);await livePanel.validate();const missing=window.SkinStudioLive2D.imageReferences(JSON.parse(output())).filter(p=>!state.files.has(p));if(missing.length)throw Error("缺少图片："+missing.join("、"));const revision=fingerprint(),result=await zip();if(result!==false)markSaved(revision);notify(result===false?"已取消保存":"项目已保存");}catch(error){notify("保存失败："+error.message,true);}finally{setBusy(false);}};
  function validateManifest(manifest){if(!manifest||manifest.formatVersion!==1||manifest.gameplaySkin!==true||
    typeof manifest.id!=="string"||!/^[a-zA-Z0-9_-][a-zA-Z0-9_.-]{0,63}$/.test(manifest.id)||
    typeof manifest.name!=="string"||!manifest.name.trim()||manifest.name.length>80)throw Error("皮肤名称或 ID 无效（ID 使用字母、数字、_、-、.）");}
  function setBusy(busy){state.busy=busy;if(busy)notify("");document.querySelector("main").inert=busy;document.querySelector(".top-actions").inert=busy;$("exportZip").textContent=busy?"处理中…":"导出皮肤 ZIP";}
  ["skinName","skinId"].forEach(id=>$(id).addEventListener("input",()=>{beginEdit();state.manifest[id==="skinName"?"name":"id"]=$(id).value;}));
  function crc32(bytes){let c=0xffffffff;for(const b of bytes){c^=b;for(let i=0;i<8;i++)c=(c>>>1)^((c&1)?0xedb88320:0)}return (c^0xffffffff)>>>0}
  function u16(v){return new Uint8Array([v&255,(v>>>8)&255])}function u32(v){return new Uint8Array([v&255,(v>>>8)&255,(v>>>16)&255,(v>>>24)&255])}function join(parts){let n=parts.reduce((a,p)=>a+p.length,0),o=new Uint8Array(n),i=0;for(const p of parts){o.set(p,i);i+=p.length}return o}
  async function zip() {
    const motions=state.customDisplays.filter(rule=>rule.enabled!==false&&rule.action==="live2dMotion");
    if(motions.length){const model=livePanel.get();if(!model.enabled||!state.files.has(model.model))throw Error("Live2D 动作规则需要启用并导入模型");const report=window.SkinStudioLive2D.inspect(model.model,JSON.parse(await state.files.get(model.model).text()),state.files);for(const rule of motions)if(!report.groups.includes(rule.motion))throw Error("动作组不存在："+rule.motion);}
    const enc=new TextEncoder(),items=[
      {name:"skin.json",data:enc.encode(JSON.stringify(state.manifest,null,2))},
      {name:"ui/gameplay-ui.json",data:enc.encode(output())},
      {name:"README.txt",data:enc.encode("Generated by CHUNITHM Mobile Skin Studio. Declarative JSON, PNG and Live2D model data only. No package code is executed.\r\n")}
    ];
    for(const [name,file] of state.files)items.push({name,data:new Uint8Array(await file.arrayBuffer())});
    // Unity limits expanded bytes, including the generated configuration and
    // metadata, not just selected resources. Do not export a boundary-sized
    // archive that passes the studio but is rejected by the game.
    if(items.reduce((sum,item)=>sum+item.data.length,0)>256*1024*1024)throw Error("皮肤总大小（含配置）超过 256 MB");
    const local=[],central=[];let offset=0;
    for(const item of items) {
      const name=enc.encode(item.name),data=item.data,crc=crc32(data);
      // Stored ZIP entries need both DOS time and date in each header. Keep
      // their lengths aligned so .NET/Unity and desktop unzip tools agree.
      const head=join([u32(0x04034b50),u16(20),u16(0x800),u16(0),u16(0),u16(0),u32(crc),u32(data.length),u32(data.length),u16(name.length),u16(0),name]);
      local.push(head,data);
      central.push(join([u32(0x02014b50),u16(20),u16(20),u16(0x800),u16(0),u16(0),u16(0),u32(crc),u32(data.length),u32(data.length),u16(name.length),u16(0),u16(0),u16(0),u16(0),u32(0),u32(offset),name]));
      offset+=head.length+data.length;
    }
    const cd=join(central),body=join(local),end=join([u32(0x06054b50),u16(0),u16(0),u16(items.length),u16(items.length),u32(cd.length),u32(body.length),u16(0)]);
    return download("skin-studio-export.zip",join([body,cd,end]),"application/zip");
  }
  livePanel=window.createLive2DPanel(state.files,render,beginEdit,finishEdit);
  $("exportZip").onclick=async()=>{if(state.busy)return;finishEdit();setBusy(true);try{validateManifest(state.manifest);await livePanel.validate();
    const missing=window.SkinStudioLive2D.imageReferences(JSON.parse(output())).filter(p=>!state.files.has(p));if(missing.length)throw Error("缺少图片："+missing.join("、"));
    const paths=new Set();for(const p of state.files.keys()){const key=p.toLowerCase();if(paths.has(key))throw Error("大小写冲突："+p);paths.add(key);}
    const revision=fingerprint(),result=await zip();if(result!==false)markSaved(revision);notify(result===false?"已取消保存":"已生成 skin-studio-export.zip");}catch(error){notify("无法导出皮肤："+error.message,true);}finally{setBusy(false);}};
  $("skinName").value=state.manifest.name;$("skinId").value=state.manifest.id;renderNumberStyles();updateHistoryUi();
  ids.slice(0,5).forEach(ensure);state.selected="songInfo";render();markSaved();
  // Register from a local script so CSP never needs to permit inline code.
  if(location.protocol!=="file:"&&window.isSecureContext&&"serviceWorker" in navigator)
    navigator.serviceWorker.register("service-worker.js").catch(()=>{});
})();
