/** Resolve native track geometry from a logical control size and numeric range. */
export function trackGeometry(width,height,min,max,requestedInterval=1){
if(![width,height,min,max,requestedInterval].every(Number.isFinite)||width<=41||height<55||max<=min||requestedInterval<1)throw Error('Invalid native track geometry');
// cn-717 sub_B62940 and sub_B62D80 use padded frames; cn-279 sub_4B66B0 uses the same drawing origins.
const count=max-min+1,interval=count%requestedInterval===1?requestedInterval:1,step=(width-41)/(max-min);
return {railY:height-35,ticks:Array.from({length:Math.floor(count/interval)+1},(_,i)=>Math.trunc(20+interval*step*i)),knobX:value=>Math.trunc(15+step*(Math.max(min,Math.min(max,value))-min))};
}

/** cn-774 sub_F6FF50 uses compact 9x18 sprites and a width-minus-39 travel. */
export function compactTrackGeometry(width,min,max,requestedInterval=1,textHeight=15){
if(![width,min,max,requestedInterval,textHeight].every(Number.isFinite)||width<=39||max<=min||requestedInterval<1||textHeight<0)throw Error('Invalid compact track geometry');
const count=max-min+1,interval=count%requestedInterval===1?requestedInterval:1,step=(width-39)/(max-min);
return {railY:textHeight+2,ticks:Array.from({length:Math.floor(count/interval)+1},(_,i)=>Math.trunc(20+interval*step*i)),knobX:value=>Math.trunc(15+step*(Math.max(min,Math.min(max,value))-min))};
}
