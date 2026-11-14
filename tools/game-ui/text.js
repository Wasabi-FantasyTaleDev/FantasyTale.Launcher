/**
 * Rasterize the requested font and weight at a higher resolution.
 * The default four-times buffer matches the existing logical-coordinate
 * renderer; set globalThis.__BF_TEXT_SAMPLES to compare other buffers.
 */
export async function paintText(element, text, options = {}) {
  const style = getComputedStyle(element);
  const size = options.size || parseFloat(style.fontSize) || 13;
  const family = options.family || 'BFMalgun';
  const weight = options.weight || 400;
  try {
    await document.fonts.load(`${weight} ${size}px ${family}`);
  } catch (cause) {
    throw new Error(`Unable to load UI font ${family} at weight ${weight}.`, {cause});
  }
  const renderer = options.renderer || globalThis.__BF_TEXT_RENDERER || 'canvas';
  if (renderer === 'svg') {
    return paintVectorText(element, text, options, {size, family, weight});
  }
  if (renderer === 'native') {
    return paintNativeText(element, text, options, {size, family, weight});
  }
  if (renderer === 'freetype') {
    return paintFreetypeText(element, text, options, {size, family, weight});
  }
  const width = options.width || parseFloat(element.style.width) || element.clientWidth;
  const height = options.height || parseFloat(element.style.height) || element.clientHeight;
  if (!(width > 0 && height > 0)) throw Error('Text requires positive logical dimensions.');
  const canvas = document.createElement('canvas');
  const configuredSamples = options.samples ?? Number(globalThis.__BF_TEXT_SAMPLES);
  const samples = Number.isFinite(configuredSamples) && configuredSamples >= 1 ? configuredSamples : 4;
  canvas.width = Math.ceil(width * samples);
  canvas.height = Math.ceil(height * samples);
  canvas.style.cssText = `position:absolute;inset:0;width:${width}px;height:${height}px;pointer-events:none`;
  canvas.ariaHidden = 'true';
  const context = canvas.getContext('2d');
  context.scale(samples, samples);
  context.imageSmoothingEnabled = true;
  context.imageSmoothingQuality = 'high';
  context.font = `${weight} ${size}px ${family}`;
  context.fillStyle = options.color || element.style.color || style.color || '#5b5b5b';
  context.textBaseline = options.nativeMetrics || options.centerInk ? 'alphabetic' : 'middle';
  context.textAlign = options.align || 'left';
  context.lineJoin = 'round';
  const lines = [];
  for (const paragraph of text.split('\n')) {
    let line = '';
    for (const character of paragraph) {
      if (options.wrap && line && context.measureText(line + character).width > width - 4) {
        lines.push(line);
        line = '';
      }
      line += character;
    }
    lines.push(line);
  }
  const spacing = options.lineHeight || size + 1;
  const decoration = options.decoration ?? (options.outline ? 4 : 1);
  const border = decoration === 4 || decoration === 2 ? 2 : decoration === 3 ? 1 : 0;
  const metrics = context.measureText(text.split('\n')[0] || ' ');
  // cn-717 sub_BAD380 anchors glyphs at FontSize - 1; sub_BB0440 centers the texture using integer arithmetic.
  const nativeHeight = Math.ceil(size + metrics.actualBoundingBoxDescent) + border * 2;
  const nativeFirst = (options.vertical === 'top' ? 0 : Math.floor(height / 2) - Math.floor(nativeHeight / 2)) + border + size - 1;
  const first = options.centerInk
    ? (height - metrics.actualBoundingBoxAscent - metrics.actualBoundingBoxDescent) / 2 + metrics.actualBoundingBoxAscent
    : options.nativeMetrics ? nativeFirst : options.vertical === 'top' ? size / 2 + 2 : (height - (lines.length - 1) * spacing) / 2;
  const x = context.textAlign === 'center' ? width / 2 : context.textAlign === 'right' ? width - 2 : (options.inset ?? 2);
  lines.forEach((line, index) => {
    const y = first + index * spacing + (options.baselineOffset || 0);
    if (options.outline && decoration > 1) {
      context.strokeStyle = options.outline;
      context.lineWidth = 2 * (options.outlineWidth || 0.75);
      context.strokeText(line, x, y);
    }
    context.fillText(line, x, y);
  });
  element.textContent = text;
  element.style.color = 'transparent';
  element.classList.add('bf-canvas-text');
  element.dataset.text = text;
  element.append(canvas);
  return element;
}

