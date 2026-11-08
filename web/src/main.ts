import { createApp } from 'vue'
import App from './App.vue'
import { router } from './router'
import { i18n } from './i18n'
import './theme'
import './styles/tokens.css'
import './styles/app.css'

const app = createApp(App)
app.use(router)
app.use(i18n)
app.mount('#app')

// A native window has no browser context menu, text selection outside inputs, or file drop.
document.addEventListener('contextmenu', (e) => e.preventDefault())
document.addEventListener('dragover', (e) => e.preventDefault())
document.addEventListener('drop', (e) => e.preventDefault())
