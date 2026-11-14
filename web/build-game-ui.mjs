import { fileURLToPath } from 'node:url'
import { copyFile, readdir, rm } from 'node:fs/promises'
import { build } from 'vite'
import vue from '@vitejs/plugin-vue'

const root = fileURLToPath(new URL('../tools/game-ui/', import.meta.url))
await build({
  configFile: false,
  root,
  plugins: [vue()],
  // The library sources live outside web/, so its packages resolve from web/node_modules.
  resolve: { alias: { '@zkl2333/freetype-wasm': fileURLToPath(new URL('./node_modules/@zkl2333/freetype-wasm', import.meta.url)) } },
  publicDir: false,
  build: {
    lib: { entry: fileURLToPath(new URL('../tools/game-ui/src/index.ts', import.meta.url)), formats: ['es'], fileName: 'game-ui', cssFileName: 'game-ui' },
    rollupOptions: { external: ['vue'] },
  },
})
// Lazy chunks (the FreeType engine) are loaded relative to game-ui.js, so every emitted file ships beside it.
const target = new URL('../src/FantasyTale.Launcher.Overlay/overlay/ui-library/', import.meta.url)
for (const name of await readdir(target))
  if (name !== 'vue.esm-browser.prod.js' && /\.(js|css|wasm)$/.test(name)) await rm(new URL(name, target))
for (const name of await readdir(new URL('../tools/game-ui/dist/', import.meta.url)))
  await copyFile(new URL(`../tools/game-ui/dist/${name}`, import.meta.url), new URL(name, target))
