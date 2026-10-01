// Exercise edits, discard guards and gestures through the public editor UI.
const assert=require("node:assert/strict");
const fs=require("node:fs/promises");
const path=require("node:path");
const {pathToFileURL}=require("node:url");
const {chromium}=require("playwright");
(async()=>{
  const studio=path.resolve(__dirname,".."),output=path.join(studio,"test-output"),sample=path.resolve(studio,"../../Examples/Skins/BongoCatLive2DTouch");
  await fs.mkdir(output,{recursive:true});const browser=await chromium.launch({headless:true,channel:process.env.STUDIO_BROWSER_CHANNEL||"msedge"});
  try{
    const page=await browser.newPage({viewport:{width:1440,height:1000},acceptDownloads:true}),errors=[];
    page.on("pageerror",error=>errors.push(error.message));await page.goto(pathToFileURL(path.join(studio,"index.html")).href);
    const dirty=()=>page.locator("#saveStatus").getAttribute("data-dirty");
    assert.equal(await dirty(),"false");await page.locator("#layoutTab").click();await page.locator("#layerList .layer-select").nth(2).click();
    await page.locator("#previewState").selectOption("combo100");assert.equal(await dirty(),"false","Selection dirtied the project");
    await page.locator("#label").fill("test saved label");await page.locator("#label").dispatchEvent("change");assert.equal(await dirty(),"true");
    await page.locator("#undoButton").click();assert.equal(await dirty(),"false");await page.locator("#redoButton").click();assert.equal(await dirty(),"true");
    // Exporting JSON is not equivalent to saving the whole resource package.
    async function download(selector,name){const wait=page.waitForEvent("download");await page.locator(selector).click();const file=path.join(output,name);await(await wait).saveAs(file);return file;}
    await download("#exportJson","workflow-ui.json");assert.equal(await dirty(),"true");
    await page.evaluate(()=>window.studioDesktop={saveFile:async()=>false});await page.locator("#saveProject").click();
    await page.waitForFunction(()=>document.getElementById("operationStatus").textContent==="已取消保存");assert.equal(await dirty(),"true");
    await page.evaluate(()=>delete window.studioDesktop);await download("#saveProject","workflow-saved.zip");assert.equal(await dirty(),"false");
    const image=path.join(sample,"ui/touch/tablet.png");await page.locator("#imageFile").setInputFiles(image);
    await page.waitForFunction(()=>!document.querySelector("main").inert);await download("#saveProject","workflow-with-image.zip");assert.equal(await dirty(),"false");
    // Conservative identity tracking also catches same-name/same-size asset
    // replacements. Undo restores the saved File object and clean checkpoint.
    await page.locator("#imageFile").setInputFiles(image);await page.waitForFunction(()=>!document.querySelector("main").inert);
    assert.equal(await dirty(),"true");await page.locator("#undoButton").click();assert.equal(await dirty(),"false");
    const savedName=await page.locator("#skinName").inputValue();await page.locator("#skinName").fill("Changed after save");assert.equal(await dirty(),"true");
    await page.locator("#undoButton").click();assert.equal(await page.locator("#skinName").inputValue(),savedName);assert.equal(await dirty(),"false");
    await page.locator("#redoButton").click();
    await page.locator("#skinFolder").setInputFiles(sample);await page.locator("#replaceProjectDialog").waitFor({state:"visible"});
    assert.equal(await page.evaluate(()=>document.querySelector("main").inert),true);
    await page.screenshot({path:path.join(output,"workflow-confirm-desktop.png"),fullPage:true});
    await page.setViewportSize({width:390,height:844});await page.screenshot({path:path.join(output,"workflow-confirm-mobile.png")});
    assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth>innerWidth),false);await page.setViewportSize({width:1440,height:1000});
    await page.locator("#cancelReplace").click();await page.waitForFunction(()=>!document.querySelector("main").inert);
    assert.equal(await page.locator("#skinName").inputValue(),"Changed after save");assert.equal(await dirty(),"true");
    await page.locator("#skinFolder").setInputFiles(sample);await page.locator("#discardProject").click();
    await page.waitForFunction(()=>document.getElementById("skinId").value==="bongocat_live2d_tablet_v4");assert.equal(await dirty(),"false");
    const config={elements:[{id:"songInfo",visible:true,x:.1,y:.1,width:.3,height:.3},{id:"scoreHud",visible:true,x:.1,y:.1,width:.3,height:.3}]};
    await page.locator("#importJson").setInputFiles({name:"layout.json",mimeType:"application/json",buffer:Buffer.from(JSON.stringify(config))});
    await page.waitForFunction(()=>document.getElementById("operationStatus").textContent==="配置已读取");assert.equal(await dirty(),"true");
    await page.locator("#layoutTab").click();
    const stage=await page.locator("#stage").boundingBox(),point={x:stage.x+.2*stage.width,y:stage.y+.8*stage.height};
    // A different DOM element under the same point must not displace the
    // selected overlapping layer. No movement means no extra undo step.
    const historyBefore=await page.locator("#historyStatus").textContent();
    await page.locator('.skin-element[data-id="scoreHud"]').dispatchEvent("pointerdown",{pointerId:23,button:0,clientX:point.x,clientY:point.y});
    await page.waitForTimeout(280);await page.evaluate(p=>document.dispatchEvent(new PointerEvent("pointerup",{pointerId:23,button:0,clientX:p.x,clientY:p.y,bubbles:true})),point);
    assert.equal(await page.locator("#elementId").inputValue(),"songInfo");assert.equal(await page.locator("#historyStatus").textContent(),historyBefore);
    await page.locator("#ruleTab").click();await page.locator("#addRulePreset").click();await page.locator("#placeRule").check();
    assert.equal(await page.locator(".rule-placement").count(),1,"A rule without an image could not be positioned");
    const before={x:await page.locator('[data-rule="x"]').inputValue(),width:await page.locator('[data-rule="width"]').inputValue()};
    await page.locator(".rule-placement").scrollIntoViewIfNeeded();let box=await page.locator(".rule-placement").boundingBox();
    await page.mouse.move(box.x+box.width/2,box.y+box.height/2);await page.mouse.down();await page.waitForTimeout(300);
    await page.mouse.move(box.x+box.width/2+35,box.y+box.height/2+15,{steps:3});await page.mouse.up();
    const moved=await page.locator('[data-rule="x"]').inputValue();assert.notEqual(moved,before.x);
    await page.locator("#undoButton").click();assert.equal(await page.locator('[data-rule="x"]').inputValue(),before.x);
    await page.locator("#redoButton").click();assert.equal(await page.locator('[data-rule="x"]').inputValue(),moved);
    box=await page.locator(".rule-resize").boundingBox();await page.mouse.move(box.x+box.width/2,box.y+box.height/2);await page.mouse.down();
    await page.mouse.move(box.x+box.width/2+25,box.y+box.height/2-25,{steps:3});await page.mouse.up();
    assert(Number(await page.locator('[data-rule="width"]').inputValue())>Number(before.width));
    await page.locator("#undoButton").click();assert.equal(await page.locator('[data-rule="width"]').inputValue(),before.width);
    await page.screenshot({path:path.join(output,"workflow-placement-desktop.png"),fullPage:true});
    // Failed save cannot establish a clean checkpoint for an incomplete rule.
    await page.locator("#saveProject").click();await page.waitForFunction(()=>document.getElementById("operationStatus").textContent.startsWith("保存失败"));assert.equal(await dirty(),"true");
    await page.setViewportSize({width:390,height:844});await page.locator("#stage").scrollIntoViewIfNeeded();await page.screenshot({path:path.join(output,"workflow-placement-mobile.png")});
    assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth>innerWidth),false);
    assert.deepEqual(errors,[]);console.log("Editor workflow passed: dirty checkpoints, cancel/invalid save, discard modal isolation, overlap priority, no-op gesture history, rule placement, resize and undo/redo.");
  }finally{await browser.close();}
})().catch(error=>{console.error(error);process.exitCode=1;});
