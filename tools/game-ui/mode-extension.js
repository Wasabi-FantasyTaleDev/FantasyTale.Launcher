/** Mode-specific artwork in kr-1256 Ui/Stages/Lobby/DlgCreateRoom_add_survival.ui. */
export const MODE_SKINS = Object.freeze({
  Pvp:'Noitem', Item:'Item', PvpStarTeam:'StarTeam', Zombie:'Zombie', EventSoccer:'Soccer',
  Sniper:'Sniper', SinglePvp:'Survival', TeamSurvival:'Survival', ZombiePve:'ZombieBoss',
  SniperTeamSurvival:'SurvivalSna', Bomb:'Demolition', Captain:'Captain', BfRunner:'CrazyRacing',
  Area32:'BoomhillWorld', ExtremeRacing:'LetsRun', SuddenSurvival:'Area32', WeaponMaster:'WeaponMaster',
  Snowfight:'WaterBomb', ZombieGhost:'ZombieGhost', ControlPoint:'ControlPoint', FinalZone:'FinalZone',
  Shotgun:'Shot', ZombieEscape:'Escape', AllPang:'AllPang', JumpJump:'Jump', Psionic:'Psionic',
  NoItemCustomAiWar:'CustomAI', TeamSurvivalCustomAiWar:'TeamSurvivalCustomAiWar', BnB:'BnB',
  Horde:'Pirates', PirateAssaultSeason3:'PiratesAttack',
  NoItemPractice:'Noitem', NoItemTutorial:'CustomAI', ZombieOrigin:'ZombieGhost',
  SimpleSuddenSurvival:'Area32', ZombieSurvival:'Escape',
});

/** The older window themes retain the original ghost artwork for Zombie mode. */
export const MODE_SKIN_OVERRIDES = Object.freeze({
  v1: {Zombie:'ZombieGhost'},
  v2: {Zombie:'ZombieGhost'},
  v3: {},
});

/**
 * An unknown artwork name uses the competitive-mode graphic without changing the mode identity.
 * @param {string} key
 * @param {import('./src/mode-extension').ModeExtensionAppearance['skins']} skins
 * @param {'v1'|'v2'|'v3'} theme
 */
export function modeSkin(key, skins, theme = 'v3') {
  const name = MODE_SKIN_OVERRIDES[theme]?.[key] ?? MODE_SKINS[key] ?? 'Noitem';
  const skin = skins[name] ?? skins.Noitem;
  if (!skin || skin.states.length !== 4) throw new Error('Four competitive-mode button states are required.');
  return skin;
}

/**
 * Preserve the complete server catalogue, rejecting duplicate or malformed identities.
 * @param {readonly import('./src/mode-extension').ModeExtensionMode[]} modes
 */
export function validateModes(modes) {
  const ids = new Set();
  for (const mode of modes) {
    if (!Number.isSafeInteger(mode.id) || mode.id < 0 || ids.has(mode.id)
      || typeof mode.key !== 'string' || !mode.key || !mode.names || typeof mode.names.en !== 'string')
      throw new Error('Mode catalogue entries require unique integer IDs and localized names.');
    ids.add(mode.id);
  }
  return modes;
}

/**
 * Selection remains a draft; callers invoke this only from the dialog's Confirm action.
 * @param {readonly import('./src/mode-extension').ModeExtensionMode[]} modes
 * @param {number|undefined} draft
 */
export function confirmedMode(modes, draft) {
  return modes.find(mode => mode.id === draft);
}

/**
 * Keep the pending choice separate from the last mode accepted by the native UI.
 * @param {readonly import('./src/mode-extension').ModeExtensionMode[]} modes
 * @param {number|undefined} selectedId
 */
export function createModeSelection(modes, selectedId) {
  const initial = confirmedMode(validateModes(modes), selectedId)?.id;
  return {
    modes, committedId: initial, draftId: initial,
    /** @param {number} id */
    select(id) {
      if (!confirmedMode(this.modes, id)) throw new Error('The selected mode is not in the catalogue.');
      this.draftId = id;
    },
    /** @param {number|undefined} id */
    reset(id) { this.committedId = confirmedMode(this.modes, id)?.id; this.cancel(); },
    /** @param {readonly import('./src/mode-extension').ModeExtensionMode[]} next */
    replaceModes(next) {
      this.modes = validateModes(next);
      this.committedId = confirmedMode(next, this.committedId)?.id;
      this.draftId = confirmedMode(next, this.draftId)?.id;
    },
    confirm() { return confirmedMode(this.modes, this.draftId); },
    cancel() { this.draftId = this.committedId; },
  };
}

