import { createApp } from 'vue';
import { createI18n } from 'vue-i18n';
import App from './App.vue';
import { localizedMessages } from './messages';

createApp(App).use(createI18n({ legacy: false, locale: 'zh-CN', fallbackLocale: 'en', messages: localizedMessages })).mount('#app');
