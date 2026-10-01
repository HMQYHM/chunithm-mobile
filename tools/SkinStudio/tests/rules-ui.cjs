// Exercise actual controls and exported ZIP bytes, not an editor-only state object.
const assert=require("node:assert/strict");
const fs=require("node:fs/promises");
const path=require("node:path");
const {pathToFileURL}=require("node:url");
const {chromium}=require("playwright");
function entries(buffer){
  const result=new Map();let pos=buffer.readUInt32LE(buffer.length-6),count=buffer.readUInt16LE(buffer.length-12);
  for(let i=0;i<count;i++){const length=buffer.readUInt16LE(pos+28),name=buffer.toString("utf8",pos+46,pos+46+length),local=buffer.readUInt32LE(pos+42),size=buffer.readUInt32LE(pos+24);assert.equal(buffer.readUInt32LE(pos),0x02014b50);const start=local+30+buffer.readUInt16LE(local+26)+buffer.readUInt16LE(local+28);result.set(name,buffer.subarray(start,start+size));pos+=46+length+buffer.readUInt16LE(pos+30)+buffer.readUInt16LE(pos+32);}return result;
}
(async()=>{
  const studio=path.resolve(__dirname,".."),output=path.join(studio,"test-output");await fs.mkdir(output,{recursive:true});
  const browser=await chromium.launch({headless:true,channel:process.env.STUDIO_BROWSER_CHANNEL||"msedge"});
  try{
    const page=await browser.newPage({viewport:{width:1440,height:1000},acceptDownloads:true}),errors=[];
    page.on("pageerror",e=>errors.push(e.message));await page.goto(pathToFileURL(path.join(studio,"index.html")).href);
    await page.locator("#layoutTab").click();await page.locator("#addRegion").click();assert.equal(await page.locator("#elementId").inputValue(),"touchpad");
    await page.locator("#x").fill("0.15");await page.locator("#regionPreset").selectOption("airZone");await page.locator("#addRegion").click();assert.equal(await page.locator("#elementId").inputValue(),"airZone");
    await page.locator("#ruleTab").click();await page.locator("#addRulePreset").click();assert.equal(await page.locator('[data-rule="threshold"]').inputValue(),"100");
    const png=path.resolve(studio,"../../Examples/Skins/BongoCatLive2DTouch/ui/touch/tablet.png");
    await page.locator("#ruleImageFile").setInputFiles(png);await page.waitForFunction(()=>document.getElementById("operationStatus").textContent==="规则图片已导入");
    await page.locator("#previewState").selectOption("combo100");assert.equal(await page.locator(".rule-preview img").count(),1);
    await page.locator('[data-rule="condition"]').selectOption("equal");await page.locator('[data-rule="threshold"]').fill("101");
    assert.equal(await page.locator(".rule-preview img").count(),0);
    await page.locator('[data-rule="threshold"]').fill("100");await page.locator('[data-rule="threshold"]').dispatchEvent("change");
    await page.locator('[data-rule="enabled"]').uncheck();assert.equal(await page.locator(".rule-preview img").count(),0);
    await page.locator('[data-rule="enabled"]').check();
    await page.locator("#rulePreset").selectOption("miss");await page.locator("#addRulePreset").click();
    assert.equal(await page.locator('[data-rule="value"]').inputValue(),"miss");
    await page.locator('[data-rule="image"]').selectOption({index:1});await page.locator("#previewState").selectOption("miss");assert.equal(await page.locator(".rule-preview img").count(),1);
    await page.locator('[data-rule="trigger"]').selectOption("air");assert.equal(await page.locator('[data-rule="value"]').inputValue(),"active");
    await page.locator('[data-rule="trigger"]').selectOption("judgement");await page.locator('[data-rule="value"]').selectOption("miss");
    await page.locator("#sceneTab").click();await page.locator("#sceneImageFile").setInputFiles(png);await page.waitForFunction(()=>document.getElementById("operationStatus").textContent==="场景图片已导入");
    await page.locator("#sceneBackgroundImage").selectOption("ui/scene/tablet.png");await page.locator("#sceneTrackImage").selectOption("ui/scene/tablet.png");await page.locator("#sceneLaneImage").selectOption("ui/scene/tablet.png");
    await page.locator("#sceneTrackColor").fill("#448844");await page.locator("#sceneTrackColor").dispatchEvent("change");
    assert.equal(await page.locator(".scene-background").count(),1);
    async function save(selector,name){const wait=page.waitForEvent("download");await page.locator(selector).click();const target=path.join(output,name);await(await wait).saveAs(target);return target;}
    const zip=await save("#saveProject","rules-project.zip"),archive=entries(await fs.readFile(zip));const data=JSON.parse(archive.get("ui/gameplay-ui.json"));
    assert.equal(data.customDisplays.length,2);assert.equal(data.customDisplays[0].threshold,100);assert.equal(data.customDisplays[1].value,"miss");assert.equal(data.scene.track.color,"#448844");assert.equal(data.elements.find(e=>e.id==="touchpad").x,.15);
    assert(archive.has("ui/scene/tablet.png"));assert(archive.has(data.customDisplays[0].image));
    await page.locator("#skinZip").setInputFiles(zip);await page.waitForFunction(()=>document.getElementById("operationStatus").textContent.startsWith("皮肤 ZIP 已打开"));
    assert.equal(await page.locator(".rule-row").count(),2);assert.equal(await page.locator("#sceneTrackColor").inputValue(),"#448844");
    // Scene edits each finish one undo transaction, including asset references.
    for(let i=0;i<23;i++){await page.locator("#sceneBackgroundOpacity").fill(String(i/25));await page.locator("#sceneBackgroundOpacity").dispatchEvent("change");}
    assert.match(await page.locator("#historyStatus").textContent(),/20\/20/);
    await page.locator("#undoButton").click();assert.equal(await page.locator("#sceneBackgroundOpacity").inputValue(),"0.84");
    await page.locator("#redoButton").click();assert.equal(await page.locator("#sceneBackgroundOpacity").inputValue(),"0.88");
    await page.locator("#ruleTab").click();await page.locator("#ruleEditor").scrollIntoViewIfNeeded();await page.screenshot({path:path.join(output,"rules-desktop.png"),fullPage:true});
    await page.setViewportSize({width:390,height:844});await page.locator("#rulesTitle").scrollIntoViewIfNeeded();await page.screenshot({path:path.join(output,"rules-mobile.png")});
    assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth>innerWidth),false,"mobile overflow");await page.setViewportSize({width:1440,height:1000});
    // Motion export fails closed if no enabled model supplies that group.
    await page.locator('[data-rule="action"]').selectOption("live2dMotion");await page.locator('[data-rule="motion"]').fill("celebrate");
    await page.locator("#exportZip").click();await page.waitForFunction(()=>document.getElementById("operationStatus").textContent.includes("启用并导入模型"));
    assert.equal(await page.evaluate(()=>document.querySelector("main").inert),false);
    // Imported labels are text, never HTML elements supplied by a package.
    await page.locator('[data-rule="action"]').selectOption("image");assert.equal(await page.locator("#ruleImageFile").count(),1);
    assert.deepEqual(errors,[]);console.log("Rules UI passed: presets, exact preview, disable, image import, scene assets, ZIP round trip, 20-step undo/redo, motion guard, desktop/mobile layout.");
  }finally{await browser.close();}
})().catch(error=>{console.error(error);process.exitCode=1;});
