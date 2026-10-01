/* Shared validation for the editor, exports and regression tests. No Cubism
 * binary is executed here: native model evaluation remains owned by the game. */
(function(root, factory) {
  if (typeof module === "object" && module.exports) module.exports = factory();
  else root.SkinStudioLive2D = factory();
})(typeof globalThis !== "undefined" ? globalThis : this, function() {
  "use strict";
  const parameters = ["leftXParameter", "leftYParameter", "leftVisibleParameter", "leftDownParameter",
    "rightXParameter", "rightYParameter", "rightVisibleParameter", "rightDownParameter"];
  const motions = ["idleMotion", "leftPressMotion", "rightPressMotion"];
  const axes = ["leftXRange", "leftYRange", "rightXRange", "rightYRange"];
  const finite = (value, low, high, fallback) => Number.isFinite(Number(value)) && value !== "" && value != null
    ? Math.min(high, Math.max(low, Number(value))) : fallback;
  function path(value) {
    const p = typeof value === "string" ? value.replace(/\\/g, "/") : "";
    if (!p || p.length > 256 || p.startsWith("/") || /[:\x00-\x1f]/.test(p) || p.split("/").some(s => !s || s === "." || s === "..")) return "";
    return p;
  }
  function allowedAsset(value) {
    const p = path(value);
    return !!p && /(?:\.png|\.moc3|\.(?:model3|motion3|exp3|cdi3|physics3|pose3)\.json|(?:^|\/)(?:LICENSE[^/]*|COPYING[^/]*)(?:\.txt)?)$/i.test(p);
  }
  const name = value => typeof value === "string" && /^[\p{L}\p{N}_-]{1,64}$/u.test(value) ? value : "";
  function defaults() {
    return {enabled:false, model:"", x:.015, y:.34, width:.20, height:.36,
      leftXParameter:"ParamMouseX", leftYParameter:"ParamMouseY", leftVisibleParameter:"", leftDownParameter:"ParamMouseLeftDown",
      rightXParameter:"ParamMouseX", rightYParameter:"ParamMouseY", rightVisibleParameter:"", rightDownParameter:"ParamMouseRightDown",
      leftXRange:{min:0,max:1}, leftYRange:{min:0,max:1}, rightXRange:{min:0,max:1}, rightYRange:{min:0,max:1},
      idleMotion:"", leftPressMotion:"", rightPressMotion:"", hiddenParts:[]};
  }
  function normalize(input) {
    const d = defaults(), v = input && typeof input === "object" ? input : {};
    d.enabled = v.enabled === true;
    const model = path(v.model); d.model = /\.model3\.json$/i.test(model) ? model : "";
    d.x = finite(v.x,0,.99,d.x); d.y = finite(v.y,0,.99,d.y);
    d.width = finite(v.width,.01,1-d.x,Math.min(d.width,1-d.x)); d.height = finite(v.height,.01,1-d.y,Math.min(d.height,1-d.y));
    for (const k of [...parameters,...motions]) if (Object.hasOwn(v,k)) d[k] = name(v[k]);
    for (const k of axes) d[k] = {min:finite(v[k]?.min,0,1,0), max:finite(v[k]?.max,0,1,1)};
    d.hiddenParts = [...new Set((Array.isArray(v.hiddenParts)?v.hiddenParts:[]).map(name).filter(Boolean))].slice(0,32);
    const b = v.background, image = path(b?.image);
    if (image && /\.png$/i.test(image)) d.background = {image,x:finite(b.x,-5,5,-1),y:finite(b.y,-5,5,-1),
      width:finite(b.width,.01,10,2),height:finite(b.height,.01,10,2)};
    return d;
  }
  function bongo(input) {
    return normalize({...input, leftXParameter:"CatParamStickRX", leftYParameter:"CatParamStickRY",
      leftVisibleParameter:"CatParamStickShowRightHand", leftDownParameter:"CatParamRightHandDown",
      rightXParameter:"CatParamStickLX", rightYParameter:"CatParamStickLY",
      rightVisibleParameter:"CatParamStickShowLeftHand", rightDownParameter:"CatParamLeftHandDown"});
  }
  // Resolve model-local references without allowing a path to escape the skin.
  function resolve(model, reference) {
    const r = path(reference); if (!r) throw Error("Invalid model reference: " + reference);
    const prefix = model.slice(0,model.lastIndexOf("/")+1);
    const target = path(prefix+r); if (!target) throw Error("Invalid resource path"); return target;
  }
  function inspect(model, document, files) {
    const refs = document?.FileReferences;
    if (!refs || typeof refs.Moc !== "string" || !Array.isArray(refs.Textures) || !refs.Textures.length) throw Error("Model requires Moc and Textures");
    if (refs.Textures.length > 16) throw Error("More than 16 textures");
    const required = [refs.Moc,...refs.Textures];
    for (const k of ["Physics","Pose","DisplayInfo"]) if (refs[k]) required.push(refs[k]);
    for (const e of refs.Expressions || []) required.push(e.File);
    const groups = Object.keys(refs.Motions || {});
    for (const list of Object.values(refs.Motions || {})) {
      if (!Array.isArray(list)) throw Error("Invalid motion group");
      for (const motion of list) required.push(motion.File);
    }
    const targets = required.map(r => resolve(model,r));
    if (!/\.moc3$/i.test(targets[0]) || targets.slice(1,1+refs.Textures.length).some(p => !/\.png$/i.test(p))) throw Error("Moc/texture format is invalid");
    if (targets.some(p => !allowedAsset(p))) throw Error("Unsupported resource format");
    return {missing:targets.filter(p=>!files.has(p)),groups:groups.filter(g=>name(g)),targets};
  }
  return {parameters,motions,axes,path,allowedAsset,defaults,normalize,bongo,resolve,inspect};
});
