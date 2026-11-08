import type { InjectionKey } from 'vue'

/** Logical ownership survives Teleport, keeping child overlays above their dialog. */
export const overlayLayer: InjectionKey<number> = Symbol('overlay-layer')
