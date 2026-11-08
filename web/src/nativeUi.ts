export const nativeUiKinds = ['messageBox', 'notification', 'announcement.show', 'announcement.close', 'chat'] as const
export type NativeUiKind = typeof nativeUiKinds[number]
export interface NativeUiButton { id: string; label: string }
export interface NativeUiTextRun { text: string; color: string }
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
}
export interface NativeUiCapability { kind: NativeUiKind; supported: boolean; ready: boolean; features: string[]; reason?: string | null }
export interface NativeUiCapabilities { build: string; operations: NativeUiCapability[] }
export interface NativeUiResult { operationId: string; status: string; buttonId?: string | null; reason?: string | null }
