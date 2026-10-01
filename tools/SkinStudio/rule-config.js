/* Shared block vocabulary and strict export checks; packages never supply code. */
(function(root,factory){if(typeof module==="object"&&module.exports)module.exports=factory();else root.SkinStudioRules=factory();})(globalThis,function(){
  const triggers=["combo","score","rank","judgement","air","touch","gameplay","displayed_metric"];
  const modes=["any","combo","remaining_combo","score","sss+","sss","ss+","ss","s+","s","lost_score","best_score"];
  const values={rank:["sss+","sss","ss+","ss","s+","s","aaa","aa","a","bbb","bb","b","c","d"],judgement:["justice_critical","justice","attack","miss"],air:["active","rising","falling","enter","exit","sensor1","sensor2","sensor3","sensor4","sensor5","sensor6"],touch:["any","lefttop","leftbottom","righttop","rightbottom"],gameplay:["start","stop"]};
  function validate(rules,complete=false){
    if(!Array.isArray(rules)||rules.length>48)throw Error("规则最多 48 项");const ids=new Set();
    for(const r of rules){
      if(!r||typeof r!=="object"||typeof r.id!=="string"||!/^\w[\w-]{0,63}$/.test(r.id)||ids.has(r.id))throw Error("规则 ID 无效或重复");ids.add(r.id);
      if(!triggers.includes(r.trigger))throw Error("未知规则来源");
      for(const key of ["enabled","animateRotation"])if(r[key]!=null&&typeof r[key]!=="boolean")throw Error("规则开关需要布尔值");
      if(!["rising","greater_or_equal","equal","less","less_or_equal","active","exit"].includes(r.condition))throw Error("未知规则条件");
      if(values[r.trigger]&&!values[r.trigger].includes(r.value))throw Error("规则目标无效");
      if(!values[r.trigger]&&r.value)throw Error("数值规则不接受文本目标");
      if(!modes.includes(r.mode||"any"))throw Error("显示模式无效");
      const bounds={threshold:[-1010000,1010000],x:[0,.99],y:[0,.99],width:[.01,1],height:[.01,1],duration:[.05,30],opacity:[0,1],fadeIn:[0,30],fadeOut:[0,30],scaleFrom:[.01,4],scaleTo:[.01,4],rotation:[-360,360],rotationTo:[-360,360]};
      for(const [key,[min,max]] of Object.entries(bounds))if(r[key]!=null&&(!Number.isFinite(r[key])||r[key]<min||r[key]>max))throw Error("规则数值越界："+key);
      if(r.x+r.width>1.001||r.y+r.height>1.001)throw Error("规则图片超出画布");
      if(r.action&&!["image","live2dMotion"].includes(r.action))throw Error("未知规则动作");
      if(r.action==="live2dMotion"){if(complete&&r.enabled!==false&&!/^[\p{L}\p{N}_-]{1,64}$/u.test(r.motion||""))throw Error("请选择 Live2D 动作组");}
      else if(complete&&r.enabled!==false&&!r.image)throw Error("规则未选择图片");
    }
  }
  // Preview a single transition, using the same boundary direction as runtime.
  // Mathf.RoundToInt resolves exact halfway thresholds to the even integer.
  function roundEven(value){const low=Math.floor(value);return value-low===.5?low+(Math.abs(low)%2):Math.round(value);}
  function crossed(current,previous,r){switch(r.condition){case "equal":return current===roundEven(r.threshold)&&current!==previous;case "less":return current<r.threshold&&previous>=r.threshold;case "less_or_equal":return current<=r.threshold&&previous>r.threshold;default:return current>=r.threshold&&previous<r.threshold;}}
  function matches(r,event){
    if(r.enabled===false||r.trigger!==event.trigger)return false;
    if(r.trigger==="displayed_metric"&&(r.mode||"any")!=="any"&&r.mode!==event.mode)return false;
    if(["combo","score","displayed_metric"].includes(r.trigger))return crossed(event.current,event.previous,r);
    if(r.value!==event.value&&!(r.trigger==="touch"&&r.value==="any"))return false;
    return r.condition==="exit"?event.exit===true:event.exit!==true;
  }
  // Match the runtime's clamped fade intervals and linear transforms. CSS
  // rotation is clockwise, unlike Unity's positive Z rotation.
  function animation(r){
    const finite=(value,fallback)=>Number.isFinite(value)?value:fallback;
    const clamp=(value,min,max)=>Math.min(max,Math.max(min,value));
    const duration=clamp(finite(r.duration,1.2),.05,30);
    const fadeIn=clamp(finite(r.fadeIn,0),0,duration);
    const fadeOut=clamp(finite(r.fadeOut,0),0,duration-fadeIn);
    const opacity=clamp(finite(r.opacity,1),0,1);
    const from=clamp(finite(r.scaleFrom,1),.01,4),to=clamp(finite(r.scaleTo,1),.01,4);
    const start=clamp(finite(r.rotation,0),-360,360);
    const end=r.animateRotation?clamp(finite(r.rotationTo,0),-360,360):start;
    const times=[...new Set([0,fadeIn,duration-fadeOut,duration])].sort((a,b)=>a-b);
    const frames=times.map(time=>{
      const progress=time/duration;
      const alpha=fadeIn>0&&time<fadeIn?time/fadeIn:fadeOut>0&&duration-time<fadeOut?(duration-time)/fadeOut:1;
      return {offset:progress,opacity:alpha*opacity,transform:`rotate(${-start-(end-start)*progress}deg) scale(${from+(to-from)*progress})`};
    });
    return {duration:duration*1000,frames};
  }
  return {triggers,modes,values,validate,crossed,matches,animation};
});
