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
  for(const override of [{threshold:NaN},{width:Infinity},{x:.9},{trigger:"exec"},{action:"script"},{value:"miss"}])assert.throws(()=>rules.validate([{...base(),...override}],true));
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
test("scene PNG dependencies include lane images and reject unsafe paths",()=>{
  assert.deepEqual(config.imageReferences({scene:{background:{image:"ui/bg.png"},track:{image:"ui/track.png",laneImage:"ui/line.png"}}}),["ui/bg.png","ui/track.png","ui/line.png"]);
  assert.throws(()=>config.imageReferences({scene:{track:{laneImage:"../outside.png"}}}));
});
