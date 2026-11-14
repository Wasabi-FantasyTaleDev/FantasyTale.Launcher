import {paintText} from './text.js';

const iconSkin = path => /(?:^|\/)(?:DialogButton|RestrictionButton)(?:Ok|Cancel)-(?:Up|Down|Hover|Disabled)\.smf$/i.test(path);

/** Whether the skins are the O/X icon buttons (caption right of the icon) rather than plain text buttons. */
export function iconCaptionSkins(skins) { return skins.some(iconSkin); }

/** Resolve the original icon-button height from its inclusive nine-slice rectangles. */
export function captionButtonGeometry(ui, {skins, width, height}) {
  if (skins.length !== 4) throw new Error('Caption buttons require four native state skins.');
  const nativeSkin = iconSkin;
  if (!skins.some(nativeSkin) || ui.manifest === undefined) return {width, height};
  if (!skins.every(nativeSkin)) throw new Error('Native caption buttons require matching icon-button state skins.');
  const heights = skins.map(path => {
    const frames = ui.manifest?.sprites?.[path];
    if (!Array.isArray(frames) || frames.length !== 9) throw new Error(`Native caption button requires nine-slice frames: ${path}`);
    for (const frame of frames) {
      if (!Array.isArray(frame) || frame.length !== 4 || !frame.every(Number.isInteger) ||
          frame[0] < 0 || frame[1] < 0 || frame[2] < frame[0] || frame[3] < frame[1])
        throw new Error(`Native caption button has an invalid inclusive frame rectangle: ${path}`);
    }
    let nativeHeight = 0;
    for (const index of [0,3,6]) {
      const rowHeight = frames[index][3] - frames[index][1] + 1;
      if (frames.slice(index,index + 3).some(frame => frame[3] - frame[1] + 1 !== rowHeight))
        throw new Error(`Native caption button has inconsistent nine-slice row heights: ${path}`);
      nativeHeight += rowHeight;
    }
    return nativeHeight;
  });
  if (heights.some(value => value !== heights[0])) throw new Error('Native caption button state skins have different original heights.');
  return {width, height:heights[0]};
}

/** The native O/X button height in client pixels: the Create Room dialog's own OK and Cancel buttons. */
export const NATIVE_ACTION_HEIGHT = 27;

/**
 * Width of a native button that fits its captions: the widest caption plus `margin`, never narrower than `minimum`.
 * The defaults are the O/X button's 37-pixel icon margin and 65-pixel width; a plain text button (kr-1256's
 * dialogue_type01BTN, 83 pixels in DlgCreateRoom) passes its own. Pass every caption the button can show so a language
 * switch keeps one width. `measure(text)` returns a caption's advance width in client pixels.
 */
export function fitCaptionWidth(captions, measure, {margin = 37, minimum = 65} = {}) {
  if (!Array.isArray(captions) || captions.length === 0) throw new Error('Fitting a caption button needs at least one caption.');
  let widest = 0;
  for (const caption of captions) {
    const width = measure(caption);
    if (!Number.isFinite(width) || width < 0) throw new Error('A caption measurement must be a finite nonnegative width.');
    widest = Math.max(widest, width);
  }
  return Math.max(minimum, Math.ceil(widest) + margin);
}

/**
 * Measures captions in the native bold caption face (13 pixels by default) once the font is loaded, then fits the width.
 * @param {string[]} captions
 * @param {string} [family]
 * @param {{size?: number, margin?: number, minimum?: number}} [options]
 */
export async function measureCaptionWidth(captions, family = 'BF', {size = 13, margin, minimum} = {}) {
  await document.fonts.load(`700 ${size}px ${family}`);
  const context = document.createElement('canvas').getContext('2d');
  if (!context) throw new Error('Native button captions require a text measurement context.');
  context.font = `700 ${size}px ${family}`;
  return fitCaptionWidth(captions, caption => context.measureText(caption).width, {margin, minimum});
}

// A theme colour such as 0xFF232323 (ARGB) as CSS.
function argb(value, fallback) {
  const match = /^0x([0-9a-f]{2})([0-9a-f]{6})$/i.exec(String(value ?? ''));
  return match ? `#${match[2]}` : fallback;
}

/** Compose native state surfaces once so pointer transitions cannot detach the click target. */
export async function createCaptionButton(ui, {label, width, height, skins, disabled = false, kind = 'Ok', family = 'BF', size = 12, weight = 700, renderer = 'native', resourceRoot = '', pixelRatio = 1}) {
  const surface = captionButtonGeometry(ui, {skins, width, height});
  // DialogButton/RestrictionButton left-middle frames contain the icon; keep their original height at an integer origin.
  const top = Math.floor((height - surface.height) / 2);
  const button = document.createElement('button');
  button.className = 'control sprite-button';
  button.ariaLabel = label;
  button.style.cssText = `left:0;top:0;width:${width}px;height:${height}px`;
  button.disabled = disabled;
  const layers = [];
  for (const path of skins) {
    const layer = await ui.panel(button, 0, top, surface.width, surface.height, path);
    layer.style.pointerEvents = 'none';
    layers.push(layer);
  }
  const icon = iconCaptionSkins(skins);
  // The client's own disabled frame (the last frame of Images/Ui/DialogBtnOk) is neutral grey; the separate
  // DialogButtonOk-Disabled skin is tinted blue, so it is drawn without colour. Plain text skins keep their own.
  if (icon) layers[3].style.filter = 'grayscale(1)';
  const text = document.createElement('span');
  text.className = 'control';
  // Icon buttons keep their caption right of the icon; a plain text button centres it across the whole face.
  const [left, span] = icon ? [27, surface.width - 33] : [0, surface.width];
  text.style.cssText = `left:${left}px;top:${top}px;width:${span}px;height:${surface.height}px;pointer-events:none`;
  button.append(text);
  const scheme = ui.manifest?.scheme?.UiButton ?? {};
  // The client's icon buttons (DialogButtonOk/Cancel) draw white captions: v1 plain, v2 with a #4190d2 outline
  // (owner's reading of the in-game buttons). Disabled captions follow the client's disabled DialogBtnOk frame: white
  // with a #949494 outline, the whole frame at 60% opacity. Plain text buttons (kr-1256 dialogue_type01BTN) use the
  // theme's UiButton text colours (v3: 0xFF232323, disabled 0xFFe1e9eb), unoutlined.
  const outlined = icon && ui.theme !== 'v1';
  const color = icon ? '#fff' : disabled ? argb(scheme.DisabledTextColor, '#e1e9eb') : argb(scheme.TextColor, '#232323');
  await paintText(text, label, {family, size, weight, align:'center', vertical:'middle', centerInk:true,
    renderer, resourceRoot, pixelRatio, color, decoration:outlined ? 4 : 0,
    outline:outlined ? (disabled ? '#949494' : '#4190d2') : undefined, outlineWidth:0.5, inset:0});
  if (disabled && icon) text.style.opacity = '0.6';
  function paint(state) {
    layers.forEach((layer, index) => { layer.style.visibility = index === (button.disabled ? 3 : state) ? 'visible' : 'hidden'; });
  }
  paint(0);
  for (const [event, state] of [['pointerenter',2], ['pointerleave',0], ['pointerdown',1], ['pointerup',2], ['pointercancel',0]]) {
    button.addEventListener(event, () => paint(state));
  }
  button.addEventListener('click', () => {
    if (!button.disabled) button.dispatchEvent(new CustomEvent('bf-action', {bubbles:true, detail:{name:kind, text:label}}));
  });
  return button;
}
