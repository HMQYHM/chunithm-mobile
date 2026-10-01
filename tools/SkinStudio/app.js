/*
 * The studio only writes the game's declarative skin format. It never loads or
 * executes package code; the ZIP writer stores JSON and user-selected PNG data.
 */
(() => {
  const ids = ["songInfo","songCover","scoreHud","comboHud","judgement","judgementImage","pauseButton","pausePanel","progressBar","fps","countdown","touchpad","airZone","airBoundaryBottom","airBoundaryTop","airGuide","trackMask","touchFeedback"];
  const labels = {songInfo:"歌曲信息",songCover:"歌曲封面",scoreHud:"分数 HUD",comboHud:"Combo HUD",judgement:"判定区域",judgementImage:"判定图片",pauseButton:"暂停按钮",pausePanel:"暂停面板",progressBar:"进度条",fps:"帧数",countdown:"倒计时",touchpad:"触控板",airZone:"AIR 区域",airBoundaryBottom:"AIR 下边界",airBoundaryTop:"AIR 上边界",airGuide:"AIR 引导线",trackMask:"轨道遮罩",touchFeedback:"触控反馈"};
  const defaults = {songInfo:[.03,.72,.28,.14],songCover:[.04,.73,.10,.12],scoreHud:[.77,.80,.21,.18],comboHud:[.80,.84,.16,.10],judgement:[.34,.42,.32,.20],judgementImage:[.40,.48,.20,.10],pauseButton:[.018,.88,.06,.08],pausePanel:[.34,.24,.32,.52],progressBar:[.05,.96,.90,.008],fps:[.02,.02,.14,.04],countdown:[.38,.36,.24,.30],touchpad:[.20,.04,.60,.34],airZone:[.05,.55,.90,.34],airBoundaryBottom:[.05,.52,.90,.008],airBoundaryTop:[.05,.88,.90,.008],airGuide:[.05,.70,.90,.008],trackMask:[.05,.72,.90,.20],touchFeedback:[0,0,1,1]};
  const metricText = {combo:"显示当前 Combo；分数区域显示正常分数。",remaining_combo:"显示距离满 Combo 还剩多少。",score:"显示从 0 增加的当前分数。", "sss+":"显示距离 SSS+ 还剩多少分。",sss:"显示距离 SSS 还剩多少分。","ss+":"显示距离 SS+ 还剩多少分。",ss:"显示距离 SS 还剩多少分。","s+":"显示距离 S+ 还剩多少分。",s:"显示距离 S 还剩多少分。",lost_score:"只累计显示已经损失的分数。",best_score:"显示 1,010,000 减去当前最好成绩后的目标差值。"};
  const state = {elements:[],selected:null,metric:"combo",customDisplays:[],files:new Map(),preview:"playing",document:{}};
  let livePanel;
  const $ = id => document.getElementById(id);
  // Keep Live2D settings discoverable without scrolling past the UI inspector.
  function setSettingsTab(tab) {
    const live=tab==="live2d";
    $("layoutSettings").hidden=live;$("liveSettings").hidden=!live;
    $("layoutTab").setAttribute("aria-selected",String(!live));$("liveTab").setAttribute("aria-selected",String(live));
  }
  $("layoutTab").onclick=()=>setSettingsTab("layout");$("liveTab").onclick=()=>setSettingsTab("live2d");
  $("layerList").addEventListener("click",()=>setSettingsTab("layout"));
  $("stage").addEventListener("click",event=>{if(event.target.closest(".skin-element"))setSettingsTab("layout");});
  $("addElement").addEventListener("click",()=>setSettingsTab("layout"));
  const cloneDefault = id => { const r = defaults[id] || [.1,.1,.1,.1]; return {id,visible:true,x:r[0],y:r[1],width:r[2],height:r[3],opacity:1,color:"",image:"",label:""}; };
  const find = id => state.elements.find(e => e.id === id);
  const ensure = id => { let e=find(id); if(!e){e=cloneDefault(id);state.elements.push(e)} return e; };
  function renderLayers(){ const list=$("layerList"); list.innerHTML=""; state.elements.forEach(e=>{ const b=document.createElement("button"); b.className="layer-row"+(state.selected===e.id?" active":""); b.innerHTML=`<span class="eye">${e.visible?"●":"○"}</span><span>${labels[e.id]||e.id}</span>`; b.onclick=()=>{state.selected=e.id;render()}; list.appendChild(b) }); $("fileSummary").textContent=`${state.files.size} 个资源 · ${state.elements.length} 个元素`; }
  function renderStage(){ const stage=$("stage"); stage.querySelectorAll(".skin-element").forEach(n=>n.remove()); state.elements.forEach(e=>{ const n=document.createElement("div"); n.className="skin-element"+(state.selected===e.id?" selected":""); n.dataset.id=e.id; n.textContent=labels[e.id]||e.id; n.style.left=`${e.x*100}%`;n.style.bottom=`${e.y*100}%`;n.style.width=`${e.width*100}%`;n.style.height=`${e.height*100}%`;n.style.opacity=e.visible?e.opacity:0.18;if(e.color)n.style.color=e.color;if(e.image&&state.files.has(e.image)){const img=document.createElement("img");img.src=URL.createObjectURL(state.files.get(e.image));n.textContent="";n.appendChild(img)}else if(e.label)n.textContent=e.label;n.onclick=()=>{state.selected=e.id;render()};stage.appendChild(n)}); $("airDemo").style.display=state.preview==="air"?"block":"none"; $("metricDemo").textContent=state.preview==="metricZero"?"SSS+ 剩余 0":state.preview==="miss"?"MISS":state.preview==="combo100"?"COMBO 100":"COMBO 0"; $("statusBadge").textContent={playing:"游玩中",pause:"暂停",miss:"MISS",combo100:"Combo 100",air:"AIR",metricZero:"目标值归零"}[state.preview]; document.querySelector(".stage-grid").style.display=$("showGrid").checked?"block":"none"; }
  function fillInspector(){ const e=find(state.selected); $("emptyInspector").hidden=!!e;$("inspector").hidden=!e;if(!e)return; $("elementId").innerHTML=ids.map(id=>`<option value="${id}">${labels[id]}</option>`).join("");$("elementId").value=e.id;$("visible").checked=!!e.visible;["x","y","width","height","opacity","color","image","label"].forEach(k=>{$(k).value=e[k]??""});$("opacityValue").value=Number(e.opacity).toFixed(2);$("metricMode").value=state.metric;$("metricDescription").textContent=metricText[state.metric]||""; }
  function render(){renderLayers();renderStage();fillInspector();livePanel?.render()}
  function bindField(id,key,parse){$(id).addEventListener("input",()=>{const e=find(state.selected);if(!e)return;e[key]=parse?parse($(id).value):$(id).value;if(key==="opacity")$("opacityValue").value=Number(e[key]).toFixed(2);renderStage()})}
  ["x","y","width","height","opacity"].forEach(k=>bindField(k,k,Number));["color","image","label"].forEach(k=>bindField(k,k));
  $("visible").addEventListener("change",()=>{const e=find(state.selected);if(e){e.visible=$("visible").checked;render()}});
  $("elementId").addEventListener("change",()=>{const current=find(state.selected);const replacement=$("elementId").value;if(!current||find(replacement))return;current.id=replacement;state.selected=replacement;render()});
  $("metricMode").addEventListener("change",()=>{state.metric=$("metricMode").value;$("metricDescription").textContent=metricText[state.metric]||""});
  $("previewState").addEventListener("change",e=>{state.preview=e.target.value;renderStage()});$("showGrid").addEventListener("change",renderStage);
  $("addElement").onclick=()=>{const id=ids.find(x=>!find(x));if(id){ensure(id);state.selected=id;render()}};
  $("removeElement").onclick=()=>{state.elements=state.elements.filter(e=>e.id!==state.selected);state.selected=state.elements[0]?.id||null;render()};
  $("imageFile").addEventListener("change",e=>{const file=e.target.files[0];if(!file||!state.selected)return;const path=`ui/studio/${file.name}`;state.files.set(path,file);const item=find(state.selected);item.image=path;render()});
  // Preserve existing touchOverlay, digit styles and other game-owned fields
  // when opening a skin configuration; editing layout must not erase Live2D.
  function output(){return JSON.stringify({...state.document,belowNoteMetric:{mode:state.metric},elements:state.elements,customDisplays:state.customDisplays,live2d:livePanel.get()},null,2)}
  function download(name,data,type="application/octet-stream"){const a=document.createElement("a");a.href=URL.createObjectURL(new Blob([data],{type}));a.download=name;a.click();setTimeout(()=>URL.revokeObjectURL(a.href),1000)}
  $("exportJson").onclick=()=>download("gameplay-ui.json",output(),"application/json");
  $("importJson").addEventListener("change",async e=>{const file=e.target.files[0];if(!file)return;try{const data=JSON.parse(await file.text());state.document=data;state.elements=(data.elements||[]).filter(x=>ids.includes(x.id)).map(x=>Object.assign(cloneDefault(x.id),x));state.metric=data.belowNoteMetric?.mode||"combo";state.customDisplays=Array.isArray(data.customDisplays)?data.customDisplays:[];livePanel.set(data.live2d);state.selected=state.elements[0]?.id||null;render()}catch(err){alert("JSON 读取失败："+err.message)}});
  function crc32(bytes){let c=0xffffffff;for(const b of bytes){c^=b;for(let i=0;i<8;i++)c=(c>>>1)^((c&1)?0xedb88320:0)}return (c^0xffffffff)>>>0}
  function u16(v){return new Uint8Array([v&255,(v>>>8)&255])}function u32(v){return new Uint8Array([v&255,(v>>>8)&255,(v>>>16)&255,(v>>>24)&255])}function join(parts){let n=parts.reduce((a,p)=>a+p.length,0),o=new Uint8Array(n),i=0;for(const p of parts){o.set(p,i);i+=p.length}return o}
  async function zip() {
    const enc=new TextEncoder(),items=[
      {name:"skin.json",data:enc.encode(JSON.stringify({id:"studio_skin",name:"Skin Studio export",gameplaySkin:true,formatVersion:1},null,2))},
      {name:"ui/gameplay-ui.json",data:enc.encode(output())},
      {name:"README.txt",data:enc.encode("Generated by CHUNITHM Mobile Skin Studio. Declarative JSON, PNG and Live2D model data only. No package code is executed.\r\n")}
    ];
    for(const [name,file] of state.files)items.push({name,data:new Uint8Array(await file.arrayBuffer())});
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
    download("skin-studio-export.zip",join([body,cd,end]),"application/zip");
  }
  livePanel=window.createLive2DPanel(state.files,render);
  $("exportZip").onclick=async()=>{try{await livePanel.validate();await zip();}catch(error){alert("无法导出皮肤："+error.message)}};
  ids.slice(0,5).forEach(ensure);state.selected="songInfo";render();
})();
