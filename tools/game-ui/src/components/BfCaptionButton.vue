<script setup lang="ts">
import {ref,watch} from 'vue';
import BfNative from './BfNative.vue';
import {createCaptionButton,measureCaptionWidth,NATIVE_ACTION_HEIGHT} from '../../caption-button.js';
/**
 * A native button with its own caption over text-free state skins (four of them: up, down, hover, disabled), as the
 * Ban Pick editor's actions draw. Use it where the theme's dialog button sprite carries a fixed baked-in caption.
 * `renderer: 'freetype'` draws the caption with the client's own glyph rasteriser; it then needs the resource root and
 * the page's text scale. `fit` sizes it like the client's own O/X buttons instead of `width`/`height`: the widest of
 * `fitCaptions` (default: the label) plus the 37-pixel icon margin, at least 65 wide, 27 high.
 */
const props=withDefaults(defineProps<{label:string;skins:string[];width?:number;height?:number;fit?:boolean;fitCaptions?:string[];fitMargin?:number;fitMinimum?:number;kind?:'Ok'|'Cancel';disabled?:boolean;fontFamily?:string;fontSize?:number;fontWeight?:number;
  renderer?:'svg'|'freetype';resourceRoot?:string;textScale?:number}>(),
  {width:65,height:NATIVE_ACTION_HEIGHT,fit:false,fitMargin:37,fitMinimum:65,kind:'Ok',disabled:false,fontFamily:'BF',fontSize:12,fontWeight:700,renderer:'svg',resourceRoot:'',textScale:1});
const emit=defineEmits<{click:[];error:[Error]}>();
const fitted=ref<number>();
// A stale measurement (the label changed while the font loaded) never replaces a newer one.
let measuring=0;
watch(()=>props.fit?[props.fontFamily,String(props.fontSize),String(props.fitMargin),String(props.fitMinimum),...(props.fitCaptions?.length?props.fitCaptions:[props.label])]:null,async captions=>{
  const current=++measuring;if(!captions){fitted.value=undefined;return}
  try{const [family,size,margin,minimum,...texts]=captions;
    const width=await measureCaptionWidth(texts,family,{size:Math.max(13,Number(size)),margin:Number(margin),minimum:Number(minimum)});if(current===measuring)fitted.value=width}
  catch(reason){if(current===measuring)emit('error',reason instanceof Error?reason:new Error(String(reason)))}
},{immediate:true});
const w=()=>props.fit?(fitted.value??props.fitMinimum):props.width;
// `fit` sizes only the width; the height is the native button's (27 for the O/X buttons by default).
const h=()=>props.height;
</script>
<template><div class="bf-caption-button" :style="{position:'relative',width:w()+'px',height:h()+'px'}"><BfNative @error="emit('error',$event)" :revision="[label,skins,w(),h(),kind,disabled,fontFamily,fontSize,fontWeight,renderer,resourceRoot,textScale]" :draw="ui => createCaptionButton(ui, {label, width:w(), height:h(), skins, disabled, kind, family:fontFamily, size:fontSize, weight:fontWeight, renderer, resourceRoot, pixelRatio:textScale})" @action="emit('click')"/></div></template>
