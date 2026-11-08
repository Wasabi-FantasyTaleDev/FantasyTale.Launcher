let nextSettingsId=0;
/** Shared settings state; paired checkbox values are represented by one enum. */
export function extensionSettingsState(value={}){
  const state={oneClickRun:false,healthDisplay:'percent',allowEscLeave:false,bgm:'old',...value};
  if(typeof state.oneClickRun!=='boolean'||typeof state.allowEscLeave!=='boolean'||!['percent','value'].includes(state.healthDisplay)||!['old','new'].includes(state.bgm))throw Error('Invalid extension settings.');
  return state;
}

/** Render one control tree; the renderer's theme supplies all presentation. */
export async function drawExtensionSettings(ui,{value={},onChange=()=>{}}={}){
  const state=extensionSettingsState(value);
  const id=++nextSettingsId;
  const el=await ui.dialog({title:'扩展设置',text:'',contentWidth:350,contentHeight:250,buttonY:260,
    buttons:[{name:'Ok',label:'确认'},{name:'Cancel',label:'取消'}],
    body:async root=>{
      const page=await ui.settingsPanel(root,3,27,344,224);
      page.dataset.bfSettingsPanel='';
      const checks=new Map();
      async function check(parent,name,text,x,y,width){
        const paired=name==='healthPercent'||name==='healthValue'||name==='oldBgm'||name==='newBgm';
        const checked=()=>name==='healthPercent'?state.healthDisplay==='percent':name==='healthValue'?state.healthDisplay==='value':name==='oldBgm'?state.bgm==='old':name==='newBgm'?state.bgm==='new':state[name];
        const label=document.createElement('label');Object.assign(label.style,{position:'absolute',left:x+'px',top:y+'px',width:width+'px',height:'20px',cursor:'pointer'});parent.append(label);
        const element=await ui.renderNode({type:'UiCheckBox',attrs:{Name:name,PosX:'0',PosY:'0',Width:'21',Height:'18',Check:checked()?'1':'0'},children:[]},label);
        const input=element.querySelector('input');input.ariaLabel=text;
        const copy=await ui.settingsLabel(label,text,28,0,width-28,20);copy.style.pointerEvents='none';
        checks.set(name,{element,checked});
        input.addEventListener('change',()=>{
          if(paired){if(name==='healthPercent'||name==='healthValue')state.healthDisplay=name==='healthPercent'?'percent':'value';else state.bgm=name==='oldBgm'?'old':'new';}
          else state[name]=input.checked;
          for(const {element,checked} of checks.values())element.setValue(checked());
          onChange({...state});
          el.dispatchEvent(new CustomEvent('bf-settings-change',{bubbles:true,detail:{...state}}));
        });
      }
      const game=document.createElement('div'),sound=document.createElement('div');page.append(game,sound);
      for(const layer of [game,sound])Object.assign(layer.style,{position:'absolute',inset:'0'});
      const controls=await ui.settingsGroup(game,'操作设置',5,5,334,110);
      await check(controls,'oneClickRun','一键跑步',17,35,300);
      await check(controls,'allowEscLeave','允许ESC中途离场',17,70,300);
      const health=await ui.settingsGroup(game,'血量显示',5,120,334,98);
      await check(health,'healthPercent','显示百分比血量',17,41,145);
      await check(health,'healthValue','显示数值血量',184,41,133);
      const music=await ui.settingsGroup(sound,'背景音乐',5,5,334,213);
      await check(music,'oldBgm','使用旧版BGM',17,39,300);
      await check(music,'newBgm','使用新版BGM',17,78,300);
      await ui.settingsLabel(music,'选择游戏中播放的背景音乐版本。',17,126,300,22,true);
      const tabs=[];
      function select(index){game.hidden=index!==0;sound.hidden=index!==1;for(let i=0;i<tabs.length;i++)tabs[i].setActive(i===index);}
      for(const [index,text] of ['游戏设置','声音设置'].entries()){
        const tab=await ui.textTab(root,text,12+index*107,0,104,28);
        tab.id=`bf-extension-tab-${id}-${index}`;tab.setAttribute('aria-controls',`bf-extension-page-${id}-${index}`);
        const layer=index?sound:game;layer.id=`bf-extension-page-${id}-${index}`;layer.setAttribute('role','tabpanel');layer.setAttribute('aria-labelledby',tab.id);
        tab.addEventListener('click',()=>select(index));
        tab.addEventListener('keydown',event=>{if(['ArrowLeft','ArrowRight','Home','End'].includes(event.key)){event.preventDefault();const next=event.key==='Home'?0:event.key==='End'?1:1-index;select(next);tabs[next].focus();}});
        tabs.push(tab);
      }
      select(0);
    }});
  el.addEventListener('bf-action',event=>{event.detail.settings={...state};});
  el.getSettings=()=>({...state});
  return el;
}
