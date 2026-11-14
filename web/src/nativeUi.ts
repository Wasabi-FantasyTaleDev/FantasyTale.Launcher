export const nativeUiKinds = ['messageBox', 'notification', 'announcement.show', 'announcement.close', 'chat',
  'control.inspect', 'control.setText', 'control.addButton', 'control.remove', 'control.events', 'control.activate'] as const
export type NativeUiKind = typeof nativeUiKinds[number]
export interface NativeUiButton { id: string; label: string }
export interface NativeUiTextRun { text: string; color: string }
export interface NativeUiControlTarget { form?: string; name?: string; id?: string; text?: string; arrayIndex?: number }
export interface NativeUiControlDefinition { name: string; x: number; y: number; width: number; height: number; template?: string }
export interface NativeUiControl {
  id: string; form: string; name: string; kind: string; text: string; writable: boolean
  maximumLength?: number | null; x?: number; y?: number; width?: number; height?: number
  enabled?: boolean | null; arrayIndex?: number | null; selectionValue?: number | null; selected?: boolean | null
}
export interface NativeUiControlEvent { sequence: number; controlId: string; event: string }
export interface NativeUiRequest {
  operationId: string
  kind: NativeUiKind
  text: string
  title?: string
  icon?: string
  buttons?: NativeUiButton[]
  announcementId?: string
  chatStyle?: 'normal' | 'system'
  chatCaption?: string
  runs?: NativeUiTextRun[]
  target?: NativeUiControlTarget
  definition?: NativeUiControlDefinition
  afterSequence?: number
  maximumLength?: number
}
export interface NativeUiCapability { kind: NativeUiKind; supported: boolean; ready: boolean; features: string[]; reason?: string | null }
export interface NativeUiCapabilities { build: string; operations: NativeUiCapability[] }
export interface NativeUiResult {
  operationId: string; status: string; buttonId?: string | null; reason?: string | null
  controls?: NativeUiControl[]; events?: NativeUiControlEvent[]; cursor?: number | null
}
