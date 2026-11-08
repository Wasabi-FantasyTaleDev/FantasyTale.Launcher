import { execFileSync } from 'node:child_process'
import { fileURLToPath } from 'node:url'

const parser = fileURLToPath(new URL('../../../tools/web-preview/bin/Release/net10.0/FantasyTale.WebPreview.dll', import.meta.url))
export function parse(text) {
  // The production frontend build prepares this backend parser before parallel test workers run.
  return JSON.parse(execFileSync('dotnet', [parser, '--stdin'], { input: text, encoding: 'utf8' }))
}
