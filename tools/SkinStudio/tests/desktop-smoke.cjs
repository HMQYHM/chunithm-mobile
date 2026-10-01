// Verify the packaged app's actual page instead of navigating to source HTML.
const assert=require("node:assert/strict");
const {_electron}=require("playwright");
const fs=require("node:fs/promises");
const path=require("node:path");
(async()=>{
  const app=await _electron.launch({executablePath:process.env.STUDIO_EXE,args:["--smoke-test"]});
  try {
    const page=await app.firstWindow();const errors=[];page.on("pageerror",e=>errors.push(e.message));
    // Chromium may surface the beforeunload guard as a native dialog when
    // the hidden smoke window is closed; dismiss it so cleanup is deterministic.
    page.on("dialog",dialog=>dialog.dismiss());
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
    assert.equal(await page.evaluate(()=>{const event=new Event("beforeunload",{cancelable:true});window.dispatchEvent(event);return event.defaultPrevented;}),true,"Unsaved close guard did not run");
    // Exercise real native writes twice, including replacement and cleanup of
    // the temporary file. Only this process-specific smoke directory is used.
    const location=await app.evaluate(({app})=>({temp:app.getPath("temp"),pid:process.pid}));
    const output=path.resolve(location.temp,"chunithm-skin-studio-smoke-"+location.pid);
    assert.equal(path.dirname(output),path.resolve(location.temp));assert.match(path.basename(output),/^chunithm-skin-studio-smoke-\d+$/);
    for(const value of ["first","replacement"])assert.equal(await page.evaluate(value=>window.studioDesktop.saveFile("gameplay-ui.json",new TextEncoder().encode(value)),value),true);
    assert.equal(await fs.readFile(path.join(output,"gameplay-ui.json"),"utf8"),"replacement");
    assert(!(await fs.readdir(output)).some(name=>name.endsWith(".tmp")),"Native save left a temporary file");
    assert.equal(await page.evaluate(async()=>{try{await window.studioDesktop.saveFile("../bad.json",new Uint8Array([1]));return false;}catch{return true;}}),true);
    await fs.rm(output,{recursive:true,force:true});
    const options=await app.evaluate(({BrowserWindow})=>{const w=BrowserWindow.getAllWindows()[0];return {visible:w.isVisible(),...w.webContents.getLastWebPreferences()};});
    assert.equal(options.visible,false);assert.equal(options.nodeIntegration,false);
    assert.equal(options.contextIsolation,true);assert.equal(options.sandbox,true);
    // Hidden native windows may not composite a screenshot. Browser regression
    // owns visual QA; this smoke test covers packaged controls and isolation.
    assert.deepEqual(errors,[]);console.log("Packaged desktop passed: Live2D undo/redo, custom events, close guard, atomic native replacement, path rejection and isolated renderer.");
  } finally {await app.close();}
})().catch(error=>{console.error(error);process.exitCode=1;});