/** Paint glyphs as browser text at the final font size, without a bitmap resize. */
function paintVectorText(element, text, options, {size, family, weight}) {
  const width = options.width || parseFloat(element.style.width) || element.clientWidth;
  const height = options.height || parseFloat(element.style.height) || element.clientHeight;
  if (!(width > 0 && height > 0)) throw Error('Text requires positive logical dimensions.');
  const measure = document.createElement('canvas').getContext('2d');
  measure.font = `${weight} ${size}px ${family}`;
  measure.textBaseline = 'alphabetic';
  const metrics = measure.measureText(text || ' ');
  const namespace = 'http://www.w3.org/2000/svg';
  const svg = document.createElementNS(namespace, 'svg');
  svg.setAttribute('width', String(width)); svg.setAttribute('height', String(height));
  svg.setAttribute('aria-hidden', 'true');
  svg.style.cssText = `position:absolute;inset:0;width:${width}px;height:${height}px;pointer-events:none;overflow:hidden`;
  const glyphs = document.createElementNS(namespace, 'text');
  const align = options.align || 'left';
  glyphs.setAttribute('x', String(align === 'center' ? width / 2 : align === 'right' ? width - 2 : options.inset ?? 2));
  glyphs.setAttribute('y', String((height - metrics.actualBoundingBoxAscent - metrics.actualBoundingBoxDescent) / 2 + metrics.actualBoundingBoxAscent));
  glyphs.setAttribute('text-anchor', align === 'center' ? 'middle' : align === 'right' ? 'end' : 'start');
  glyphs.style.font = `${weight} ${size}px ${family}`;
  glyphs.style.fontSynthesis = 'none';
  glyphs.setAttribute('fill', options.color || '#5b5b5b');
  if (options.outline) {
    glyphs.setAttribute('stroke', options.outline);
    glyphs.setAttribute('stroke-width', String((options.outlineWidth ?? 0.5) * 2));
    glyphs.setAttribute('stroke-linejoin', 'round');
    glyphs.setAttribute('paint-order', 'stroke fill');
  }
  glyphs.textContent = text; svg.append(glyphs);
  element.textContent = text; element.style.color = 'transparent';
  element.dataset.text = text; element.append(svg);
  return element;
}

/** Use the browser's native glyph rasterizer; Canvas remains an explicit fallback. */
function paintNativeText(element, text, options, metrics) {
  const {size, family, weight} = metrics;
  const style = getComputedStyle(element);
  const decoration = options.decoration ?? (options.outline ? 4 : 1);
  const align = options.align || 'left';
  const vertical = options.vertical || 'middle';
  element.textContent = text;
  element.classList.remove('bf-canvas-text');
  Object.assign(element.style, {
    color: options.color || element.style.color || style.color || '#5b5b5b',
    fontFamily: family,
    fontSize: `${size}px`,
    fontWeight: String(weight),
    fontSynthesis: 'none',
    textRendering: 'geometricPrecision',
    whiteSpace: 'pre-wrap',
    textAlign: align,
    lineHeight: `${options.lineHeight || size + 1}px`,
    display: 'flex',
    alignItems: vertical === 'top' ? 'flex-start' : vertical === 'bottom' ? 'flex-end' : 'center',
    justifyContent: align === 'center' ? 'center' : align === 'right' ? 'flex-end' : 'flex-start',
    padding: `${options.inset ?? 0}px`,
    boxSizing: 'border-box',
    WebkitFontSmoothing: 'antialiased',
    MozOsxFontSmoothing: 'grayscale',
    WebkitTextStroke: options.outline && decoration > 1 ? `${options.outlineWidth || 1}px ${options.outline}` : '0 transparent',
    paintOrder: 'stroke fill'
  });
  element.dataset.text = text;
  return element;
}

