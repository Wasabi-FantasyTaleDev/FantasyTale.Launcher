import { readFileSync } from 'node:fs'
import { runInNewContext } from 'node:vm'
import ts from 'typescript'
import * as versions from '../src/store/versions.ts'

const module = { exports: {} }
runInNewContext(ts.transpileModule(readFileSync(new URL('../src/store/updates.ts', import.meta.url), 'utf8'), {
  compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 },
}).outputText, { module, exports: module.exports, require: () => versions })
export const updates = module.exports

const groupsModule = { exports: {} }
runInNewContext(ts.transpileModule(readFileSync(new URL('../src/store/groups.ts', import.meta.url), 'utf8'), {
  compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 },
}).outputText, { module: groupsModule, exports: groupsModule.exports, require: () => versions })
export const groups = groupsModule.exports
