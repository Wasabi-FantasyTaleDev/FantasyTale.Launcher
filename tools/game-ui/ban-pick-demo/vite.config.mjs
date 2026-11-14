import { fileURLToPath, pathToFileURL } from 'node:url';
import { createRequire } from 'node:module';

const path = relative => fileURLToPath(new URL(relative, import.meta.url));
const require = createRequire(new URL('../../../web/package.json', import.meta.url));
const vue = (await import(pathToFileURL(require.resolve('@vitejs/plugin-vue')).href)).default;
export default {
  root: path('./'),
  base: './',
  plugins: [vue(), {
    name: 'ban-pick-resource-refresh',
    configureServer(server) {
      const resource = path('../../../artifacts/ban-pick-demo/resources/ui.json').replaceAll('\\', '/');
      const refresh = (event, file) => {
        if ((event === 'change' || event === 'add') && file.replaceAll('\\', '/') === resource) {
          server.ws.send({type:'custom', event:'ban-pick:resources-ready', data:{}});
        }
      };
      server.watcher.add(resource);
      server.watcher.on('all', refresh);
      server.httpServer?.once('close', () => server.watcher.off('all', refresh));
    },
  }],
  publicDir: path('../../../artifacts/ban-pick-demo/resources/'),
  resolve: { alias: {
    vue: path('../../../web/node_modules/vue/dist/vue.runtime.esm-bundler.js'),
    'vue-i18n': path('../../../web/node_modules/vue-i18n/dist/vue-i18n.esm-bundler.js'),
  } },
  server: { host: '127.0.0.1', port: 4187, strictPort: true, fs: { allow: [path('../../../')] } },
  build: { outDir: path('../../../artifacts/ban-pick-demo/dist/'), emptyOutDir: true },
};