// ---------------------------------------------------------------------------
// Native FreeType WebAssembly Renderer
// Reproduces cn-717 text pipeline:
// - cn-717 sub_BAE810: size clamped >= 11; dword_FEC7C4 == 2 forces weight 700.
// - cn-717 sub_BB1620 / sub_C61AE0: strokers radius 16 (0.25px), 32 (0.5px), 128 (2.0px).
// - cn-717 sub_BAD380: load flags 40968 (0xA008); FT_Glyph_StrokeBorder outside border;
//   baseline at size - 1 - bearing_top; destination-over alpha blending.
// - cn-717 sub_BAEAC0: decoration 2, 3, 4 two-pass outline rasterization.
// - cn-717 sub_BB0440: integer centring halves (no fractional coordinates).
// ---------------------------------------------------------------------------

const fontDataRegistry = new Map();
let freetypeEnginePromise = null;
let freetypeEngine = null;

const GLYPH_CACHE_LIMIT = 2000;
// Finished caption bitmaps, least recently used dropped first: a card caption is about 25 KB at 150% scale, so the
// limit holds a few pages of every tab in roughly 5 MB.
const CAPTION_CACHE_LIMIT = 200;
const captionCache = new Map();
const TEXT_CACHE_LIMIT = 200;
const glyphCache = new Map();
const textCache = new Map();

/** Register raw font buffer (TTF/WOFF2 ArrayBuffer or Uint8Array) for FreeType rendering. */
export function registerFreetypeFont(family, buffer) {
  if (!family || !buffer) throw new Error('registerFreetypeFont requires family name and buffer.');
  const data = buffer instanceof Uint8Array ? buffer : new Uint8Array(buffer);
  fontDataRegistry.set(family, data);
}

/** Clear FreeType glyph and text texture caches. */
export function clearFreetypeCaches() {
  glyphCache.clear();
  textCache.clear();
}

/** Initialize and cache the FreeType WebAssembly engine and client-matching strokers. */
export async function getFreetypeEngine() {
  if (freetypeEngine) return freetypeEngine;
  if (!freetypeEnginePromise) {
    freetypeEnginePromise = (async () => {
      const { default: initFreeType } = await import('@zkl2333/freetype-wasm');
      const ft = await initFreeType();

      const pStroker = ft.module._malloc(4);

      // cn-717 sub_BB1620 / sub_C61AE0:
      // Stroker 16: emboldening (radius 16 = 0.25px in 26.6, butt cap, round join)
      ft.module._FT_Stroker_New(ft.library, pStroker);
      const strokerBold = ft.module.getValue(pStroker, 'i32');
      ft.module._FT_Stroker_Set(strokerBold, 16, 0, 0, 1);

      // Stroker 9: weight 600, between the client's plain and emboldened text (~0.14px); not a client weight,
      // used where the owner asked for a slightly heavier caption.
      ft.module._FT_Stroker_New(ft.library, pStroker);
      const strokerMedium = ft.module.getValue(pStroker, 'i32');
      ft.module._FT_Stroker_Set(strokerMedium, 9, 0, 0, 1);

      // Stroker 32: decoration 2/3 (radius 32 = 0.5px in 26.6, round cap, round join)
      ft.module._FT_Stroker_New(ft.library, pStroker);
      const strokerDeco32 = ft.module.getValue(pStroker, 'i32');
      ft.module._FT_Stroker_Set(strokerDeco32, 32, 1, 0, 1);

      // Stroker 128: decoration 4 (radius 128 = 2.0px in 26.6, round cap, round join)
      ft.module._FT_Stroker_New(ft.library, pStroker);
      const strokerDeco128 = ft.module.getValue(pStroker, 'i32');
      ft.module._FT_Stroker_Set(strokerDeco128, 128, 1, 0, 1);

      ft.module._free(pStroker);

      const faces = new Map();

      function getFace(family, fontBytes) {
        if (faces.has(family)) return faces.get(family);
        const face = ft.newFace(fontBytes);
        faces.set(family, face);
        return face;
      }

      return {
        ft,
        getFace,
        strokerBold,
        strokerMedium,
        strokerDeco32,
        strokerDeco128,
        dispose() {
          ft.module._FT_Stroker_Done(strokerBold);
          ft.module._FT_Stroker_Done(strokerMedium);
          ft.module._FT_Stroker_Done(strokerDeco32);
          ft.module._FT_Stroker_Done(strokerDeco128);
          for (const face of faces.values()) face.destroy();
          faces.clear();
          ft.destroy();
          freetypeEngine = null;
          freetypeEnginePromise = null;
        }
      };
    })();
  }
  freetypeEngine = await freetypeEnginePromise;
  return freetypeEngine;
}

