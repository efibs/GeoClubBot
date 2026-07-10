import { describe, expect, it, vi } from 'vitest';
import { mount } from '@vue/test-utils';
import { createMemoryHistory, createRouter } from 'vue-router';
import TabNav from './TabNav.vue';
import { queryPlugin, testQueryClient } from '../test/query';
import type { MeDto } from '../types';

// The session query only fetches when the cache is empty (the "not loaded" case); stub it so no real
// request is attempted. Seeded cases short-circuit on the pre-seeded cache.
vi.mock('../api', () => ({ fetchMe: vi.fn(() => Promise.reject(new Error('no session'))) }));

const stub = { template: '<div />' };

const baseMe: MeDto = {
  discordUserId: '42',
  isAdmin: false,
  linked: null,
  club: null,
  openLinkRequest: null,
};

function mountTabNav(me: MeDto | null | undefined) {
  const client = testQueryClient(me);
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/', component: stub },
      { path: '/missions', component: stub },
      { path: '/me', component: stub },
      { path: '/admin', component: stub },
    ],
  });

  return mount(TabNav, { global: { plugins: [queryPlugin(client), router] } });
}

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
    const wrapper = mountTabNav(undefined);

    expect(wrapper.find('[data-testid="tab-admin"]').exists()).toBe(false);
  });
});
