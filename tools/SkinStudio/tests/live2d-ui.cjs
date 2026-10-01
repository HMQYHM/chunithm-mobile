// Browser regression uses a real model folder without executing its native
// binary. Downloads and screenshots are disposable test-output artifacts.
const assert = require("node:assert/strict");
const fs = require("node:fs/promises");
const path = require("node:path");
const {pathToFileURL} = require("node:url");
const {chromium,_electron} = require("playwright");
(async()=>{
  const studio=path.resolve(__dirname,".."), project=path.resolve(studio,"../..");
  const sample=path.join(project,"Examples/Skins/BongoCatLive2DTouch");
  const output=path.join(studio,"test-output");await fs.mkdir(output,{recursive:true});
  const browser=process.env.STUDIO_EXE
    ? await _electron.launch({executablePath:process.env.STUDIO_EXE,args:["--smoke-test"]})
    : await chromium.launch({headless:true,...(process.env.STUDIO_BROWSER_CHANNEL?{channel:process.env.STUDIO_BROWSER_CHANNEL}:{})});
  try{
    const page=process.env.STUDIO_EXE?await browser.firstWindow():await browser.newPage({viewport:{width:1440,height:1000},acceptDownloads:true});
    const errors=[];page.on("pageerror",e=>errors.push(e.message));
    if(process.env.STUDIO_EXE)page.on("crash",()=>console.error("Packaged renderer crashed"));
    // Hidden Electron windows have no reliable compositor hit testing after
    // scrolling. Visible-browser tests cover pointer clicks; here dispatch
    // the same control event while testing the real packaged save/load code.
    const click=selector=>process.env.STUDIO_EXE?page.locator(selector).dispatchEvent("click"):page.locator(selector).click();
    const fill=(selector,value)=>process.env.STUDIO_EXE?page.locator(selector).evaluate((input,text)=>{
      input.value=text;input.dispatchEvent(new Event("input",{bubbles:true}));
    },value):page.locator(selector).fill(value);
    const desktopTemp=process.env.STUDIO_EXE?await browser.evaluate(({app})=>({root:app.getPath("temp"),pid:process.pid})):null;
    const desktopOutput=desktopTemp?path.resolve(desktopTemp.root,"chunithm-skin-studio-smoke-"+desktopTemp.pid):null;
    if(desktopOutput){
      assert.equal(path.dirname(desktopOutput),path.resolve(desktopTemp.root));
      assert.match(path.basename(desktopOutput),/^chunithm-skin-studio-smoke-\d+$/);
    }
    async function save(button,filename,destination){
      if(!desktopOutput){const wait=page.waitForEvent("download");await click(button);await(await wait).saveAs(destination);return;}
      await click(button);const source=path.join(desktopOutput,filename);
      // File creation can precede the asynchronous write. Wait for the app's
      // completed-save acknowledgement, not the first filesystem appearance.
      await page.waitForFunction(()=>document.getElementById("operationStatus").textContent.startsWith("已生成"));
      await fs.copyFile(source,destination);
    }
    if(!process.env.STUDIO_EXE)await page.goto(pathToFileURL(path.join(studio,"index.html")).href);
    else assert.match(page.url(),/app\.asar/);
    assert.equal(await page.locator("#liveTab").getAttribute("aria-selected"),"true");
    await click("#layoutTab");assert.equal(await page.locator("#liveSettings").isVisible(),false);
    await click("#liveTab");assert.equal(await page.locator("#liveSettings").isVisible(),true);
    await page.locator("#skinFolder").setInputFiles(sample);
    await page.waitForFunction(()=>document.getElementById("skinId").value==="bongocat_live2d_tablet_v4");
    assert.equal(await page.locator("#liveModel").inputValue(),"model/cat.model3.json");
    await page.locator("#live2dTitle").scrollIntoViewIfNeeded();
    await page.locator("#importJson").setInputFiles(path.join(sample,"ui/gameplay-ui.json"));
    await page.locator("#liveModel").inputValue();
    await page.locator("#modelFolder").setInputFiles(path.join(sample,"model"));
    await page.waitForFunction(()=>document.getElementById("modelStatus").textContent.includes("资源齐全"));
    await click("#bongoPreset");
    assert.equal(await page.locator('[data-binding="leftXParameter"]').inputValue(),"CatParamStickRX");
    await page.locator('[data-binding="leftXRange.min"]').fill("0.7");
    // A real click/blur, not synthetic change events, must retain edits without
    // deleting the next focused field or swallowing the export click.
    await page.locator('[data-binding="leftXRange.max"]').click();
    await page.locator('[data-binding="leftXRange.max"]').fill("0.2");
    await page.locator("details").filter({hasText:"模型背景 / 平板"}).locator("summary").click();
    await page.locator("#liveBackgroundFile").setInputFiles(path.join(sample,"ui/touch/tablet.png"));
    await page.waitForFunction(()=>document.getElementById("liveBackground").value==="ui/studio/tablet.png");
    await save("#exportJson","gameplay-ui.json",path.join(output,"gameplay-ui.json"));
    const data=JSON.parse(await fs.readFile(path.join(output,"gameplay-ui.json"),"utf8"));
    assert.equal(data.live2d.enabled,true);assert.equal(data.live2d.model,"model/cat.model3.json");
    assert.deepEqual(data.live2d.leftXRange,{min:.7,max:.2});
    assert.equal(data.live2d.background.image,"ui/studio/tablet.png");
    assert.equal(data.live2d.background.x,-.53);
    const original=JSON.parse(await fs.readFile(path.join(sample,"ui/gameplay-ui.json"),"utf8"));
    for(const key of Object.keys(original).filter(k=>!["elements","belowNoteMetric","customDisplays","live2d"].includes(k)))assert.deepEqual(data[key],original[key]);
    await save("#exportZip","skin-studio-export.zip",path.join(output,"live2d-skin.zip"));
    // Check both headers and payloads, not just a successful download event.
    const zip=await fs.readFile(path.join(output,"live2d-skin.zip"));
    const tail=zip.length-22;assert.equal(zip.readUInt32LE(tail),0x06054b50);
    const count=zip.readUInt16LE(tail+10),entries=new Map();let cursor=zip.readUInt32LE(tail+16);
    for(let i=0;i<count;i++){
      assert.equal(zip.readUInt32LE(cursor),0x02014b50);
      const length=zip.readUInt16LE(cursor+28),name=zip.toString("utf8",cursor+46,cursor+46+length);
      const local=zip.readUInt32LE(cursor+42),size=zip.readUInt32LE(cursor+24);
      assert.equal(zip.readUInt32LE(local),0x04034b50);
      const start=local+30+zip.readUInt16LE(local+26)+zip.readUInt16LE(local+28);
      entries.set(name,zip.subarray(start,start+size));
      cursor+=46+length+zip.readUInt16LE(cursor+30)+zip.readUInt16LE(cursor+32);
    }
    assert.deepEqual(JSON.parse(entries.get("ui/gameplay-ui.json").toString()),data);
    assert.deepEqual(entries.get("model/demomodel3.moc3"),await fs.readFile(path.join(sample,"model/demomodel3.moc3")));
    assert(entries.has("model/demomodel3.1024/texture_00.png"));
    assert(entries.has("ui/touch/tablet.png"));
    assert.equal(JSON.parse(entries.get("skin.json").toString()).id,"bongocat_live2d_tablet_v4");
    // Reopening the actual exported project proves that references survive
    // a close/reopen workflow, not merely a single editor session.
    const reopened=path.join(output,"exported-skin");
    for(const [name,bytes] of entries){const destination=path.join(reopened,name);
      await fs.mkdir(path.dirname(destination),{recursive:true});await fs.writeFile(destination,bytes);}
    await page.locator("#skinFolder").setInputFiles(reopened);
    await page.waitForFunction(()=>document.querySelector('[data-binding="leftXRange.min"]').value==="0.7");
    assert.equal(await page.locator("#liveModel").inputValue(),"model/cat.model3.json");
    assert.equal(await page.locator('[data-binding="background.x"]').inputValue(),"-0.53");
    await page.locator("#live2dTitle").scrollIntoViewIfNeeded();
    if(!process.env.STUDIO_EXE)await page.screenshot({path:path.join(output,"live2d-desktop.png"),fullPage:true});
    if(!process.env.STUDIO_EXE){
      await page.setViewportSize({width:390,height:844});
      await page.locator("#live2dTitle").scrollIntoViewIfNeeded();
      await page.screenshot({path:path.join(output,"live2d-mobile.png")});
      assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth>window.innerWidth),false,"mobile horizontal overflow");
      await page.setViewportSize({width:1440,height:1000});
    }
    async function errorAction(action,pattern){await action();await page.waitForFunction(source=>{
      const status=document.getElementById("operationStatus");return status.classList.contains("invalid")&&new RegExp(source).test(status.textContent);
    },pattern.source);assert.match(await page.locator("#operationStatus").textContent(),pattern);}
    async function rejected(pattern){await errorAction(()=>click("#exportZip"),pattern);}
    await fill('[data-binding="leftXParameter"]',"NonexistentPaw");
    await rejected(/模型参数不存在/);
    await fill('[data-binding="leftXParameter"]',"CatParamStickRX");
    assert.equal(await page.evaluate(()=>document.querySelector("main").inert),false,"Validation failure locked the editor");
    await click("#layoutTab");
    await click("#addElement");
    await fill("#image","ui/missing.png");
    await rejected(/缺少图片/);
    await fill("#image","");
    // A plausible PNG header with no actual image data must be rejected.
    await errorAction(()=>page.locator("#imageFile").setInputFiles({name:"broken.png",mimeType:"image/png",
      buffer:Buffer.from([137,80,78,71,13,10,26,10,0,0,0,13,73,72,68,82,0,0,0,16,0,0,0,16])}),/图片读取失败/);
    await click("#liveTab");
    // Whole-folder replacement failure must not corrupt the open project.
    await errorAction(()=>page.locator("#skinFolder").setInputFiles(path.join(sample,"model")),/皮肤根目录/);
    assert.equal(await page.locator("#liveModel").inputValue(),"model/cat.model3.json");
    // Export must fail closed when an enabled model points to missing resources.
    await fill("#liveModel","model/missing.model3.json");await page.locator("#liveModel").dispatchEvent("change");
    await rejected(/缺少模型文件/);
    if(desktopOutput){
      const denied=await page.evaluate(async()=>{try{await window.studioDesktop.saveFile("../arbitrary.json",new Uint8Array([1]));return false;}catch{return true;}});
      assert.equal(denied,true,"Desktop bridge allowed an arbitrary filename");
      await fs.rm(desktopOutput,{recursive:true,force:true});
    }
    assert.deepEqual(errors,[]);console.log("Live2D UI passed: folder import/reopen, immediate edits, calibration, JSON preservation, actual ZIP bytes, corrupt PNG/missing-image/parameter/path guards. Target: "+(desktopOutput?"packaged desktop with real disk export":"browser with desktop/mobile screenshots"));
  }finally{await browser.close();}
})().catch(error=>{console.error(error);process.exitCode=1;});
