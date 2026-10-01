// Node regression tests share the same validation code as browser exports.
const assert = require("node:assert/strict");
const test = require("node:test");
const config = require("../live2d-config.js");
test("preserves calibrated mappings and reversed ranges",()=>{
  const model=config.bongo({enabled:true,model:"model/cat.model3.json",leftXRange:{min:.8,max:.2},hiddenParts:["Part8","Part8","../bad"]});
  assert.equal(model.leftXParameter,"CatParamStickRX");
  assert.equal(model.rightXParameter,"CatParamStickLX");
  assert.deepEqual(model.leftXRange,{min:.8,max:.2});
  assert.deepEqual(model.hiddenParts,["Part8"]);
});
test("bounds nonfinite values and rejects unsafe resources",()=>{
  const model=config.normalize({x:.99,width:Infinity,y:NaN,model:"../cat.model3.json",background:{image:"C:/cat.png"}});
  assert(model.x+model.width<=1);assert.equal(model.model,"");assert.equal(model.background,undefined);
  for(const path of ["../a.moc3","/a.png","C:/a.png","https://a.png","x//a.png","model/code.js","model/code.dll"])
    assert.equal(config.allowedAsset(path),false,path);
  for(const path of ["model/a.moc3","model/a.model3.json","model/a.motion3.json","model/a.cdi3.json","model/a.png","model/LICENSE-MIT.txt"])
    assert.equal(config.allowedAsset(path),true,path);
});
test("reports missing model references and rejects traversal",()=>{
  const doc={FileReferences:{Moc:"cat.moc3",Textures:["textures/a.png"],Motions:{idle:[{File:"idle.motion3.json"}]}}};
  const files=new Map([["model/cat.moc3",{}],["model/textures/a.png",{}]]);
  const result=config.inspect("model/cat.model3.json",doc,files);
  assert.deepEqual(result.missing,["model/idle.motion3.json"]);assert.deepEqual(result.groups,["idle"]);
  doc.FileReferences.Moc="../cat.moc3";assert.throws(()=>config.inspect("model/cat.model3.json",doc,files));
});
test("real BongoCat model dependencies resolve with preserved folder paths",()=>{
  const fs=require("node:fs"),path=require("node:path");
  const root=path.resolve(__dirname,"../../..","Examples/Skins/BongoCatLive2DTouch");
  const model="model/cat.model3.json";const doc=JSON.parse(fs.readFileSync(path.join(root,model),"utf8"));
  const files={has:p=>fs.existsSync(path.join(root,p))};
  assert.equal(config.inspect(model,doc,files).missing.length,0);
});
test("retains background coordinates before an image has been selected",()=>{
  const model=config.normalize({background:{image:"",x:-.53,y:-.375,width:1.2,height:.72}});
  assert.equal(model.background.x,-.53);
  model.background.image="ui/tablet.png";
  assert.equal(config.normalize(model).background.width,1.2);
});
test("rejects wrong model and expression formats, deduplicates dependencies",()=>{
  const doc={FileReferences:{Moc:"cat.moc3",Textures:["a.png","a.png"]}};
  const result=config.inspect("model/cat.model3.json",doc,new Map());
  assert.equal(result.targets.length,2);
  assert.throws(()=>config.inspect("model/cat.json",doc,new Map()));
  doc.FileReferences.Expressions=[{File:"a.png"}];
  assert.throws(()=>config.inspect("model/cat.model3.json",doc,new Map()));
});
test("checks ordinary layout, effects and touch image dependencies",()=>{
  assert.deepEqual(config.imageReferences({elements:[{image:"ui/a.png"}],customDisplays:[{image:"ui/a.png"}],
    touchOverlay:{tablet:{image:"ui/b.png"}},touchZones:{idle:"ui/c.png"}}),["ui/a.png","ui/b.png","ui/c.png"]);
  assert.throws(()=>config.imageReferences({elements:[{image:"../a.png"}]}));
});