/** Parse CSS hex color '#rgb', '#rrggbb' or return default. */
function parseCssColor(str, fallback = { r: 91, g: 91, b: 91, a: 255 }) {
  if (!str) return fallback;
  let c = str.trim();
  if (c.startsWith('#')) c = c.slice(1);
  if (c.length === 3) c = c.split('').map(x => x + x).join('');
  if (c.length === 6) {
    return {
      r: parseInt(c.slice(0, 2), 16),
      g: parseInt(c.slice(2, 4), 16),
      b: parseInt(c.slice(4, 6), 16),
      a: 255
    };
  }
  return fallback;
}

/**
 * Pure layout and measurement calculation reproducing cn-717 sub_BAE810, sub_BAD8F0, sub_BAEAC0.
 * Exported for isolated Node unit testing without requiring WebAssembly initialization.
 */
export function computeFreetypeLayout(text, options, { size, weight = 400, glyphWidths = null }) {
  // cn-717 sub_BAE810: font size clamped >= 11
  const clampedSize = options.exactPixelSize ? size : Math.max(11, size);
  // Weight 400 by default (unemboldened). 0.25px stroke is applied only when explicit weight 700 is requested.
  const isBold = weight === 700 || options.weight === 700 || options.bold === true;
  const decoration = options.decoration ?? (options.outline ? 4 : 1);
  // cn-717 sub_BAD8F0: decoration padding
  const border = decoration === 4 || decoration === 2 ? 2 : decoration === 3 ? 1 : 0;
  const spacing = options.lineHeight || clampedSize + 1;
  const inset = options.inset ?? 2;

  const width = options.width;
  const height = options.height;
  if (!(width > 0 && height > 0)) {
    throw new Error('Text requires positive logical dimensions.');
  }

  // Fallback rough char advance if glyphWidths map not supplied
  const getCharAdvance = (char) => {
    if (glyphWidths && glyphWidths.has(char)) return glyphWidths.get(char);
    return char.charCodeAt(0) > 127 ? clampedSize : Math.ceil(clampedSize * 0.55);
  };

  const paragraphs = text.split('\n');
  const lines = [];
  const maxWidth = width - border * 2 - inset * 2;

  for (const para of paragraphs) {
    if (!options.wrap) {
      let lineWidth = 0;
      for (const ch of para) lineWidth += getCharAdvance(ch);
      lines.push({ text: para, width: lineWidth });
      continue;
    }
    let currentLine = '';
    let currentWidth = 0;
    for (const ch of para) {
      const chAdv = getCharAdvance(ch);
      if (currentLine && currentWidth + chAdv > maxWidth) {
        lines.push({ text: currentLine, width: currentWidth });
        currentLine = '';
        currentWidth = 0;
      }
      currentLine += ch;
      currentWidth += chAdv;
    }
    lines.push({ text: currentLine, width: currentWidth });
  }

  // cn-717 sub_BAEAC0 / sub_BB0440: integer arithmetic for vertical centering
  const contentHeight = (lines.length - 1) * spacing + clampedSize + border * 2;
  let firstLineY = border + clampedSize - 1;
  if (options.vertical === 'top') {
    firstLineY = border + clampedSize - 1;
  } else if (options.vertical === 'bottom') {
    firstLineY = height - contentHeight + border + clampedSize - 1;
  } else {
    // cn-717 sub_BB0440: floor(H/2) - floor(contentH/2)
    firstLineY = Math.floor(height / 2) - Math.floor(contentHeight / 2) + border + clampedSize - 1;
  }
  firstLineY += (options.baselineOffset || 0);

  const positionedLines = lines.map((line, idx) => {
    let x = inset + border;
    if (options.align === 'center') {
      // cn-717 sub_BAEAC0: floor((totalWidth - lineWidth) / 2)
      x = Math.floor((width - (line.width + border * 2)) / 2) + border;
    } else if (options.align === 'right') {
      x = width - line.width - border - inset;
    }
    return {
      text: line.text,
      x,
      y: firstLineY + idx * spacing,
      width: line.width
    };
  });

  return {
    clampedSize,
    isBold,
    decoration,
    border,
    spacing,
    lines: positionedLines,
    contentHeight,
    firstLineY
  };
}

