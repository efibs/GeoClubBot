import { describe, expect, it } from 'vitest';
import { mount } from '@vue/test-utils';
import { createPinia } from 'pinia';
import { createMemoryHistory, createRouter } from 'vue-router';
import TabNav from './TabNav.vue';
import { useSessionStore } from '../stores/session';
import type { MeDto } from '../types';

const stub = { template: '<div />' };

function mountTabNav(me: MeDto | null) {
  const pinia = createPinia();
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/', component: stub },
      { path: '/missions', component: stub },
      { path: '/me', component: stub },
      { path: '/admin', component: stub },
    ],
  });

  const wrapper = mount(TabNav, { global: { plugins: [pinia, router] } });
  const session = useSessionStore();
  session.me = me;
  return wrapper;
}

const baseMe: MeDto = {
  discordUserId: '42',
  isAdmin: false,
  linked: null,
  club: null,
  openLinkRequest: null,
};

describe('TabNav', () => {
  it('hides the admin tab for regular members', async () => {
    const wrapper = mountTabNav(baseMe);
    await wrapper.vm.$nextTick();

    expect(wrapper.find('[data-testid="tab-overview"]').exists()).toBe(true);
    expect(wrapper.find('[data-testid="tab-missions"]').exists()).toBe(true);
    expect(wrapper.find('[data-testid="tab-me"]').exists()).toBe(true);
    expect(wrapper.find('[data-testid="tab-admin"]').exists()).toBe(false);
  });

  it('shows the admin tab for admins', async () => {
    const wrapper = mountTabNav({ ...baseMe, isAdmin: true });
    await wrapper.vm.$nextTick();

    expect(wrapper.find('[data-testid="tab-admin"]').exists()).toBe(true);
  });

  it('hides the admin tab before the session has loaded', () => {
    const wrapper = mountTabNav(null);

    expect(wrapper.find('[data-testid="tab-admin"]').exists()).toBe(false);
  });
});
