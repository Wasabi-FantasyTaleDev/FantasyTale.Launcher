/** Resolves imported image references through the host's existing resource origin, including desktop development mode. */
export function serverImageUrl(reference: string, assetBase?: string): string {
  return reference.startsWith('/server-media/') && assetBase ? new URL(reference, assetBase).href : reference
}
