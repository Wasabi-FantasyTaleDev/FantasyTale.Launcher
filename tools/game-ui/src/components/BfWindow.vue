<script setup lang="ts">
import {computed} from 'vue';
import BfNative from './BfNative.vue';
import type {BfRenderer} from '../context';
import {paintText} from '../../text.js';
const props=defineProps<{title:string;width:number;height:number;closeLabel?:string;titleAlign?:'left'|'center';fontFamily?:string;fontWeight?:400|600|700;textRenderer?:'canvas'|'native'|'svg'|'freetype';textScale?:number;resourceRoot?:string;titleBounds?:{top:number;height:number}}>();
const emit=defineEmits<{close:[];action:[Record<string,unknown>];error:[Error]}>();
// Slot state (scrolling or selection) must not rebuild the shell and detach controls holding pointer capture.
const revision=computed(()=>[props.title,props.width,props.height,props.closeLabel,props.titleAlign,props.fontFamily,props.fontWeight,props.textRenderer,props.textScale,props.titleBounds,props.resourceRoot]);
function action(value:Record<string,unknown>){if(value.name==='close')emit('close');emit('action',value)}
async function draw(ui:BfRenderer){
  const element=await ui.windowShell(props.width,props.height,props.title);
  if(props.closeLabel)element.querySelector('.bf-close')?.setAttribute('aria-label',props.closeLabel);
  const caption=element.querySelector('.caption') as HTMLElement|null;
  if(props.titleAlign&&caption){
    const skin=ui.manifest.scheme.Dialog;
    if(props.titleBounds){caption.style.top=props.titleBounds.top+'px';caption.style.height=props.titleBounds.height+'px';}
    if(props.titleAlign==='center')caption.style.left=(props.width-parseFloat(caption.style.width))/2+'px';
    const nominalSize=Number(skin.Title_FontSize),scale=props.textScale??1;
    const size=props.textScale?Math.max(10,Math.round(nominalSize*scale))/scale:nominalSize;
    await paintText(caption,props.title,{size,family:props.fontFamily??'BF',align:props.titleAlign,weight:props.fontWeight??400,
      color:'#'+skin.Title_TextColor.slice(-6),decoration:Number(skin.Title_TextDecoration),
      outline:'#'+skin.Title_TextDecoColor.slice(-6),outlineWidth:props.textRenderer==='svg'||props.textRenderer==='freetype'?0.5:props.textRenderer==='native'?1:2,nativeMetrics:ui.theme!=='v1',renderer:props.textRenderer==='freetype'?'freetype':props.titleBounds&&props.textRenderer!=='svg'?'canvas':props.textRenderer,resourceRoot:props.resourceRoot,pixelRatio:props.textScale,centerInk:Boolean(props.titleBounds),
      vertical:ui.theme==='v3'?'middle':'top'});
    if(props.titleBounds&&props.textRenderer!=='svg'){
      const canvas=caption.querySelector('canvas') as HTMLCanvasElement|null;if(canvas){
      const image=Object.assign(new Image(), { crossOrigin: 'anonymous' });image.alt='';image.ariaHidden='true';image.style.cssText=canvas.style.cssText;
      image.src=canvas.toDataURL('image/png');await image.decode();canvas.replaceWith(image);}
    }
  }
  return element;
}
</script>
<template><BfNative :revision="revision" :draw="draw" @action="action" @error="emit('error',$event)"><slot/></BfNative></template>
