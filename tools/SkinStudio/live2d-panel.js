/* UI bindings keep the Live2D model data separate from normal UI elements.
 * Only validated local resources are added to the shared ZIP asset collection. */
window.createLive2DPanel = function(files, changed, beginEdit, finishEdit) {
  "use strict";
  beginEdit=beginEdit||(()=>{});finishEdit=finishEdit||(()=>{});
  const config = window.SkinStudioLive2D;
  const $ = id => document.getElementById(id);
  let live = config.defaults();
  let objectUrl = null;
  let statusRevision = 0, importing = false;
  const parameterLabels = ["左手 X","左手 Y","左手显示","左手按下","右手 X","右手 Y","右手显示","右手按下"];
  const motionLabels = ["空闲动作组","左手按下动作组","右手按下动作组"];
  function field(parent, title, key, value, update, numeric=false, min=0, max=1) {
    const label = document.createElement("label"); label.className="field";
    const caption = document.createElement("span"); caption.textContent=title;
    const input = document.createElement("input"); input.type=numeric?"number":"text";
    if(numeric){input.min=min;input.max=max;input.step=.01;}else {
      input.maxLength=64;input.setAttribute("list",key.endsWith("Motion")?"liveMotionOptions":"liveParameterOptions");
    }
    input.value=value; input.dataset.binding=key;
    // Do not rebuild the focused inspector on blur: that can swallow the
    // click on the next field. Capture input immediately, including exports.
    let previous=value;
    const edit=()=>{const next=numeric?Number(input.value):input.value;if(Object.is(previous,next))return;
      beginEdit();update(next);previous=next;changed();};
    input.addEventListener("input",edit);input.addEventListener("change",()=>{edit();finishEdit();});input.addEventListener("blur",finishEdit);
    label.append(caption,input);parent.append(label);
  }
  async function checkModel() {
    if(!live.model)return null;
    const file=files.get(live.model);
    if(!file)throw Error("缺少模型文件："+live.model);
    return config.inspect(live.model,JSON.parse(await file.text()),files);
  }
  async function status() {
    // Discard an outdated async result if the user selects another model.
    const model=live.model,revision=++statusRevision;
    try {const result=await checkModel();if(revision!==statusRevision)return;
      $("modelStatus").textContent=result?(result.missing.length?"缺少资源："+result.missing.join("、"):"资源齐全 · 动作组："+(result.groups.join("、")||"无")):"未导入模型";
      $("modelStatus").classList.toggle("invalid",!!result?.missing.length);
      $("liveMotionOptions").replaceChildren(...(result?.groups||[]).map(g=>new Option(g,g)));
      $("liveParameterOptions").replaceChildren();
      const doc=model?JSON.parse(await files.get(model).text()):null;
      const displayInfo=doc?.FileReferences?.DisplayInfo;
      if(displayInfo){const cdi=files.get(config.resolve(model,displayInfo));
        if(cdi){const info=JSON.parse(await cdi.text());if(revision!==statusRevision)return;
          $("liveParameterOptions").replaceChildren(...(info.Parameters||[]).slice(0,2048).filter(p=>typeof p.Id==="string").map(p=>new Option(p.Name||p.Id,p.Id)));
        }
      }
    } catch(error){if(revision===statusRevision){$("modelStatus").textContent=error.message;$("modelStatus").classList.add("invalid");}}
  }
  function refresh() {
    $("liveEnabled").checked=live.enabled;$("liveModel").value=live.model;
    const select=$("modelSelect");select.replaceChildren(new Option("选择已导入的模型",""));
    for(const path of files.keys())if(/\.model3\.json$/i.test(path))select.add(new Option(path,path));select.value=live.model;
    document.querySelectorAll("[data-live]").forEach(input=>{input.value=live[input.dataset.live]});
    $("liveParameters").replaceChildren();
    config.parameters.forEach((key,i)=>field($("liveParameters"),parameterLabels[i],key,live[key],value=>{live[key]=value}));
    config.axes.forEach((key,i)=>{
      for(const bound of ["min","max"])field($("liveParameters"),["左手 X","左手 Y","右手 X","右手 Y"][i]+(bound==="min"?" 起点":" 终点"),key+"."+bound,live[key][bound],value=>{live[key][bound]=value},true);
    });
    $("liveMotions").replaceChildren();config.motions.forEach((key,i)=>field($("liveMotions"),motionLabels[i],key,live[key],value=>{live[key]=value}));
    $("hiddenParts").value=live.hiddenParts.join("\n");
    $("liveBackground").value=live.background?.image||"";
    $("backgroundFields").replaceChildren();
    for(const key of ["x","y","width","height"]){const b=live.background||{x:-1,y:-1,width:2,height:2};
      field($("backgroundFields"),{x:"模型 X",y:"模型 Y",width:"模型宽度",height:"模型高度"}[key],"background."+key,b[key],value=>{ensureBackground()[key]=value},true,key==="x"||key==="y"?-5:.01,key==="x"||key==="y"?5:10);
    }status();
  }
  function ensureBackground(){if(!live.background)live.background={image:"",x:-1,y:-1,width:2,height:2};return live.background;}
  function renderBounds() {
    document.getElementById("liveBounds")?.remove();
    if(objectUrl){URL.revokeObjectURL(objectUrl);objectUrl=null;}
    if(!live.enabled)return;
    const box=document.createElement("div");box.id="liveBounds";box.className="live-bounds";
    Object.assign(box.style,{left:live.x*100+"%",bottom:live.y*100+"%",width:live.width*100+"%",height:live.height*100+"%"});
    const tag=document.createElement("span");tag.textContent="Live2D";box.append(tag);
    const background=files.get(live.background?.image);
    if(background){const img=document.createElement("img");objectUrl=URL.createObjectURL(background);img.src=objectUrl;box.append(img);}
    $("stage").append(box);
  }
  async function validateFile(file) {
    if(!file.size||file.size>64*1024*1024)throw Error("文件为空或超过 64 MB："+file.name);
    if(/\.json$/i.test(file.name)&&file.size>2*1024*1024)throw Error("JSON 配置超过 2 MB："+file.name);
    if(/\.json$/i.test(file.name))JSON.parse(await file.text());
    if(/\.moc3$/i.test(file.name)){
      const head=new Uint8Array(await file.slice(0,4).arrayBuffer());
      if(head.length!==4||[77,79,67,51].some((byte,i)=>head[i]!==byte))throw Error("不是有效 MOC3 模型："+file.name);
    }
    if(/\.png$/i.test(file.name)){
      const head=new Uint8Array(await file.slice(0,24).arrayBuffer());
      if(head.length<24||[137,80,78,71,13,10,26,10].some((byte,i)=>head[i]!==byte))throw Error("不是有效 PNG："+file.name);
      const view=new DataView(head.buffer),w=view.getUint32(16),h=view.getUint32(20);
      if(!w||!h||w>4096||h>4096)throw Error("PNG 尺寸超过 4096："+file.name);
      // Headers alone accept truncated/corrupt PNGs. Use the browser decoder
      // before packaging, and release the decoded bitmap immediately.
      const bitmap=await createImageBitmap(file);bitmap.close();
    }
  }
  $("modelFolder").addEventListener("change",async event=>{
    importing=true;$("modelStatus").textContent="正在读取模型资源…";
    try {
      const list=Array.from(event.target.files);
      if(list.length>2000)throw Error("文件数量超过 2000");
      const additions=new Map(),keys=new Set();let skipped=0;
      for(const file of list){
        // Strip only the user-selected outer directory. Internal paths remain
        // intact so model3 references still resolve on case-sensitive devices.
        const relative=file.webkitRelativePath?file.webkitRelativePath.split("/").slice(1).join("/"):file.name;
        if(!config.allowedAsset(relative)){skipped++;continue;}
        const path=config.path(relative.startsWith("model/")?relative:"model/"+relative),key=path.toLowerCase();
        if(!path||keys.has(key))throw Error("资源路径无效或大小写冲突："+relative);keys.add(key);
        await validateFile(file);additions.set(path,file);
      }
      const models=[...additions.keys()].filter(p=>/\.model3\.json$/i.test(p));
      if(!models.length)throw Error("文件夹中没有 .model3.json");
      const merged=new Map([...files,...additions]);
      const mergedKeys=new Set();for(const path of merged.keys()){const key=path.toLowerCase();if(mergedKeys.has(key))throw Error("大小写冲突："+path);mergedKeys.add(key);}
      if([...merged.values()].reduce((sum,file)=>sum+file.size,0)>256*1024*1024)throw Error("资源总量超过 256 MB");
      // Validate the selected model's dependency paths before accepting files.
      const selected=models.includes(live.model)?live.model:models[0];
      const report=config.inspect(selected,JSON.parse(await additions.get(selected).text()),merged);
      if(report.missing.length)throw Error("缺少资源："+report.missing.join("、"));
      beginEdit();
      additions.forEach((file,path)=>files.set(path,file));live.model=selected;live.enabled=true;
      refresh();changed();finishEdit();
      if(skipped)$("modelStatus").textContent+=" · 已忽略 "+skipped+" 个非模型文件";
    }catch(error){$("modelStatus").textContent=error.message;$("modelStatus").classList.add("invalid");}
    importing=false;event.target.value="";
  });
  // Capture history before mutation, including resources. Pure refresh/status
  // work must not add history or overwrite the currently focused field.
  $("liveEnabled").addEventListener("change",()=>{beginEdit();live.enabled=$("liveEnabled").checked;changed();finishEdit();});
  $("modelSelect").addEventListener("change",()=>{beginEdit();live.model=$("modelSelect").value;refresh();changed();finishEdit();});
  $("liveModel").addEventListener("input",()=>{beginEdit();live.model=$("liveModel").value;status();changed();});
  document.querySelectorAll("[data-live]").forEach(input=>{input.addEventListener("input",()=>{beginEdit();live[input.dataset.live]=Number(input.value);changed();});input.addEventListener("change",finishEdit);});
  $("bongoPreset").onclick=()=>{beginEdit();live=config.bongo(live);refresh();changed();finishEdit();};
  $("hiddenParts").addEventListener("input",()=>{beginEdit();live.hiddenParts=$("hiddenParts").value.split(/[\r\n,]+/).map(s=>s.trim());changed();});
  $("liveBackground").addEventListener("input",()=>{beginEdit();ensureBackground().image=$("liveBackground").value;changed();});
  for(const id of ["liveModel","hiddenParts","liveBackground"])$(id).addEventListener("change",finishEdit);
  $("liveBackgroundFile").addEventListener("change",async event=>{importing=true;try{
    const file=event.target.files[0];if(!file)return;await validateFile(file);
    const path=config.path("ui/studio/"+file.name);if(!path)throw Error("无效文件名");
    beginEdit();files.set(path,file);ensureBackground().image=path;refresh();changed();finishEdit();
  }catch(error){$("modelStatus").textContent=error.message;}finally{importing=false;event.target.value="";}});
  refresh();
  return {get:()=>{const value=config.normalize(live);if(!value.background?.image)delete value.background;return value;},
    // Draft calibration remains undoable even before selecting a PNG; only
    // the export-facing get() omits an empty background image.
    snapshot:()=>config.normalize(live),
    set:value=>{live=config.normalize(value);refresh();},render:renderBounds,validateFile,
    async validate(){
      if(importing)throw Error("模型还在导入，请完成后再导出");
      if(files.size>2000||[...files.values()].reduce((sum,file)=>sum+file.size,0)>256*1024*1024)throw Error("资源数量或总大小超过限制");
      for(const [path,file] of files){if(!config.allowedAsset(path))throw Error("不支持的资源："+path);await validateFile(file);}
      if(!live.enabled)return;const report=await checkModel();
      if(!report)throw Error("请选择 Live2D 模型");if(report.missing.length)throw Error("缺少模型资源："+report.missing.join("、"));
      const doc=JSON.parse(await files.get(live.model).text()),display=doc.FileReferences.DisplayInfo;
      if(display){const cdi=JSON.parse(await files.get(config.resolve(live.model,display)).text());
        if(Array.isArray(cdi.Parameters)&&cdi.Parameters.length){const ids=new Set(cdi.Parameters.map(p=>p.Id));
          for(const key of config.parameters)if(live[key]&&!ids.has(live[key]))throw Error("模型参数不存在："+live[key]);
        }
      }
      for(const key of config.motions)if(live[key]&&!report.groups.includes(live[key]))throw Error("动作组不存在："+live[key]);
      let pixels=0;for(const path of new Set([...report.targets.filter(p=>/\.png$/i.test(p)),...(live.background?.image?[live.background.image]:[])])){
        if(!files.has(path))throw Error("缺少背景图片："+path);
        const view=new DataView(await files.get(path).slice(0,24).arrayBuffer());pixels+=view.getUint32(16)*view.getUint32(20);
      }if(pixels>16777216)throw Error("模型贴图总像素超过 16,777,216");
    }};
};
