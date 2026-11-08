import { fileURLToPath } from 'node:url'
import { copyFile } from 'node:fs/promises'
import { build } from 'vite'
import vue from '@vitejs/plugin-vue'

const root = fileURLToPath(new URL('../tools/game-ui/', import.meta.url))
await build({
  configFile: false,
  root,
  plugins: [vue()],
  publicDir: false,
  build: {
    lib: { entry: fileURLToPath(new URL('../tools/game-ui/src/index.ts', import.meta.url)), formats: ['es'], fileName: 'game-ui', cssFileName: 'game-ui' },
    rollupOptions: { external: ['vue'] },
  },
})
for (const name of ['game-ui.js', 'game-ui.css']) {
  await copyFile(new URL(`../tools/game-ui/dist/${name}`, import.meta.url),
    new URL(`../src/FantasyTale.Launcher.Overlay/overlay/ui-library/${name}`, import.meta.url))
}
