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
    assert.equal(await page.evaluate(()=>typeof window.require),"undefined");
    const options=await app.evaluate(({BrowserWindow})=>{const w=BrowserWindow.getAllWindows()[0];return {visible:w.isVisible(),...w.webContents.getLastWebPreferences()};});
    assert.equal(options.visible,false);assert.equal(options.nodeIntegration,false);
    assert.equal(options.contextIsolation,true);assert.equal(options.sandbox,true);
    // Hidden native windows may not composite a screenshot. Browser regression
    // owns visual QA; this smoke test covers packaged controls and isolation.
    assert.deepEqual(errors,[]);console.log("Packaged desktop passed: Live2D controls, isolated renderer, hidden smoke window.");
  } finally {await app.close();}
})().catch(error=>{console.error(error);process.exitCode=1;});