/** Clamp the thumb to the track between the arrow buttons, independently of viewport height. */
export function scrollGeometry(contentHeight, viewportHeight, top, trackHeight, minimumThumb = 24) {
  const maximum = Math.max(0, contentHeight - viewportHeight);
  const height = maximum ? Math.min(trackHeight, Math.max(minimumThumb, trackHeight * viewportHeight / contentHeight)) : trackHeight;
  const travel = trackHeight - height;
  return {maximum, height, travel, top: maximum ? Math.max(0, Math.min(maximum, top)) / maximum * travel : 0};
}

/** Convert pointer motion in logical track pixels into a clamped content position. */
export function dragScroll(top, delta, geometry) {
  return geometry.travel > 0 ? Math.max(0, Math.min(geometry.maximum, top + delta * geometry.maximum / geometry.travel)) : 0;
}

/**
 * Own a captured drag until release, cancellation, lost capture or component disposal.
 * @param {{getTop:()=>number,setTop:(value:number)=>void,getGeometry:()=>ReturnType<typeof scrollGeometry>,getScale:()=>number}} options
 */
export function createScrollDrag({getTop, setTop, getGeometry, getScale}) {
  let active;
  function move(event) {
    if (!active || event.pointerId !== active.pointer) return;
    event.preventDefault();
    setTop(dragScroll(active.top, (event.clientY - active.y) / active.scale, getGeometry()));
  }
  function stop(event = undefined) {
    if (!active || (event && event.pointerId !== active.pointer)) return;
    const previous = active; active = undefined;
    previous.document.removeEventListener('pointermove', move, true);
    previous.document.removeEventListener('pointerup', stop, true);
    previous.document.removeEventListener('pointercancel', stop, true);
    previous.element.removeEventListener('lostpointercapture', stop);
    if (previous.element.hasPointerCapture(previous.pointer)) previous.element.releasePointerCapture(previous.pointer);
  }
  function start(event) {
    if (event.button !== 0 || !getGeometry().maximum) return;
    stop();
    const element = event.currentTarget, scale = getScale();
    if (!(scale > 0) || !Number.isFinite(scale)) throw new Error('A scroll drag requires a visible native track.');
    active = {pointer:event.pointerId,y:event.clientY,top:getTop(),scale,element,document:element.ownerDocument};
    active.document.addEventListener('pointermove', move, true);
    active.document.addEventListener('pointerup', stop, true);
    active.document.addEventListener('pointercancel', stop, true);
    element.addEventListener('lostpointercapture', stop);
    event.preventDefault(); event.stopPropagation();
    try { element.setPointerCapture(event.pointerId); }
    catch (error) { stop(); throw error; }
  }
  return {start, stop};
}

/** Validate explicit build filters against the complete mode catalogue, without inferring client support. */
export function validateModeAvailability(policy, modes) {
  validateModes(modes);
  if (policy?.format !== 1 || !Array.isArray(policy.rules)) throw new Error('Mode availability requires format 1 and a rules array.');
  const known = new Set(modes.map(mode => mode.id));
  for (const rule of policy.rules) {
    if (!rule || !Array.isArray(rule.builds) || !rule.builds.length
      || rule.builds.some(build => typeof build !== 'string' || !/^[A-Za-z0-9*_.-]+$/.test(build)))
      throw new Error('Each mode-availability rule requires build IDs or wildcard patterns.');
    for (const name of ['allow', 'hidden']) {
      if (rule[name] === undefined) continue;
      if (!Array.isArray(rule[name]) || new Set(rule[name]).size !== rule[name].length
        || rule[name].some(id => !Number.isSafeInteger(id) || !known.has(id)))
        throw new Error(`Mode availability ${name} must contain distinct catalogue IDs.`);
    }
    if (rule.allow === undefined && rule.hidden === undefined) throw new Error('A mode-availability rule requires allow or hidden IDs.');
  }
  return policy;
}

/** Matching allow lists intersect and hidden lists subtract; an empty policy preserves every catalogue entry. */
export function filterModesForBuild(modes, policy, buildId) {
  validateModeAvailability(policy, modes);
  if (!policy.rules.length) return modes;
  if (typeof buildId !== 'string' || !buildId) throw new Error('Build-specific mode availability requires the current build ID.');
  const matches = pattern => new RegExp('^' + pattern.split('*').map(part => part.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')).join('.*') + '$', 'i').test(buildId);
  let result = modes;
  for (const rule of policy.rules) {
    if (!rule.builds.some(matches)) continue;
    const allowed = rule.allow === undefined ? undefined : new Set(rule.allow);
    const hidden = new Set(rule.hidden ?? []);
    result = result.filter(mode => (!allowed || allowed.has(mode.id)) && !hidden.has(mode.id));
  }
  return result;
}
/** Keep legacy confirmation rows close to the list while retaining the taller modern controls. */
export function modeWindowGeometry(theme) {
  if (!['v1','v2','v3'].includes(theme)) throw new Error('Unknown mode window theme.');
  return {width:602,height:theme==='v3'?516:496,actionsTop:theme==='v3'?464:448,actionsHeight:theme==='v3'?33:42};
}
