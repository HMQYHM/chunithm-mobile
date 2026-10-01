// Verify the packaged app's actual page instead of navigating to source HTML.
const assert=require("node:assert/strict");
const {_electron}=require("playwright");
(async()=>{
  const app=await _electron.launch({executablePath:process.env.STUDIO_EXE,args:["--smoke-test"]});
  try {
    const page=await app.firstWindow();const errors=[];page.on("pageerror",e=>errors.push(e.message));
    await page.locator("#live2dTitle").waitFor();
    assert.match(page.url(),/app\.asar/);
    await page.locator("#liveEnabled").check();await page.locator("#bongoPreset").click();
    assert.equal(await page.locator('[data-binding="leftXParameter"]').inputValue(),"CatParamStickRX");
    await page.locator("#undoButton").dispatchEvent("click");
    assert.equal(await page.locator('[data-binding="leftXParameter"]').inputValue(),"ParamMouseX");
    await page.locator("#redoButton").dispatchEvent("click");
    assert.equal(await page.locator('[data-binding="leftXParameter"]').inputValue(),"CatParamStickRX");
    await page.locator("#previewState").selectOption("custom");
    await page.locator("#previewTrigger").selectOption("displayed_metric");
    assert.equal(await page.locator("#previewEventFields").isVisible(),true);
    assert.equal(await page.locator("#previewMode").inputValue(),"sss+");
    assert.equal(await page.locator("#replayPreview").count(),1);
    const timing=await page.evaluate(()=>window.SkinStudioRules.animation({duration:1,rotation:30}));
    assert.equal(timing.duration,1000);assert.match(timing.frames[0].transform,/rotate\(-30deg\)/);
    assert.equal(await page.evaluate(()=>typeof window.require),"undefined");
    assert.equal(await page.evaluate(()=>typeof window.studioDesktop.saveFile),"function");
    const options=await app.evaluate(({BrowserWindow})=>{const w=BrowserWindow.getAllWindows()[0];return {visible:w.isVisible(),...w.webContents.getLastWebPreferences()};});
    assert.equal(options.visible,false);assert.equal(options.nodeIntegration,false);
    assert.equal(options.contextIsolation,true);assert.equal(options.sandbox,true);
    // Hidden native windows may not composite a screenshot. Browser regression
    // owns visual QA; this smoke test covers packaged controls and isolation.
    assert.deepEqual(errors,[]);console.log("Packaged desktop passed: Live2D undo/redo, custom-event controls, animation helper, isolated renderer, hidden smoke window.");
  } finally {await app.close();}
})().catch(error=>{console.error(error);process.exitCode=1;});
