import {fileURLToPath,pathToFileURL} from 'node:url';
import {createRequire} from 'node:module';
import {resolve} from 'node:path';
const path=value=>fileURLToPath(new URL(value,import.meta.url));
const web=process.env.LAUNCHER_WEB_ROOT||path('../../../web');
const require=createRequire(resolve(web,'package.json'));
const vue=(await import(pathToFileURL(require.resolve('@vitejs/plugin-vue')).href)).default;
const resources=process.env.BF_UI_RESOURCE_ROOT;
if(!resources)throw new Error('Set BF_UI_RESOURCE_ROOT to the private prepared mode resource folder.');
export default {
  root:path('./'),base:'./',plugins:[vue()],publicDir:resources,
  resolve:{alias:{vue:resolve(web,'node_modules/vue/dist/vue.runtime.esm-bundler.js'),
    '@zkl2333/freetype-wasm':resolve(web,'node_modules/@zkl2333/freetype-wasm')}},
  server:{host:'127.0.0.1',port:4191,strictPort:true,fs:{allow:[path('../../../'),web,resources]}},
  build:{outDir:process.env.BF_UI_DEMO_OUTPUT||path('../../../artifacts/mode-extension-demo/dist'),emptyOutDir:true},
};
