const test=require("node:test");
const assert=require("node:assert/strict");
const rules=require("../rule-config.js");
const config=require("../live2d-config.js");
const base=()=>({id:"combo100",trigger:"combo",condition:"equal",threshold:100,value:"",image:"ui/a.png",x:.4,y:.4,width:.2,height:.2});
test("blocks validate bounded numeric, event and motion rules",()=>{
  rules.validate([base()],true);
  rules.validate([{...base(),trigger:"judgement",condition:"active",value:"miss"}],true);
  rules.validate([{...base(),action:"live2dMotion",image:"",motion:"celebrate"}],true);
  rules.validate([{...base(),enabled:false,image:""}],true);
  for(const override of [{threshold:NaN},{width:Infinity},{x:.9},{trigger:"exec"},{action:"script"},{value:"miss"},{enabled:"false"},{animateRotation:1}])assert.throws(()=>rules.validate([{...base(),...override}],true));
  assert.throws(()=>rules.validate([base(),base()]));
  assert.throws(()=>rules.validate([{...base(),image:""}],true));
  assert.throws(()=>rules.validate([{...base(),action:"live2dMotion",motion:"../run"}],true));
  assert.throws(()=>rules.validate(Array.from({length:49},(_,i)=>({...base(),id:"rule_"+i}))));
});
test("preview follows threshold direction and exact event filters",()=>{
  assert(rules.matches(base(),{trigger:"combo",current:100,previous:99}));
  assert(!rules.matches(base(),{trigger:"combo",current:100,previous:100}));
  const budget={...base(),trigger:"displayed_metric",mode:"sss+",condition:"less",threshold:0};
  assert(rules.matches(budget,{trigger:"displayed_metric",current:-1,previous:0,mode:"sss+"}));
  assert(!rules.matches(budget,{trigger:"displayed_metric",current:-1,previous:0,mode:"sss"}));
  assert(!rules.matches({...base(),enabled:false},{trigger:"combo",current:100,previous:99}));
  assert(!rules.matches({...base(),trigger:"judgement",value:"attack"},{trigger:"judgement",value:"miss"}));
});
test("fractional equality thresholds follow Unity's ties-to-even rounding",()=>{
  for(const [threshold,current] of [[100.5,100],[101.5,102],[-.5,0],[-1.5,-2]])assert(rules.matches({...base(),threshold},{trigger:"combo",current,previous:99}));
  assert(!rules.matches({...base(),threshold:100.5},{trigger:"combo",current:101,previous:99}));
});
test("scene PNG dependencies include lane images and reject unsafe paths",()=>{
  assert.deepEqual(config.imageReferences({scene:{background:{image:"ui/bg.png"},track:{image:"ui/track.png",laneImage:"ui/line.png"}}}),["ui/bg.png","ui/track.png","ui/line.png"]);
  assert.throws(()=>config.imageReferences({scene:{track:{laneImage:"../outside.png"}}}));
});
test("animation keyframes preserve runtime fades, scale and rotation direction",()=>{
  const timing=rules.animation({duration:2,fadeIn:.5,fadeOut:.5,opacity:.6,scaleFrom:.5,scaleTo:1.5,rotation:30,rotationTo:90,animateRotation:true});
  assert.equal(timing.duration,2000);
  assert.deepEqual(timing.frames.map(frame=>frame.offset),[0,.25,.75,1]);
  assert.deepEqual(timing.frames.map(frame=>frame.opacity),[0,.6,.6,0]);
  assert.equal(timing.frames[0].transform,"rotate(-30deg) scale(0.5)");
  assert.equal(timing.frames[1].transform,"rotate(-45deg) scale(0.75)");
  assert.equal(timing.frames[3].transform,"rotate(-90deg) scale(1.5)");
  const fixed=rules.animation({rotation:30,rotationTo:90,animateRotation:false});
  assert(fixed.frames.every(frame=>frame.transform.startsWith("rotate(-30deg)")));
});
test("overlapping fades are clamped and zero duration cannot leak invalid frames",()=>{
  for(const rule of [{duration:0,fadeIn:30,fadeOut:30},{duration:NaN,scaleFrom:Infinity},{duration:2,fadeIn:1.8,fadeOut:1.8}]){
    const timing=rules.animation(rule);assert(timing.duration>=50&&timing.duration<=30000);
    assert.equal(timing.frames[0].offset,0);assert.equal(timing.frames.at(-1).offset,1);
    for(const frame of timing.frames){assert(frame.opacity>=0&&frame.opacity<=1);assert(!/NaN|Infinity/.test(frame.transform));}
  }
});
