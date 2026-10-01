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
    await page.goto(pathToFileURL(path.join(studio,"index.html")).href);
    assert.equal(await page.locator("#liveTab").getAttribute("aria-selected"),"true");
    await page.locator("#layoutTab").click();assert.equal(await page.locator("#liveSettings").isVisible(),false);
    await page.locator("#liveTab").click();assert.equal(await page.locator("#liveSettings").isVisible(),true);
    await page.locator("#live2dTitle").scrollIntoViewIfNeeded();
    await page.locator("#importJson").setInputFiles(path.join(sample,"ui/gameplay-ui.json"));
    await page.locator("#liveModel").inputValue();
    await page.locator("#modelFolder").setInputFiles(path.join(sample,"model"));
    await page.waitForFunction(()=>document.getElementById("modelStatus").textContent.includes("资源齐全"));
    await page.locator("#bongoPreset").click();
    assert.equal(await page.locator('[data-binding="leftXParameter"]').inputValue(),"CatParamStickRX");
    await page.locator('[data-binding="leftXRange.min"]').fill("0.7");
    await page.locator('[data-binding="leftXRange.min"]').dispatchEvent("change");
    await page.locator('[data-binding="leftXRange.max"]').fill("0.2");
    await page.locator('[data-binding="leftXRange.max"]').dispatchEvent("change");
    await page.locator("details").filter({hasText:"模型背景 / 平板"}).locator("summary").click();
    await page.locator("#liveBackgroundFile").setInputFiles(path.join(sample,"ui/touch/tablet.png"));
    await page.waitForFunction(()=>document.getElementById("liveBackground").value==="ui/studio/tablet.png");
    const jsonWait=page.waitForEvent("download");await page.locator("#exportJson").click();
    const jsonDownload=await jsonWait;await jsonDownload.saveAs(path.join(output,"gameplay-ui.json"));
    const data=JSON.parse(await fs.readFile(path.join(output,"gameplay-ui.json"),"utf8"));
    assert.equal(data.live2d.enabled,true);assert.equal(data.live2d.model,"model/cat.model3.json");
    assert.deepEqual(data.live2d.leftXRange,{min:.7,max:.2});
    assert.equal(data.live2d.background.image,"ui/studio/tablet.png");
    const original=JSON.parse(await fs.readFile(path.join(sample,"ui/gameplay-ui.json"),"utf8"));
    for(const key of Object.keys(original).filter(k=>!["elements","belowNoteMetric","customDisplays","live2d"].includes(k)))assert.deepEqual(data[key],original[key]);
    const zipWait=page.waitForEvent("download");await page.locator("#exportZip").click();
    await (await zipWait).saveAs(path.join(output,"live2d-skin.zip"));
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
    await page.locator("#live2dTitle").scrollIntoViewIfNeeded();await page.screenshot({path:path.join(output,"live2d-desktop.png"),fullPage:true});
    await page.setViewportSize({width:390,height:844});
    await page.locator("#live2dTitle").scrollIntoViewIfNeeded();await page.screenshot({path:path.join(output,"live2d-mobile.png")});
    assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth>window.innerWidth),false,"mobile horizontal overflow");
    // Export must fail closed when an enabled model points to missing resources.
    await page.locator("#liveModel").fill("model/missing.model3.json");await page.locator("#liveModel").dispatchEvent("change");
    const dialogWait=page.waitForEvent("dialog"),click=page.locator("#exportZip").click();
    const dialog=await dialogWait;assert.match(dialog.message(),/缺少模型文件/);await dialog.accept();await click;
    assert.deepEqual(errors,[]);console.log("Live2D UI passed: real folder import, calibration, JSON preservation, ZIP export, missing-file guard, desktop/mobile screenshots.");
  }finally{await browser.close();}
})().catch(error=>{console.error(error);process.exitCode=1;});
