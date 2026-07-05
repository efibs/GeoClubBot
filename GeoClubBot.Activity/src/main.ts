import { createApp } from 'vue';
import { VueQueryPlugin } from '@tanstack/vue-query';
import App from './App.vue';
import { router } from './router';
import { queryClient } from './queryClient';
import './styles/tokens.css';
import './styles/base.css';
import './styles/layout.css';
import './styles/rows.css';

createApp(App).use(VueQueryPlugin, { queryClient }).use(router).mount('#app');
