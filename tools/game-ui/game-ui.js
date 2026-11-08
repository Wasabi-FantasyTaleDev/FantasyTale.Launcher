import {paintText} from './text.js';
import {trackGeometry,compactTrackGeometry} from './native-layout.js';
import {drawExtensionSettings} from './extension-settings.js';

/**
 * Create an isolated renderer; all themes use these same component functions.
 * @param {{theme?:string,resourceRoot?:string,sounds?:ReturnType<typeof import('./ui-sounds.js').createUiSoundPlayer>}} options
 */
export async function createGameUi({theme='v2',resourceRoot='./local/',sounds}={}) {
  if(!['v1','v2','v3'].includes(theme))throw Error(`Unknown UI theme: ${theme}`);
  const base=()=>`${resourceRoot.replace(/\/?$/, '/')}${theme}/`;
  const response=await fetch(base()+'manifest.json');
  if(!response.ok)throw Error(`Unable to load ${theme} UI resources (${response.status}).`);
  const manifest=await response.json();
const number = (a, key, fallback=0) => Number(a[key] || fallback);
function place(el,x,y,w,h){Object.assign(el.style,{left:`${x}px`,top:`${y}px`,width:`${w}px`,height:`${h}px`});return el;}
async function tile(parent,path,rect,x,y,w,h){const index=manifest.sprites[path]?.findIndex(r=>r.every((v,i)=>v===rect[i]));if(index===undefined||index<0)throw Error(`Missing sprite frame: ${path}`);const box=place(document.createElement('span'),x,y,w,h);box.className='tile';const copy=new Image();copy.src=base()+path.replace(/\.smf$/i,'')+'/'+index+'.png';Object.assign(copy.style,{width:'100%',height:'100%',left:'0',top:'0'});box.append(copy);parent.append(box);}
const decodedFrames=new Map();
async function frameImage(path,index){const key=path+':'+index;if(!decodedFrames.has(key)){decodedFrames.set(key,(async()=>{const img=new Image();img.src=base()+path.replace(/\.smf$/i,'')+'/'+index+'.png';await img.decode();return img;})());}return decodedFrames.get(key);}
async function skin(parent,path,w,h){
  const frames=manifest.sprites[path];if(!frames)throw Error(`Missing sprite: ${path}`);
  const canvas=document.createElement('canvas');canvas.className='skin';canvas.width=w;canvas.height=h;canvas.style.width=w+'px';canvas.style.height=h+'px';canvas.ariaHidden='true';
  const ctx=canvas.getContext('2d');ctx.imageSmoothingEnabled=false;
  // Compose shared integer edges before CSS scaling to avoid independently rounded tile boundaries.
  if(frames.length!==9)ctx.drawImage(await frameImage(path,0),0,0,w,h);
  else {const widths=[frames[0][2]-frames[0][0]+1,0,frames[2][2]-frames[2][0]+1],heights=[frames[0][3]-frames[0][1]+1,0,frames[6][3]-frames[6][1]+1];widths[1]=w-widths[0]-widths[2];heights[1]=h-heights[0]-heights[2];if(widths[1]<0||heights[1]<0)throw Error(`Skin destination too small: ${path}`);const images=await Promise.all(frames.map((_,i)=>frameImage(path,i)));let y=0;for(let row=0;row<3;row++){let x=0;for(let col=0;col<3;col++){if(widths[col]&&heights[row])ctx.drawImage(images[row*3+col],x,y,widths[col],heights[row]);x+=widths[col];}y+=heights[row];}}
  parent.prepend(canvas);return canvas;
}
async function button(parent,name,x,y,w,h,text,explicit,attrs={}){
if(theme==='v3'&&(!explicit||explicit.includes('/dialogueNew/'))){
  const el=place(document.createElement('button'),x,y,w,h);el.className='control sprite-button';el.ariaLabel=text;el.dataset.control=attrs.Name||name;parent.append(el);
  const fallback=name==='Ok'?'Images/Ui/dialogueNew/dialogue_type01BTN-Up.smf':manifest.scheme.UiButton.SkinImage_Up;
  const up=explicit||attrs.SpriteSetUp||fallback;let revision=0,selected=false;
  const paths=[up,attrs.SpriteSetDown||up.replace(/(?:-Up|_up)\.smf$/,up.includes('Tab_')?'_down.smf':'-Down.smf'),attrs.SpriteSetHover||up.replace(/(?:-Up|_up)\.smf$/,up.includes('Tab_')?'_hover.smf':'-Hover.smf'),attrs.SpriteSetDisabled||up.replace(/(?:-Up|_up)\.smf$/,up.includes('Tab_')?'_disable.smf':'-Disabled.smf')];
  async function paint(state){
    const current=++revision,layer=document.createElement('span');layer.className='control';place(layer,0,0,w,h);layer.style.pointerEvents='none';
    await skin(layer,paths[el.disabled?3:selected?1:state],w,h);
    const label=place(document.createElement('span'),0,0,w,h);label.className='control';layer.append(label);
    await paintText(label,text,{family:'BF',size:Number(attrs.FontSize)||16,weight:400,align:'center',color:el.disabled?'#e1e9eb':attrs.TextColor?'#'+attrs.TextColor.slice(-6):'#232323',decoration:1,nativeMetrics:true,vertical:'middle',inset:0});
    if(current===revision)el.replaceChildren(layer);
  }
  await paint(0);el.setSelected=async value=>{selected=Boolean(value);el.ariaPressed=String(selected);await paint(0)};
  el.addEventListener('pointerenter',()=>{void paint(2)});el.addEventListener('pointerleave',()=>{void paint(0)});el.addEventListener('pointerdown',()=>{void paint(1)});el.addEventListener('pointerup',()=>{void paint(2)});el.addEventListener('pointercancel',()=>{void paint(0)});
  el.addEventListener('click',()=>parent.dispatchEvent(new CustomEvent('bf-action',{bubbles:true,detail:{name,text}})));return el;
}
const el=place(document.createElement('button'),x,y,w,h);el.className='control sprite-button';el.ariaLabel=text;parent.append(el);const path=explicit || `Images/Ui/DialogBtn${name}.smf`;let state=0;async function paint(i){state=i;el.replaceChildren();if(theme==='v1'&&(!explicit||/DialogButton(?:Ok|Cancel)-Up/.test(explicit))){const suffix=['Up','Down','Hover','Disabled'][i];const skinName=explicit?.match(/DialogButton(Ok|Cancel)-Up/)?.[1]??name;await skin(el,`Images/Ui/DialogButton${skinName}-${suffix}.smf`,w,h);const label=document.createElement('span');label.className='text';label.style.cssText='left:14px;right:0;top:0;bottom:0;width:'+(w-14)+'px;height:'+h+'px';el.append(label);await paintText(label,text,{family:'BFMalgun',size:13,weight:400,width:w-14,height:h,align:'center',color:'white',outline:'#79a4cf',outlineWidth:0.5});}else{const frames=manifest.sprites[path];if(!frames)throw Error(`Missing button: ${path}`);await tile(el,path,frames[Math.min(i,frames.length-1)],0,0,w,h);}}await paint(0);el.setSelected=async selected=>{el.ariaPressed=String(selected);await paint(selected?3:0);};el.addEventListener('pointerenter',()=>paint(el.ariaPressed==='true'?3:2));el.addEventListener('pointerleave',()=>paint(el.ariaPressed==='true'?3:0));el.addEventListener('pointerdown',()=>paint(1));el.addEventListener('pointerup',()=>paint(el.ariaPressed==='true'?3:2));el.addEventListener('click',()=>{parent.dispatchEvent(new CustomEvent('bf-action',{bubbles:true,detail:{name,text,state}}));});return el;}
async function windowShell(width,height,title){const el=place(document.createElement('div'),0,0,width,height);el.className='bf-window';el.dataset.theme=theme;el.dataset.bfOpenSound='popup';await skin(el,theme==='v3'?manifest.scheme.Dialog.SkinImage_Title:'Images/Ui/DialogSkin-Basic.smf',width,height);const caption=document.createElement('span');caption.className='caption';caption.style.width=(width-(theme==='v1'?62:50))+'px';caption.style.height=(theme==='v2'?38:18)+'px';if(theme==='v1'){caption.style.left='37px';caption.style.top='11px';}if(theme==='v2'){caption.style.left='3px';caption.style.top='9px';caption.style.width=width+'px';}if(theme==='v3'){
 const a=manifest.scheme.Dialog;place(caption,Number(a.Title_TextMargin_Left),Number(a.Title_TextMargin_Top),width-Number(a.Title_TextMargin_Left)-Number(a.Title_TextMargin_Right),Number(a.Title_Margin_Top)-Number(a.Title_TextMargin_Top)-Number(a.Title_TextMargin_Bottom));
}el.append(caption);await paintText(caption,title,{size:Number(manifest.scheme.Dialog.Title_FontSize),family:'BF',align:theme==='v1'?'left':'center',color:'#'+manifest.scheme.Dialog.Title_TextColor.slice(-6),decoration:Number(manifest.scheme.Dialog.Title_TextDecoration),outline:'#'+manifest.scheme.Dialog.Title_TextDecoColor.slice(-6),outlineWidth:2,nativeMetrics:theme!=='v1',vertical:theme==='v3'?'middle':'top'});const path=manifest.scheme.Dialog.CloseButton_SkinImage;
const frames=manifest.sprites[path];
const closeWidth=frames[0][2]-frames[0][0]+1;
const closeHeight=frames[0][3]-frames[0][1]+1;
// cn-279 sub_4A7170 and cn-717 sub_B469E0 position a child of the content origin.
const right=Number(manifest.scheme.Dialog.Title_Margin_Right)+(theme==='v1'?5:-3);
// cn-774 UiScheme.ini supplies edge margins, not the sprite center.
const close=place(document.createElement('button'),theme==='v3'?width-Number(manifest.scheme.Dialog.CloseButton_MarginRight_X)-closeWidth:width-right-closeWidth,theme==='v3'?Number(manifest.scheme.Dialog.CloseButton_MarginRight_Y):theme==='v1'?11:8,closeWidth,closeHeight);
close.className='bf-close';close.ariaLabel='关闭';
// cn-279 sub_4A7C60 overrides this button's default press event with cancel.
close.dataset.bfPressSound='cancel';
await tile(close,path,frames[0],0,0,closeWidth,closeHeight);
const sprite=close.querySelector('img');
const paintClose=index=>{sprite.src=base()+path.replace(/\.smf$/i,'')+'/'+index+'.png';};
close.addEventListener('pointerenter',()=>paintClose(close.disabled?3:2));
close.addEventListener('pointerleave',()=>paintClose(close.disabled?3:0));
close.addEventListener('pointerdown',()=>{if(!close.disabled)paintClose(1);});
close.addEventListener('pointerup',()=>paintClose(close.disabled?3:2));
close.addEventListener('pointercancel',()=>paintClose(close.disabled?3:0));
close.onclick=()=>el.dispatchEvent(new CustomEvent('bf-action',{bubbles:true,detail:{name:'close'}}));el.append(close);return el;}
function messageTextSize(text,size,family){
  const canvas=document.createElement('canvas'),ctx=canvas.getContext('2d');
  if(!ctx)throw Error('Unable to measure message text.');
  ctx.font=`400 ${size}px ${family}`;
  const lines=[];for(const paragraph of text.split('\n')){
    let line='';for(const character of paragraph)line+=character;lines.push(line);
  }
  return {width:Math.ceil(Math.max(...lines.map(line=>ctx.measureText(line||' ').width),0)),height:Math.ceil(lines.length*(size+1))};
}
function messageGeometry({contentWidth,contentHeight,textWidth,textHeight,buttonWidth,buttonHeight,buttonCount,buttonSpan=buttonWidth}){
  // cn-717 sub_921AB0/sub_B409D0: keep a 200-unit minimum and resize the
  // message body before placing the frame, icon, and buttons.
  const textUnits=Math.max(200,textWidth),bodyHeight=Math.max(70,textHeight+16,58);
  const width=Math.max(contentWidth,textUnits+104,buttonSpan+20),height=Math.max(contentHeight,bodyHeight+9+buttonHeight);
  return {width,height,bodyHeight,buttonY:bodyHeight+9,buttonX:buttonCount===1?Math.floor((width-buttonWidth)/2):undefined};
}
async function renderMessageBox({title,text,iconKind='notice',buttons,body,contentWidth=350,contentHeight,contentX=10,contentY=38,outerWidth,outerHeight,buttonY:customButtonY}){
  if(theme==='v3')return renderV3MessageBox(arguments[0]);
  const modern=theme==='v2',baseWidth=contentWidth,baseHeight=contentHeight??(modern?104:105),customBody=typeof body==='function';
  const iconNames={notice:'Notice',warning:'Warning',question:'Question',error:'Error'};
  // Warning reserves a 100-unit slot in the .ui layout, but its extracted PNG
  // is 63x58. Keep the slot for layout calculations and draw the bitmap at
  // natural size so the triangle is never stretched.
  const iconFile=`Images/Ui/MsgBoxIcon${iconNames[iconKind]||'Notice'}.png`,iconWidth=63;
  const gap=modern?5:8,normalized=buttons.map(spec=>({...spec,width:spec.width??(modern?64:68),height:spec.height??(modern?26:25)}));
  if(!normalized.length&&!customBody)throw Error('Message boxes require at least one button.');
  const buttonSpan=normalized.reduce((sum,spec)=>sum+spec.width,0)+gap*(normalized.length-1);
  await document.fonts.load('400 13px BFMalgun');
  const measured=customBody?{width:0,height:0}:messageTextSize(text,13,'BFMalgun');
  const geometry=customBody
    ? {width:contentWidth,height:baseHeight,bodyHeight:baseHeight,buttonY:customButtonY??(baseHeight+9)}
    : messageGeometry({contentWidth:baseWidth,contentHeight:baseHeight,textWidth:measured.width,textHeight:measured.height,buttonWidth:normalized[0].width,buttonHeight:normalized[0].height,buttonCount:normalized.length,buttonSpan});
  // Custom bodies reserve their own action row; the shell must include it.
  const actionBottom=geometry.buttonY+Math.max(0,...normalized.map(spec=>spec.height));
  geometry.height=Math.max(geometry.height,actionBottom);
  const right=Number(manifest.scheme.Dialog.Title_Margin_Right),bottom=Number(manifest.scheme.Dialog.Title_Margin_Bottom);
  const el=await windowShell(Math.max(outerWidth??0,contentX+geometry.width+right),Math.max(outerHeight??0,contentY+geometry.height+bottom),title);
  const content=place(document.createElement('div'),contentX,contentY,geometry.width,geometry.height);content.className='bf-form';el.append(content);
  if(customBody)await body(content,geometry);
  else {
    const panelWidth=modern?geometry.width-1:geometry.width;const panel=place(document.createElement('div'),modern?1:0,2,panelWidth,geometry.bodyHeight);panel.className='control';await skin(panel,'Images/Ui/FrameBox.smf',panelWidth,geometry.bodyHeight);content.append(panel);
    const icon=new Image();icon.src=base()+iconFile;icon.className='control';
    // The .ui width is a layout slot, not an instruction to scale the PNG.
    // Read the decoded bitmap for every icon so a future resource pack with
    // a different natural size cannot silently stretch it.
    await icon.decode().catch(()=>{});
    const naturalWidth=icon.naturalWidth||iconWidth,naturalHeight=icon.naturalHeight||58;
    place(icon,11,Math.round(2+(geometry.bodyHeight-naturalHeight)/2),naturalWidth,naturalHeight);content.append(icon);
    const textHeight=Math.max(40,measured.height),copy=place(document.createElement('div'),90,Math.round(2+(geometry.bodyHeight-textHeight)/2),geometry.width-104,textHeight);copy.className='message-copy';content.append(copy);await paintText(copy,text,{size:13,weight:400,family:'BFMalgun',align:'center',color:'#444',height:textHeight,width:geometry.width-104});
  }
  // cn-279 sub_8A27D0 anchors two buttons to the content right edge;
  // the scheme's outer right margin remains outside this coordinate space.
  let cursor=!modern&&normalized.length>1?geometry.width-buttonSpan:Math.floor((geometry.width-buttonSpan)/2);
  for(const spec of normalized){
    const delta=geometry.width-baseWidth;
    const x=spec.x===undefined?cursor:spec.x+(modern?delta/2:delta);
    const sprite=spec.sprite||(modern?`Images/Ui/DialogBtn${spec.name}.smf`:`Images/Ui/DialogButton${spec.name}-Up.smf`);
    await button(content,spec.name,x,geometry.buttonY,spec.width,spec.height,spec.label,sprite);
    cursor+=spec.width+gap;
  }
  return el;
}
async function dialog(confirmOrOptions,iconKind=typeof confirmOrOptions==='boolean'?(confirmOrOptions?'question':'notice'):'notice'){
  const modern=theme!=='v1',confirm=typeof confirmOrOptions==='boolean'?confirmOrOptions:Boolean(confirmOrOptions?.confirm);
  const options=typeof confirmOrOptions==='object'&&confirmOrOptions?confirmOrOptions:{};
  const defaultButtons=theme==='v3'?(confirm?[{name:'Ok',label:'确认'},{name:'Cancel',label:'取消'}]:[{name:'Ok',label:'确认'}]):confirm?[{name:'Ok',label:'确认',x:modern?113:undefined,sprite:modern?'Images/Ui/DialogBtnOk.smf':'Images/Ui/DialogButtonOk-Up.smf'},{name:'Cancel',label:'取消',x:modern?182:undefined,sprite:modern?'Images/Ui/DialogBtnCancel.smf':'Images/Ui/DialogButtonCancel-Up.smf'}]:[{name:'Ok',label:'确认'}];
  const element=await renderMessageBox({
    title:options.title||(confirm?'确认':'公告'),
    text:options.text||(confirm?(modern?'是否中断新手教程?':'是否停止观看教学视频?'):(modern?'自动开始新手教程。\n完成新手教程可以获得丰富奖励，按ESC键可以退出。\n根据电脑配置不同可能会延长加载时间。':'与服务器连接中断。')),
    iconKind:options.icon||iconKind,
    buttons:options.buttons??defaultButtons,
    body:options.body,
    contentWidth:options.contentWidth,
    contentHeight:options.contentHeight,
    contentX:options.contentX,
    contentY:options.contentY,
    outerWidth:options.outerWidth,
    outerHeight:options.outerHeight,
    buttonY:options.buttonY
  });
  // cn-279 sub_4A78A0 selects popup/fail independently of the icon.
  if(options.openSound===false)delete element.dataset.bfOpenSound;
  else element.dataset.bfOpenSound=options.openSound??'popup';
  return element;
}
const translations={'gbGraphicOption':'画面设置','gbSoundOption':'声音设置','lblBgm':'背景音','lblEffect':'效果音','lblMSG1':'与服务器连接中断。','lblText':'与服务器连接中断。','lblResolution':'1366 x 768'};
async function renderNode(node,parent,overrides={}){if(theme==='v3')return renderNodeV3(node,parent,overrides);const a={...node.attrs,...overrides[node.attrs.Name]},x=number(a,'PosX'),y=number(a,'PosY'),w=number(a,'Width'),h=number(a,'Height');let el=document.createElement('div');el.className='control';place(el,x,y,w,h);parent.append(el);const path=a.SkinSpriteSet||a.SpriteSetBackground;const text=overrides[a.Name]?.Text||translations[a.Name]||a.Text||'';
switch(node.type){case 'UiPanel':case 'UiForm':if(path)await skin(el,path,w,h);else if(a.SkinImage){const img=document.createElement('img');img.src=base()+a.SkinImage;img.style.cssText='width:100%;height:100%';el.append(img);}break;
case 'UiGroupBox':if(path)await skin(el,path,w,h);{const label=place(document.createElement('span'),26,4,w-26,22);label.textContent=text;label.style.color='white';label.className='control bf-label';el.append(label);await paintText(label,text,{color:'white',size:Number(a.FontSize)||Number(manifest.scheme.GroupBox.FontSize),family:'BF',nativeMetrics:true,vertical:'top',inset:0});}break;
case 'UiLabel':el.classList.add('bf-label');el.textContent=text;el.style.fontSize=`${number(a,'FontSize',13)}px`;el.style.fontFamily=['Default','DefaultBold','NexonBTBold'].includes(a.FontName)?'BF':'BFMalgun';if(a.TextColor)el.style.color='#'+a.TextColor.slice(-6);if(number(a,'TextFormat')===5)el.style.justifyContent='center';await paintText(el,text,{size:Number(a.FontSize)||13,color:a.TextColor?'#'+a.TextColor.slice(-6):'#5b5b5b',family:el.style.fontFamily||'BFMalgun',align:number(a,'TextFormat')===5?'center':'left',outline:overrides[a.Name]?.OutlineColor,nativeMetrics:true,vertical:(number(a,'TextFormat')&4)?'middle':'top',inset:0});break;
case 'UiButton':el.remove();await button(parent,/cancel/i.test(a.Name)?'Cancel':'Ok',x,y,w,h,text||(/cancel/i.test(a.Name)?'取消':'确认'),a.SpriteSet||a.SpriteSetUp);return;
case 'UiCheckBox':{el.classList.add('bf-check');const input=document.createElement('input');input.type='checkbox';input.checked=a.Check==='1';input.ariaLabel=a.Name||text;const draw=async()=>{el.querySelectorAll('.tile').forEach(t=>t.remove());const p=a.SpriteSet||'Images/Ui/UiCheckBoxSkin.smf';await tile(el,p,manifest.sprites[p][input.checked?1:0],0,0,21,18);};el.setValue=value=>{input.checked=Boolean(value);draw();};el.append(input);input.onchange=()=>{draw();parent.dispatchEvent(new CustomEvent('bf-change',{bubbles:true,detail:{name:a.Name,value:input.checked}}));};await draw();break;}
case 'UiTrackBar':{el.classList.add('bf-track');const input=document.createElement('input');input.type='range';input.min=a.Min??0;input.max=a.Max??100;input.value=a.Position??75;input.ariaLabel=a.Name;const path='Images/Ui/TrackBar.smf',frames=manifest.sprites[path],railY=h-35;
await tile(el,path,frames[0],15,railY,7,31);await tile(el,path,frames[1],22,railY,w-44,31);await tile(el,path,frames[2],w-22,railY,7,31);
const geometry=trackGeometry(w,h,Number(input.min),Number(input.max),Number(a.Interval)||1);
// cn-717 sub_B62940 and sub_B62D80: tick origin 20, knob origin 15, full padded sprite frames.
for(const tickX of geometry.ticks)await tile(el,path,frames[7],tickX,railY,1,31);
const knob=place(document.createElement('span'),geometry.knobX(Number(input.value)),railY,12,31);knob.className='control';await tile(knob,path,frames[3],0,0,12,31);el.append(knob,input);
if(a.MinText)label(el,a.MinText==='작게'?'缩小':a.MinText,0,h-50,50,15,'#808080',{size:Number(a.FontSize)||Number(manifest.scheme.TrackBar.FontSize),family:'BF',align:'center'});if(a.MaxText)label(el,a.MaxText==='크게'?'放大':a.MaxText,w-50,h-52,50,15,'#808080',{size:Number(a.FontSize)||Number(manifest.scheme.TrackBar.FontSize),family:'BF',align:'center'});
el.setValue=value=>{input.value=String(value);knob.style.left=geometry.knobX(Number(input.value))+'px';};input.oninput=()=>{knob.style.left=geometry.knobX(Number(input.value))+'px';parent.dispatchEvent(new CustomEvent('bf-change',{bubbles:true,detail:{name:a.Name,value:Number(input.value)}}));};break;}
case 'UiEdit':case 'UiEditBox':{const input=document.createElement('input');input.className='control bf-edit';place(input,x,y,w,h);input.value=text;input.ariaLabel=a.Name;el.replaceWith(input);el=input;break;}
default:throw Error(`Unsupported control: ${node.type} (${a.Name||'unnamed'})`);}
for(const child of node.children)await renderNode(child,el,overrides);return el;}

/** Read cn-774 controls without applying the older bitmap-label conventions. */
async function renderNodeV3(node,parent,overrides={}){
  const a={...node.attrs,...overrides[node.attrs.Name]},x=number(a,'PosX'),y=number(a,'PosY'),w=number(a,'Width'),h=number(a,'Height');
  if(a.Visible==='False'||a.Visible==='0'||node.type==='UiMarker')return;
  const el=place(document.createElement('div'),x,y,w,h);el.className='control';el.dataset.control=a.Name||'';
  parent.append(el);
  const text=a.Text||'',path=a.SkinSpriteSet||a.SpriteSetBackground;
  const font={family:a.FontName==='Malgun'?'BFMalgun':'BF',size:Number(a.FontSize)||Number(manifest.scheme.UiLabel.FontSize),weight:400,color:a.TextColor?'#'+a.TextColor.slice(-6):'#bbdde0',decoration:Number(a.TextDecoration)||1,nativeMetrics:true,inset:0};
  switch(node.type){
    case 'UiPanel':case 'UiForm':case 'UiGroupBox':
      if(path)await skin(el,path,w,h);
      else if(a.SkinImage){const image=new Image();image.src=base()+a.SkinImage;await image.decode();place(image,0,0,image.naturalWidth,image.naturalHeight);el.append(image);}
      if(node.type==='UiGroupBox'&&text)label(el,text,9,5,w-18,20,'#29d4ff',{size:14,family:'BF'});
      break;
    case 'UiLabel':{
      const format=number(a,'TextFormat');el.classList.add('bf-label');
      await paintText(el,text,{...font,align:format&1?'center':format&2?'right':'left',vertical:format&4?'middle':format&8?'bottom':'top',wrap:Boolean(format&16)});break;
    }
    case 'UiButton':el.remove();return button(parent,/cancel/i.test(a.Name)?'Cancel':/okay|^btnok$/i.test(a.Name)?'Ok':a.Name,x,y,w,h,text,a.SpriteSet||a.SpriteSetUp,{...a,FontSize:Number(a.FontSize)?a.FontSize:manifest.scheme.UiButton.FontSize});
    case 'UiCheckBox':{
      el.classList.add('bf-check');const input=document.createElement('input');input.type='checkbox';input.checked=a.Check==='1';input.ariaLabel=text||a.Name;el.append(input);
      const p=a.SpriteSet||manifest.scheme.UiCheckBox.SkinImage,frames=manifest.sprites[p];
      const art=place(document.createElement('span'),0,0,21,18);art.className='control';el.append(art);
      const draw=async()=>{art.replaceChildren();await tile(art,p,frames[input.checked?1:0],0,0,21,18)};
      el.setValue=value=>{input.checked=Boolean(value);void draw()};
      input.onchange=()=>{void draw();parent.dispatchEvent(new CustomEvent('bf-change',{bubbles:true,detail:{name:a.Name,value:input.checked}}))};
      await draw();if(text){const copy=place(document.createElement('span'),30,0,Math.max(1,w-30),h);copy.className='control';el.append(copy);await paintText(copy,text,{...font,vertical:'middle'});}break;
    }
    case 'UiTrackBar':{
      el.classList.add('bf-track');const input=document.createElement('input');input.type='range';input.min=a.Min??0;input.max=a.Max??100;input.value=a.Position??50;input.ariaLabel=a.Name;el.append(input);
      const hasText=a.HasText!=='False',geometry=compactTrackGeometry(w,Number(input.min),Number(input.max),Number(a.Interval)||1,hasText?15:0);
      const p=manifest.scheme.TrackBar.SkinImage,frames=manifest.sprites[p],railY=geometry.railY;
      await tile(el,p,frames[0],15,railY,4,18);await tile(el,p,frames[1],19,railY,w-38,18);await tile(el,p,frames[2],w-19,railY,4,18);
      if(a.HasPoint!=='False')for(const tickX of geometry.ticks)await tile(el,p,frames[7],tickX,railY,1,18);
      const knob=place(document.createElement('span'),geometry.knobX(Number(input.value)),railY,9,18);knob.className='control';await tile(knob,p,frames[3],0,0,9,18);el.append(knob);
      const update=()=>{knob.style.left=geometry.knobX(Number(input.value))+'px'};
      el.setValue=value=>{input.value=String(value);update()};input.oninput=()=>{update();parent.dispatchEvent(new CustomEvent('bf-change',{bubbles:true,detail:{name:a.Name,value:Number(input.value)}}))};
      if(hasText){const translate=value=>manifest.translations[value]||({'작게':'小','크게':'大'}[value])||value;
        label(el,translate(a.MinText||''),0,0,50,15,font.color,{size:font.size,family:font.family,align:'center'});label(el,translate(a.MaxText||''),w-50,0,50,15,font.color,{size:font.size,family:font.family,align:'center'});
      }break;
    }
    case 'UiEdit':case 'UiEditBox':{
      const input=document.createElement('input');input.className='control bf-edit';input.ariaLabel=a.Name;input.value=text;place(input,x,y,w,h);el.replaceWith(input);return input;
    }
    default:throw Error(`Unsupported cn-774 control: ${node.type} (${a.Name||'unnamed'})`);
  }
  for(const child of node.children)await renderNode(child,el,overrides);return el;
}

/** cn-774 sub_77A980 retains the body minimum, resizes its height and centers actions. */
async function renderV3MessageBox(options){
  const {title,text='',iconKind='notice',buttons=[],body}=options,customBody=typeof body==='function';
  const form=manifest.forms['DlgMessageBox.ui'],lookup=name=>form.children.find(n=>n.attrs.Name===name).attrs;
  const frame=lookup('pnlFrameBox'),textAttrs=lookup('lblText'),ok=lookup('btnOk');
  const contentWidth=options.contentWidth??Number(form.attrs.Width),buttonGap=4;
  const normalized=buttons.map(b=>({...b,width:b.width??Number(ok.Width),height:b.height??Number(ok.Height)}));
  if(!normalized.length&&!customBody)throw Error('Message boxes require at least one button.');
  await document.fonts.load('400 14px BF');
  const measure=document.createElement('canvas').getContext('2d');measure.font='400 14px BF';
  const textWidth=contentWidth-Number(textAttrs.PosX)-(Number(form.attrs.Width)-Number(textAttrs.PosX)-Number(textAttrs.Width));
  const lines=[];for(const paragraph of text.split('\n')){let line='';for(const ch of paragraph){if(line&&measure.measureText(line+ch).width>textWidth){lines.push(line);line=''}line+=ch;}lines.push(line);}
  const bodyHeight=customBody?(options.contentHeight??214):Math.max(Number(frame.Height),lines.length*15);
  const buttonY=options.buttonY??bodyHeight+(Number(ok.PosY)-Number(frame.Height)),contentHeight=customBody?(options.contentHeight??214):buttonY+Number(ok.Height)+1;
  const x=options.contentX??Number(manifest.scheme.Dialog.Title_Margin_Left),y=options.contentY??Number(manifest.scheme.Dialog.Title_Margin_Top);
  const actionBottom=buttonY+Math.max(0,...normalized.map(b=>b.height));
  const el=await windowShell(options.outerWidth??contentWidth+20,Math.max(options.outerHeight??0,contentHeight+y+12,actionBottom+y+12),title);
  const content=place(document.createElement('div'),x,y,contentWidth,Math.max(contentHeight,actionBottom));content.className='bf-form';el.append(content);
  if(customBody)await body(content,{width:contentWidth,height:contentHeight,bodyHeight,buttonY});
  else{
    await panel(content,Number(frame.PosX),Number(frame.PosY),contentWidth-6,bodyHeight,frame.SkinSpriteSet);
    const names={notice:0,warning:1,question:2,error:3},iconAttrs=form.children.find(n=>n.attrs.Name==='pnlIcon'&&Number(n.attrs.ArrayIndex)===(names[iconKind]??0)).attrs;
    const image=new Image();image.src=base()+iconAttrs.SkinImage;await image.decode();image.className='control';place(image,Number(iconAttrs.PosX),Math.floor((bodyHeight-image.naturalHeight)/2),image.naturalWidth,image.naturalHeight);content.append(image);
    const copy=place(document.createElement('div'),Number(textAttrs.PosX),0,textWidth,bodyHeight);copy.className='message-copy';content.append(copy);
    await paintText(copy,lines.join('\n'),{family:'BF',size:14,weight:400,color:'#e4f3f4',align:'center',height:bodyHeight,width:textWidth,decoration:1,lineHeight:15});
  }
  const span=normalized.reduce((sum,b)=>sum+b.width,0)+Math.max(0,normalized.length-1)*buttonGap;
  let cursor=Math.floor((contentWidth-span)/2);
  for(const b of normalized){await button(content,b.name,b.x??cursor,buttonY,b.width,b.height,b.label,b.sprite);cursor+=b.width+buttonGap;}
  return el;
}

async function formV3(path,title){
  const form=manifest.forms[path];if(!form)throw Error(`Missing cn-774 layout: ${path}`);
  const scheme=manifest.scheme.Dialog,left=Number(scheme.Title_Margin_Left),top=Number(scheme.Title_Margin_Top);
  const el=await windowShell(Number(form.attrs.Width)+left+Number(scheme.Title_Margin_Right),Number(form.attrs.Height)+top+Number(scheme.Title_Margin_Bottom),title||form.attrs.Text);
  const copy=structuredClone(form);Object.assign(copy.attrs,{PosX:String(left),PosY:String(top)});await renderNode(copy,el);return el;
}

async function settingsV3(){
  const layout=manifest.forms['Option/DlgOption4.ui'],a=manifest.scheme.Dialog,left=Number(a.Title_Margin_Left),top=Number(a.Title_Margin_Top);
  const el=await windowShell(Number(layout.attrs.Width)+left+Number(a.Title_Margin_Right),Number(layout.attrs.Height)+top+Number(a.Title_Margin_Bottom),layout.attrs.Text);
  const form=place(document.createElement('div'),left,top,Number(layout.attrs.Width),Number(layout.attrs.Height));form.className='bf-form';el.append(form);
  const plane=layout.children.find(n=>n.attrs.Name==='pnlBackPlane');const tabs=[];let active=1,revision=0;
  const content=place(document.createElement('div'),Number(plane.attrs.PosX),Number(plane.attrs.PosY),Number(plane.attrs.Width),Number(plane.attrs.Height));content.className='control';form.append(content);
  async function show(index){
    const current=++revision;active=index;
    for(let i=0;i<tabs.length;i++){tabs[i].ariaSelected=String(i===index);await tabs[i].setSelected(i===index);}
    const layer=document.createElement('div');place(layer,0,0,Number(plane.attrs.Width),Number(plane.attrs.Height));layer.className='control';
    await skin(layer,plane.attrs.SkinSpriteSet,Number(plane.attrs.Width),Number(plane.attrs.Height));
    const page=structuredClone(manifest.forms[index?'Option/DlgOption-VideoSound3.ui':'Option/DlgOption-Game5.ui']);Object.assign(page.attrs,{PosX:'0',PosY:'0'});
    const visit=n=>{if(Number(n.attrs.PosY)>=1000)n.attrs.Visible='False';if(n.attrs.Name==='chkWindowMode')n.attrs.Check='1';if(['tbBGMVolume','tbEffectVolume'].includes(n.attrs.Name))n.attrs.Position='75';n.children.forEach(visit)};visit(page);
    await renderNode(page,layer);if(current===revision)content.replaceChildren(layer);
  }
  for(const name of ['btnTabMouseSense','btnTabDisplaySound']){const n=layout.children.find(n=>n.attrs.Name===name);const tab=await renderNode(n,form);tab.setAttribute('role','tab');const i=tabs.length;tab.onclick=()=>{void show(i)};tabs.push(tab);}
  for(const name of ['btnOkay','btnCancel'])await renderNode(layout.children.find(n=>n.attrs.Name===name),form);
  await show(active);return el;
}
async function settings() {
  if(theme==='v3')return settingsV3();
  const modern = theme === 'v2';
  const layout=manifest.forms[modern?'DlgOption2.ui':'DlgOption.ui'];
  const marginLeft=Number(manifest.scheme.Dialog.Title_Margin_Left);
  const marginTop=Number(manifest.scheme.Dialog.Title_Margin_Top);
  const width=Number(layout.attrs.Width)+marginLeft+Number(manifest.scheme.Dialog.Title_Margin_Right);
  const height=Number(layout.attrs.Height)+marginTop+Number(manifest.scheme.Dialog.Title_Margin_Bottom);
  const findControl=name=>layout.children.find(node=>node.attrs.Name===name).attrs;
  const plane=findControl('pnlBackPlane');
  const panelHeight=Number(plane.Height);
  const el = await windowShell(width, height, '选项');
  const content = place(document.createElement('div'), marginLeft+Number(plane.PosX), marginTop+Number(plane.PosY), Number(plane.Width), panelHeight);
  content.className = 'control';
  el.append(content);
  const copy = structuredClone(manifest.forms[modern ? 'DlgOption-VideoSound2.ui' : 'DlgOption-VideoSound.ui']);
  copy.attrs = {...copy.attrs, PosX: '0', PosY: '0'};
  copy.children = copy.children.filter(n => !['chkResolutionRatio', 'lblResolutionRatio'].includes(n.attrs.Name));
  for (const n of copy.children) {
    if (['chkWindowMode', 'chkLowQuality'].includes(n.attrs.Name)) n.attrs.Check = '1';
    if (n.attrs.Name === 'chkBgm') n.attrs.Check = '0';
    if (n.type === 'UiTrackBar') n.attrs.Position = '75';
    if (n.type === 'UiLabel' && n.attrs.Text.includes('低清晰度模式')) {
      Object.assign(n.attrs, {Text: '( 低清晰度模式重启游戏后生效 )', Width: '250', FontSize: '12'});
    }
    if (n.type === 'UiLabel' && n.attrs.Text === '창모드로 실행') n.attrs.Text = modern ? '窗口模式' : '窗口模式运行';
    if (n.type === 'UiLabel' && n.attrs.Text === '음소거') n.attrs.Text = '静音';
  }
  const tabs = [];
  let active = 1;
  async function show(index) {
    active = index;
    for (let i = 0; i < tabs.length; i++) {
      tabs[i].replaceChildren();
      tabs[i].ariaSelected = String(i === index);
      const path = `Images/TaskBar/Option/OptionTab0${i + 1}.smf`;
      await tile(tabs[i], path, manifest.sprites[path][i === index ? 3 : 0], 0, 0, i ? 106 : 73, 27);
    }
    content.replaceChildren();
    await skin(content, 'Images/Ui/DialogInnerSkin03.smf', 295, panelHeight);
    if (index === 1) await renderNode(copy, content);
    else {
      const game = structuredClone(manifest.forms['DlgOption-Game.ui']);
      Object.assign(game.attrs, {PosX: '0', PosY: '0'});
      game.children = game.children.filter(n => n.attrs.Name !== 'chkErp');
      await renderNode(game, content, {gbMouseSense: {Text: '鼠标灵敏度调节'}});
    }
  }
  for (let i = 0; i < 2; i++) {
    const attrs=findControl(i?'btnTabDisplaySound':'btnTabMouseSense');
    const tab = place(document.createElement('button'), marginLeft+Number(attrs.PosX), marginTop+Number(attrs.PosY), Number(attrs.Width), Number(attrs.Height));
    tab.className = 'control sprite-button';
    tab.ariaLabel = i ? '画面/声音设置' : '游戏设置';
    tab.onclick = () => show(i);
    tabs.push(tab);
    el.append(tab);
  }
  await show(active);
  for(const [name,label] of [['btnOkay','确认'],['btnCancel','取消']]){
    const attrs=findControl(name);
    await button(el,name==='btnOkay'?'Ok':'Cancel',marginLeft+Number(attrs.PosX),marginTop+Number(attrs.PosY),Number(attrs.Width),Number(attrs.Height),label);
  }
  return el;
}

async function panel(parent,x,y,w,h,path='Images/Ui/FrameBox.smf'){
if(theme==='v3')path=({'Images/Ui/FrameBox.smf':'Images/Ui/dialogueNew/dialogue_basic02.smf','Images/Ui/DialogInnerSkin03.smf':'Images/Ui/dialogueNew/dialogue_basic02.smf','Images/Ui/DialogInnerSkin06.smf':'Images/Ui/dialogueNew/dialogue_basic03.smf'}[path])||path;
const el=place(document.createElement('div'),x,y,w,h);el.className='control';await skin(el,path,w,h);parent.append(el);return el;}
/** Theme tokens affect appearance only; extension settings share all geometry. */
const settingStyles={
  v1:{color:'#5b5b5b',muted:'#808080',font:'BFMalgun',size:13,tab:'#ffffff',inactive:'#687075',panel:'Images/Ui/DialogInnerSkin03.smf',group:'Images/Ui/DialogSkin-Basic04.smf',groupText:'#ffffff',groupX:26,groupY:4,groupSize:13,groupHeight:18,groupAlign:'top'},
  v2:{color:'#5b5b5b',muted:'#808080',font:'BFMalgun',size:13,tab:'#ffffff',inactive:'#849ba6',panel:'Images/Ui/DialogInnerSkin03.smf',group:'Images/Ui/DialogSkin-Basic04.smf',groupText:'#ffffff',groupX:26,groupY:4,groupSize:13,groupHeight:18,groupAlign:'top'},
  v3:{color:'#bbdde0',muted:'#89b4ca',font:'BF',size:13,tab:'#ffffff',inactive:'#e0eff4',panel:'Images/Ui/dialogueNew/dialogue_basic02.smf',group:'Images/Ui/dialogueNew/dialogue_basic03.smf',groupText:'#29d4ff',groupX:9,groupY:5,groupSize:14,groupHeight:20,groupAlign:'middle'}
}[theme];
async function settingsLabel(parent,text,x,y,w,h,muted=false){const el=place(document.createElement('span'),x,y,w,h);el.className='control';el.textContent=text;parent.append(el);await paintText(el,text,{family:settingStyles.font,size:muted?12:settingStyles.size,weight:400,color:muted?settingStyles.muted:settingStyles.color,nativeMetrics:true,vertical:'middle',inset:0});return el;}
async function settingsPanel(parent,x,y,w,h){return panel(parent,x,y,w,h,settingStyles.panel);}
// cn-717 sub_B6C9E0 uses (26,4), format 0; option forms override size to 13.
async function settingsGroup(parent,text,x,y,w,h){const el=await panel(parent,x,y,w,h,settingStyles.group);el.dataset.bfSettingsGroup='';const label=place(document.createElement('span'),settingStyles.groupX,settingStyles.groupY,w-settingStyles.groupX-6,settingStyles.groupHeight);label.className='control';el.append(label);await paintText(label,text,{family:'BF',size:settingStyles.groupSize,weight:400,color:settingStyles.groupText,nativeMetrics:true,vertical:settingStyles.groupAlign,inset:0});return el;}
/** Paint labels with the original font; tab surfaces use CSS and no tab bitmap. */
async function textTab(parent,text,x,y,w,h){
  const el=place(document.createElement('button'),x,y,w,h);el.className='control sprite-button bf-text-tab';el.dataset.theme=theme;el.ariaLabel=text;el.setAttribute('role','tab');parent.append(el);
  const activeLayer=place(document.createElement('span'),0,0,w,h),inactiveLayer=place(document.createElement('span'),0,0,w,h);
  activeLayer.className='bf-tab-active';inactiveLayer.className='bf-tab-inactive';el.append(activeLayer,inactiveLayer);
  await Promise.all([[activeLayer,settingStyles.tab],[inactiveLayer,settingStyles.inactive]].map(([label,color])=>paintText(label,text,{family:settingStyles.font,size:13,weight:400,color,nativeMetrics:true,vertical:'middle',align:'center',inset:0})));
  el.setActive=active=>{el.ariaSelected=String(active);el.tabIndex=active?0:-1;};el.setActive(false);return el;
}
function label(parent,text,x,y,w,h,color=theme==='v3'?'#bbdde0':'#5b5b5b',options={}){const el=place(document.createElement('span'),x,y,w,h);el.className='control bf-label';el.textContent=text;el.style.color=color;parent.append(el);paintText(el,text,{color,nativeMetrics:true,vertical:'middle',wrap:text.length>40,...options});return el;}
async function checkbox(parent,text,x,y,checked=false){await renderNode({type:'UiCheckBox',attrs:{Name:text,PosX:String(x),PosY:String(y),Width:'21',Height:'18',Check:checked?'1':'0'},children:[]},parent);label(parent,text,x+30,y+1,100,20,theme==='v3'?'#bbdde0':'#5b5b5b',{size:11,family:'BF'});}
async function room(){if(theme==='v3')return formV3('Stages/Lobby/DlgCreateRoom.ui','创建房间');const modern=theme==='v2',w=450,h=modern?694:460;const el=await windowShell(w,h,'创建房间');await panel(el,9,36,431,63);label(el,'房间名称',24,43,60,23);label(el,modern?'非公开':'密码',39,72,50,20);await checkbox(el,'',15,75);for(const [x,y,width,value] of [[89,42,343,modern?'今天看我的！':'逃跑达人宝宝'],[89,70,110,'']]){const input=place(document.createElement('input'),x,y,width,23);input.className='control bf-edit';input.value=value;input.ariaLabel=y===42?'房间名称':'密码';el.append(input);}label(el,modern?'输入6位以内的字符':'最多6个字符',204,72,180,20,'#258cea');await panel(el,9,105,431,h-149);const data=manifest.forms['DlgCreateRoom.ui'];const modes=data.children.filter(n=>n.type==='UiButton'&&n.attrs.Name==='btnGameMode');const modeButtons=[];const wanted=modern?['13_Beast','12_Demolition','01_Noitem','02_Item','07_Sniper','06_Npc','05_Zombie','09_Zombie_Pve','08_Surviaval','08_Survival_Team','11_Survival_Sniper']:['01_Noitem','02_Item','03_Starsingle','04_Starteam','05_Zombie'];for(let i=0;i<wanted.length;i++){const n=modes.find(n=>n.attrs.SpriteSet.includes(wanted[i]));if(!n)continue;const mode=await button(el,'Mode',16,113+i*49,190,51,wanted[i],n.attrs.SpriteSet);modeButtons.push(mode);await mode.setSelected(i===(modern?2:0));mode.onclick=async()=>{for(const other of modeButtons)await other.setSelected(other===mode);};}
const description='只使用泡泡枪的基本规则：“泡封”、“击爆”、“营救”来展现自身实力的游戏模式。要精确击中对方和及时解救我方，才能获取胜利哦。';await panel(el,modern?207:205,modern?326:145,modern?228:228,modern?321:262,'Images/Ui/DialogInnerSkin06.smf');if(modern){await panel(el,207,111,228,114,'Images/Ui/DialogInnerSkin06.smf');label(el,'游戏选项',214,116,100,20,'#2a7ed4');for(const [text,x,y] of [['锤子战',214,139],['回合战',214,160],['极速击爆',214,181],['地图选项',214,202],['组织比赛',327,139]])await checkbox(el,text,x,y);await panel(el,207,229,228,94,'Images/Ui/DialogInnerSkin06.smf');label(el,'房间选项',214,234,100,20,'#2a7ed4');await checkbox(el,'随机组',214,257);await checkbox(el,'闯入',214,278,true);label(el,'模式说明',214,331,100,20,'#2a7ed4');}const copy=label(el,description,modern?211:208,modern?351:151,modern?217:218,100,'#5b5b5b',{vertical:'top',size:12});copy.style.alignItems='flex-start';copy.style.lineHeight='15px';await button(el,'Ok',modern?157:298,h-35,modern?65:67,modern?27:25,'确认');await button(el,'Cancel',modern?226:373,h-35,modern?65:68,modern?27:25,'取消');return el;}
/** Compatibility entry point using the same shared extension settings tree. */
async function customSettings(options){return drawExtensionSettings(ui,options);}
async function message(){
  const modern=theme!=='v1',w=modern?300:320,h=modern?60:82;
  const el=place(document.createElement('div'),0,0,w,h);el.className='bf-form';
  await skin(el,modern?'Images/Ui/dialogue_Notify_Back00.smf':'Images/Ui/NotifyBox.smf',w,h);
  const text=place(document.createElement('span'),18,8,w-36,h-16);text.className='control';el.append(text);
  const notify=manifest.scheme.NotifyBox;
  await paintText(text,modern?'9不在线':'9不在线。',{size:modern?14:Number(notify.FontSize),weight:modern?400:Number(notify.FontWeight),family:'BF',align:'center',color:modern?'white':'#111',outline:modern?'#168ac2':'#888',outlineWidth:modern?2:0.75,decoration:modern?4:Number(notify.TextDecoration)});
  if(modern){
    const path='Images/Ui/Notify_Progress.smf';
    const frames=manifest.sprites[path];
    let frame=0;
    const draw=async()=>{el.querySelectorAll('.notify-progress').forEach(node=>node.remove());const box=place(document.createElement('span'),w-28,h-28,20,20);box.className='tile notify-progress';const image=new Image();image.src=base()+path.replace(/\.smf$/i,'')+'/'+frame+'.png';Object.assign(image.style,{width:'100%',height:'100%',left:'0',top:'0'});box.append(image);el.append(box);};
    await draw();
    window.setInterval(()=>{frame=(frame+1)%frames.length;void draw();},180);
  }
  return el;
}

  const trackBar=(parent,attrs)=>renderNode({type:'UiTrackBar',attrs,children:[]},parent);
  const groupBox=(parent,attrs)=>renderNode({type:'UiGroupBox',attrs,children:[]},parent);
  const ui={theme,manifest,sounds,trackBar,groupBox,windowShell,renderNode,button,panel,label,checkbox,dialog,settings,room,message,customSettings,settingsLabel,settingsPanel,settingsGroup,textTab};
  return {...ui,extensionSettings:options=>drawExtensionSettings(ui,options)};
}
