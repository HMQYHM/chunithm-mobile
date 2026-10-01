// Test public controls, animation frames and saved bytes in the real browser.
const assert=require("node:assert/strict");
const fs=require("node:fs/promises");
const path=require("node:path");
const {pathToFileURL}=require("node:url");
const {chromium}=require("playwright");
(async()=>{
  const studio=path.resolve(__dirname,".."),sample=path.resolve(studio,"../../Examples/Skins/BongoCatLive2DTouch"),output=path.join(studio,"test-output");
  await fs.mkdir(output,{recursive:true});
  const browser=await chromium.launch({headless:true,channel:process.env.STUDIO_BROWSER_CHANNEL||"msedge"});
  try{
    const page=await browser.newPage({viewport:{width:1440,height:1000},acceptDownloads:true}),errors=[];
    page.on("pageerror",error=>errors.push(error.message));await page.goto(pathToFileURL(path.join(studio,"index.html")).href);
    await page.locator("#ruleTab").click();await page.locator("#addRulePreset").click();
    await page.locator("#ruleImageFile").setInputFiles(path.join(sample,"ui/touch/tablet.png"));
    await page.waitForFunction(()=>document.getElementById("operationStatus").textContent==="规则图片已导入");
    await page.locator('[data-rule="duration"]').fill("2");await page.locator('[data-rule="fadeIn"]').fill("0.5");
    await page.locator('[data-rule="fadeOut"]').fill("0.5");await page.locator('[data-rule="opacity"]').fill("0.6");
    await page.locator('[data-rule="scaleFrom"]').fill("0.5");await page.locator('[data-rule="scaleTo"]').fill("1.5");
    await page.locator('[data-rule="rotation"]').fill("30");await page.locator('[data-rule="rotationTo"]').fill("90");
    await page.locator('[data-rule="animateRotation"]').check();await page.locator("#previewState").selectOption("custom");
    await page.locator("#previewCurrent").fill("101");assert.equal(await page.locator(".rule-preview").count(),1);
    await page.locator('[data-rule="condition"]').selectOption("equal");assert.equal(await page.locator(".rule-preview").count(),0);
    await page.locator("#previewCurrent").fill("");assert.equal(await page.locator(".rule-preview").count(),0);
    assert.match(await page.locator("#previewResult").textContent(),/范围内的整数/);
    await page.locator("#previewCurrent").fill("100");await page.locator("#replayPreview").click();
    const frame=await page.locator(".rule-preview").evaluate(node=>{
      const animation=node.getAnimations()[0];animation.pause();animation.currentTime=1000;
      const style=getComputedStyle(node),matrix=new DOMMatrix(style.transform);
      return {alpha:Number(style.opacity),scale:Math.hypot(matrix.a,matrix.b),angle:Math.atan2(matrix.b,matrix.a)*180/Math.PI};
    });
    assert(Math.abs(frame.alpha-.6)<.001);assert(Math.abs(frame.scale-1)<.001);assert(Math.abs(frame.angle+60)<.01);
    await page.locator(".rule-preview img").evaluate(image=>image.decode());
    assert(await page.locator(".rule-preview img").evaluate(image=>image.naturalWidth>0));
    await page.locator("#stage").screenshot({path:path.join(output,"interactions-animation.png")});
    await page.locator("#stopPreview").click();assert.equal(await page.evaluate(()=>document.getElementById("stage").getAnimations({subtree:true}).length),0);
    await page.locator("#replayPreview").click();await page.locator(".rule-preview").evaluate(node=>node.getAnimations()[0].finish());
    await page.waitForFunction(()=>document.getElementById("stopPreview").disabled);
    assert.equal(await page.locator(".rule-preview").evaluate(node=>getComputedStyle(node).opacity),"0");
    await page.locator("#rulePreset").selectOption("target");await page.locator("#addRulePreset").click();
    await page.locator('[data-rule="image"]').selectOption({index:1});await page.locator("#layoutTab").click();
    await page.locator("#metricMode").selectOption("sss+");await page.locator("#previewState").selectOption("metricZero");assert.equal(await page.locator(".rule-preview").count(),0);
    await page.locator("#previewState").selectOption("metricNegative");assert.equal(await page.locator(".rule-preview").count(),1);
    assert.match(await page.locator("#metricDemo").textContent(),/SSS\+.*-1/);
    await page.locator("#ruleTab").click();await page.locator('[data-rule="trigger"]').selectOption("touch");
    await page.locator('[data-rule="value"]').selectOption("lefttop");await page.locator('[data-rule="condition"]').selectOption("exit");
    await page.locator("#previewState").selectOption("custom");await page.locator("#previewTrigger").selectOption("touch");await page.locator("#previewValue").selectOption("lefttop");
    assert.equal(await page.locator(".rule-preview").count(),0);await page.locator("#previewEdge").selectOption("exit");assert.equal(await page.locator(".rule-preview").count(),1);
    // Preview controls do not add undo records or get exported into a skin.
    const history=await page.locator("#historyStatus").textContent();await page.locator("#previewValue").selectOption("righttop");
    assert.equal(await page.locator("#historyStatus").textContent(),history);
    await page.locator("#skinFolder").setInputFiles(sample);await page.locator("#discardProject").click();await page.waitForFunction(()=>document.getElementById("skinId").value==="bongocat_live2d_tablet_v4");
    await page.locator("#liveTab").click();const initial=await page.locator('[data-binding="leftXRange.min"]').inputValue();
    await page.locator('[data-binding="leftXRange.min"]').fill("0.4");await page.locator('[data-binding="leftXRange.min"]').dispatchEvent("change");
    await page.locator("#undoButton").click();assert.equal(await page.locator('[data-binding="leftXRange.min"]').inputValue(),initial);
    await page.locator("#redoButton").click();assert.equal(await page.locator('[data-binding="leftXRange.min"]').inputValue(),"0.4");
    await page.locator('[data-live="x"]').fill("0.09");await page.locator('[data-live="x"]').dispatchEvent("change");
    await page.locator("#undoButton").click();assert.equal(await page.locator('[data-live="x"]').inputValue(),"0.015");
    await page.locator("#liveEnabled").uncheck();await page.locator("#undoButton").click();assert.equal(await page.locator("#liveEnabled").isChecked(),true);
    await page.locator("details").filter({hasText:"模型背景 / 平板"}).locator("summary").click();
    await page.locator("#liveBackground").fill("");await page.locator("#liveBackground").dispatchEvent("change");
    await page.locator('[data-binding="background.x"]').fill("0.2");await page.locator('[data-binding="background.x"]').dispatchEvent("change");
    await page.locator("#undoButton").click();assert.equal(await page.locator('[data-binding="background.x"]').inputValue(),"-0.53");
    assert.equal(await page.locator("#liveBackground").inputValue(),"");await page.locator("#redoButton").click();
    const filesBefore=await page.locator("#fileSummary").textContent();
    await page.locator("#liveBackgroundFile").setInputFiles(path.join(sample,"ui/touch/tablet.png"));
    await page.waitForFunction(()=>document.getElementById("liveBackground").value==="ui/studio/tablet.png");
    await page.locator("#undoButton").click();assert.equal(await page.locator("#liveBackground").inputValue(),"");
    assert.equal(await page.locator("#fileSummary").textContent(),filesBefore);assert.equal(await page.locator('[data-binding="background.x"]').inputValue(),"0.2");
    await page.locator("#redoButton").click();assert.equal(await page.locator("#liveBackground").inputValue(),"ui/studio/tablet.png");
    const brokenModel=path.join(output,"incomplete-model");await fs.mkdir(brokenModel,{recursive:true});
    const historyBefore=await page.locator("#historyStatus").textContent(),resourcesBefore=await page.locator("#fileSummary").textContent();
    // A missing dependency must reject the entire folder before replacing any
    // current resource or recording an undo step.
    await fs.writeFile(path.join(brokenModel,"cat.model3.json"),JSON.stringify({FileReferences:{Moc:"missing.moc3",Textures:["missing.png"]}}));
    await page.locator("#modelFolder").setInputFiles(brokenModel);await page.waitForFunction(()=>document.getElementById("modelStatus").textContent.includes("缺少资源"));
    assert.equal(await page.locator("#liveModel").inputValue(),"model/cat.model3.json");assert.equal(await page.locator("#fileSummary").textContent(),resourcesBefore);
    assert.equal(await page.locator("#historyStatus").textContent(),historyBefore);
    // Failed imports must leave the active model, metadata and undo intact.
    for(const elements of [[{id:"songInfo",x:"0.2"}],[{id:"songInfo",width:null}],[{id:"songInfo",x:.95,width:.2}],[{id:"songInfo"},{id:"songInfo"}],[null]]){
      await page.locator("#importJson").setInputFiles({name:"invalid.json",mimeType:"application/json",buffer:Buffer.from(JSON.stringify({elements}))});
      await page.waitForFunction(()=>document.getElementById("operationStatus").textContent.startsWith("JSON 读取失败"));
      assert.equal(await page.locator("#skinId").inputValue(),"bongocat_live2d_tablet_v4");assert.equal(await page.locator("#liveModel").inputValue(),"model/cat.model3.json");
    }
    const save=page.waitForEvent("download");await page.locator("#saveProject").click();await(await save).saveAs(path.join(output,"interactions-project.zip"));
    // Exercise actual responsive layout with all custom-event controls visible.
    await page.locator("#previewState").selectOption("custom");await page.locator("#previewTrigger").selectOption("displayed_metric");
    await page.screenshot({path:path.join(output,"interactions-desktop.png"),fullPage:true});
    await page.setViewportSize({width:390,height:844});await page.locator("#stage").scrollIntoViewIfNeeded();
    await page.screenshot({path:path.join(output,"interactions-mobile.png")});
    assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth>innerWidth),false);
    assert.deepEqual(errors,[]);console.log("Studio interactions passed: animated frames, replay/stop, custom transitions, target loss, touch exit, Live2D calibration/resource undo, invalid-import isolation and responsive layout.");
  }finally{await browser.close();}
})().catch(error=>{console.error(error);process.exitCode=1;});
