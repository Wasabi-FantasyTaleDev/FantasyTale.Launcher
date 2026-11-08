/**
 * Rasterize the original font at a higher resolution without changing its weight.
 * The default four-times buffer matches the existing logical-coordinate
 * renderer; set globalThis.__BF_TEXT_SAMPLES to compare other buffers.
 */
export async function paintText(element, text, options = {}) {
  const style = getComputedStyle(element);
  const size = options.size || parseFloat(style.fontSize) || 13;
  const family = options.family || 'BFMalgun';
  const weight = options.weight || 400;
  await document.fonts.load(`${weight} ${size}px ${family}`);
  const renderer = options.renderer || globalThis.__BF_TEXT_RENDERER || 'canvas';
  if (renderer === 'native') {
    return paintNativeText(element, text, options, {size, family, weight});
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
  context.textBaseline = options.nativeMetrics ? 'alphabetic' : 'middle';
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
  const first = options.nativeMetrics ? nativeFirst : options.vertical === 'top' ? size / 2 + 2 : (height - (lines.length - 1) * spacing) / 2;
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