/** Paint text using FreeType rasterization at 1x device pixels matching cn-717. */
async function paintFreetypeText(element, text, options, metrics) {
  const cssWidth = options.width || parseFloat(element.style.width) || element.clientWidth;
  const cssHeight = options.height || parseFloat(element.style.height) || element.clientHeight;
  if (!(cssWidth > 0 && cssHeight > 0)) throw new Error('Text requires positive logical dimensions.');

  // Rasterize at the final screen pixels: a logical-size bitmap that CSS zoom or display scaling stretches is
  // resampled and blurred. FreeType also needs a whole pixel size; a fractional one renders nothing.
  const ratio = Math.max(0.25, (options.pixelRatio ?? 1) * (globalThis.devicePixelRatio || 1));
  const width = Math.ceil(cssWidth * ratio);
  const height = Math.ceil(cssHeight * ratio);
  const { size, family, weight = 400 } = metrics;
  // cn-717 sub_BAE810 clamps the logical size to at least 11 before any scaling.
  const clampedSize = Math.max(1, Math.round(Math.max(11, size) * ratio));
  // Weight 400 by default (unemboldened). 0.25px stroke is applied only when explicit weight 700 is requested.
  const isBold = weight === 700 || options.weight === 700 || options.bold === true;
  const isMedium = !isBold && (weight === 600 || options.weight === 600);
  const decoration = options.decoration ?? (options.outline ? 4 : 1);
  const border = decoration === 4 || decoration === 2 ? 2 : decoration === 3 ? 1 : 0;

  // A caption drawn before with the same text, face, box, colours and scale is copied instead of rasterized again;
  // a host can also draw captions ahead (the next pages) so showing them costs a copy.
  const cacheKey = JSON.stringify([text, family, size, weight, cssWidth, cssHeight, ratio,
    options.color || element.style.color || '', options.outline || '', decoration, options.align, options.vertical,
    Boolean(options.centerInk), options.inset, Boolean(options.wrap), options.lineHeight, options.baselineOffset,
    Boolean(options.bold), Boolean(options.nativeMetrics)]);
  const cachedCaption = captionCache.get(cacheKey);
  if (cachedCaption) {
    captionCache.delete(cacheKey);
    captionCache.set(cacheKey, cachedCaption);
    const canvas = document.createElement('canvas');
    canvas.width = width;
    canvas.height = height;
    canvas.style.cssText = `position:absolute;inset:0;width:${cssWidth}px;height:${cssHeight}px;pointer-events:none`;
    canvas.ariaHidden = 'true';
    canvas.getContext('2d').putImageData(new ImageData(new Uint8ClampedArray(cachedCaption), width, height), 0, 0);
    return finishFreetypeText(element, text, canvas);
  }

  // Resolve font buffer
  let fontData = options.fontData || options.fontBuffer || fontDataRegistry.get(family);
  if (!fontData && typeof fetch === 'function') {
    const root = options.resourceRoot || globalThis.__BF_RESOURCE_ROOT || '';
    const prefix = root.replace(/\/?$/, '/');
    // The resources package's compressed copies of the client faces, by the family names the UI registers.
    const fileName = {BF: 'GB13000.woff2', BFMalgun: 'DF_GB_Y9.woff2', BFKorean: 'NexonBTBold.woff2'}[family];
    try {
      const resp = fileName ? await fetch(`${prefix}fonts/${fileName}`) : null;
      if (resp?.ok) {
        const ab = await resp.arrayBuffer();
        fontData = new Uint8Array(ab);
        fontDataRegistry.set(family, fontData);
      }
    } catch {
      // Fall through to error below
    }
  }

  if (!fontData) {
    throw new Error(`FreeType font data for family '${family}' is not loaded or registered.`);
  }

  const engine = await getFreetypeEngine();
  const face = engine.getFace(family, fontData);
  face.setPixelSize(clampedSize, clampedSize);

  // Measure glyph advances
  const glyphWidths = new Map();
  for (const ch of text) {
    if (ch === '\n' || glyphWidths.has(ch)) continue;
    let glyphIndex = face.charIndex(ch.charCodeAt(0));
    if (!glyphIndex) glyphIndex = face.charIndex(9633) || face.charIndex(32);
    engine.ft.module._FT_Load_Glyph(face.ptr, glyphIndex, 40968);
    const slotPtr = engine.ft.module.getValue(face.ptr + engine.ft.offsets.FT_FaceRec.glyph, 'i32');
    const advX = engine.ft.module.getValue(slotPtr + engine.ft.offsets.FT_GlyphSlotRec.advance + engine.ft.offsets.FT_Vector.x, 'i32') >> 6;
    glyphWidths.set(ch, advX);
  }

  const layout = computeFreetypeLayout(text, { ...options, width, height, exactPixelSize: true,
    inset: Math.round((options.inset ?? 2) * ratio), lineHeight: options.lineHeight ? Math.round(options.lineHeight * ratio) : undefined },
    { size: clampedSize, weight, glyphWidths });

  // Render to 1x Canvas
  const canvas = document.createElement('canvas');
  canvas.width = Math.ceil(width);
  canvas.height = Math.ceil(height);
  canvas.style.cssText = `position:absolute;inset:0;width:${cssWidth}px;height:${cssHeight}px;pointer-events:none`;
  canvas.ariaHidden = 'true';

  const ctx = canvas.getContext('2d');
  if (!ctx) throw new Error('Unable to obtain 2D rendering context for canvas.');

  const imgData = ctx.createImageData ? ctx.createImageData(canvas.width, canvas.height) : { data: new Uint8Array(canvas.width * canvas.height * 4), width: canvas.width, height: canvas.height };
  const pixels = imgData.data;

  const style = getComputedStyle(element);
  const textColor = parseCssColor(options.color || element.style.color || style?.color || '#5b5b5b');
  const outlineColor = parseCssColor(options.outline || '#ffffff');

  // Destination-over blending (cn-717 sub_BAD220)
  function blendPixel(dstX, dstY, srcR, srcG, srcB, srcAlpha) {
    if (dstX < 0 || dstX >= canvas.width || dstY < 0 || dstY >= canvas.height) return;
    const idx = (dstY * canvas.width + dstX) * 4;
    const dstA = pixels[idx + 3];

    const outA = Math.min(255, Math.round(dstA + srcAlpha * (255 - dstA) / 255));
    if (outA === 0) return;

    const v4 = Math.round(255 * dstA / outA);
    pixels[idx] = Math.round((v4 * pixels[idx] + (255 - v4) * srcR) / 255);
    pixels[idx + 1] = Math.round((v4 * pixels[idx + 1] + (255 - v4) * srcG) / 255);
    pixels[idx + 2] = Math.round((v4 * pixels[idx + 2] + (255 - v4) * srcB) / 255);
    pixels[idx + 3] = outA;
  }

  // Draw a single glyph with given stroker into pixels
  function drawGlyph(charCode, originX, baselineY, color, stroker = null) {
    // Glyph bitmaps land on whole pixels; a fractional origin would index no pixel at all.
    originX = Math.round(originX); baselineY = Math.round(baselineY);
    let glyphIndex = face.charIndex(charCode);
    if (!glyphIndex) glyphIndex = face.charIndex(9633) || face.charIndex(32);

    const cacheKey = `${family}:${clampedSize}:${isBold ? 1 : isMedium ? 2 : 0}:${stroker ? 1 : 0}:${charCode}`;
    let cached = glyphCache.get(cacheKey);

    if (!cached) {
      engine.ft.module._FT_Load_Glyph(face.ptr, glyphIndex, 40968);
      const slotPtr = engine.ft.module.getValue(face.ptr + engine.ft.offsets.FT_FaceRec.glyph, 'i32');
      const pGlyph = engine.ft.module._malloc(4);
      engine.ft.module._FT_Get_Glyph(slotPtr, pGlyph);

      if (isBold || isMedium) {
        engine.ft.module._FT_Glyph_StrokeBorder(pGlyph, isBold ? engine.strokerBold : engine.strokerMedium, 0, 1);
      }
      if (stroker) {
        engine.ft.module._FT_Glyph_StrokeBorder(pGlyph, stroker, 0, 1);
      }
      engine.ft.module._FT_Glyph_To_Bitmap(pGlyph, 0, 0, 1);
      const glyphPtr = engine.ft.module.getValue(pGlyph, 'i32');

      const left = engine.ft.module.getValue(glyphPtr + 20, 'i32');
      const top = engine.ft.module.getValue(glyphPtr + 24, 'i32');
      const rows = engine.ft.module.getValue(glyphPtr + 28 + engine.ft.offsets.FT_Bitmap.rows, 'i32');
      const cols = engine.ft.module.getValue(glyphPtr + 28 + engine.ft.offsets.FT_Bitmap.width, 'i32');
      const pitch = engine.ft.module.getValue(glyphPtr + 28 + engine.ft.offsets.FT_Bitmap.pitch, 'i32');
      const bufPtr = engine.ft.module.getValue(glyphPtr + 28 + engine.ft.offsets.FT_Bitmap.buffer, 'i32');

      const coverageBuffer = new Uint8Array(rows * cols);
      for (let r = 0; r < rows; r++) {
        for (let c = 0; c < cols; c++) {
          coverageBuffer[r * cols + c] = engine.ft.module.HEAPU8[bufPtr + r * pitch + c];
        }
      }

      engine.ft.module._FT_Done_Glyph(glyphPtr);
      engine.ft.module._free(pGlyph);

      cached = { left, top, rows, cols, coverageBuffer };
      if (glyphCache.size >= GLYPH_CACHE_LIMIT) {
        glyphCache.delete(glyphCache.keys().next().value);
      }
      glyphCache.set(cacheKey, cached);
    }

    const { left, top, rows, cols, coverageBuffer } = cached;
    for (let r = 0; r < rows; r++) {
      for (let c = 0; c < cols; c++) {
        const cov = coverageBuffer[r * cols + c];
        if (cov > 0) {
          const pixelAlpha = Math.round(color.a * cov / 255);
          blendPixel(originX + left + c, baselineY - top + r, color.r, color.g, color.b, pixelAlpha);
        }
      }
    }
  }

  // Pass 1: inner fill text (cn-717 sub_BAEAC0)
  for (const line of layout.lines) {
    let curX = line.x;
    const shift = Math.round(2 * ratio);
    if (decoration === 2) curX -= shift;
    const curY = decoration === 2 ? line.y - shift : line.y;
    for (let i = 0; i < line.text.length; i++) {
      const ch = line.text[i];
      const charCode = ch.charCodeAt(0);
      drawGlyph(charCode, curX, curY, textColor, null);
      curX += glyphWidths.get(ch) || 0;
    }
  }

  // Pass 2: outline stroke pass (cn-717 sub_BAEAC0)
  if (decoration > 1) {
    const outlineStroker = decoration === 4 ? engine.strokerDeco128 : engine.strokerDeco32;
    for (const line of layout.lines) {
      let curX = line.x;
      for (let i = 0; i < line.text.length; i++) {
        const ch = line.text[i];
        const charCode = ch.charCodeAt(0);
        drawGlyph(charCode, curX, line.y, outlineColor, outlineStroker);
        curX += glyphWidths.get(ch) || 0;
      }
    }
  }

  // centerInk: callers that centre a caption in a bar (window title, tabs) centre the drawn ink, as the canvas
  // renderer does; font metrics alone leave CJK captions high in the bar.
  if (options.centerInk) {
    const rowBytes = canvas.width * 4;
    let first = -1, last = -1;
    for (let y = 0; y < canvas.height; y++) {
      for (let i = y * rowBytes + 3; i < (y + 1) * rowBytes; i += 4) {
        if (pixels[i]) { if (first < 0) first = y; last = y; break; }
      }
    }
    const shift = first < 0 ? 0 : Math.floor((canvas.height - (last - first + 1)) / 2) - first;
    if (shift) {
      const moved = new Uint8ClampedArray(pixels.length);
      for (let y = first; y <= last; y++) {
        const to = y + shift;
        if (to >= 0 && to < canvas.height) moved.set(pixels.subarray(y * rowBytes, (y + 1) * rowBytes), to * rowBytes);
      }
      pixels.set(moved);
    }
  }

  ctx.putImageData(imgData, 0, 0);
  captionCache.set(cacheKey, pixels.slice());
  while (captionCache.size > CAPTION_CACHE_LIMIT) captionCache.delete(captionCache.keys().next().value);
  return finishFreetypeText(element, text, canvas);
}

function finishFreetypeText(element, text, canvas) {
  element.textContent = text;
  element.style.color = 'transparent';
  element.classList.remove('bf-canvas-text');
  element.classList.add('bf-freetype-text');
  element.dataset.text = text;
  element.append(canvas);
  return element;
}

