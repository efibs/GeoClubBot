import { beforeEach, describe, expect, it, vi } from 'vitest';
import { mount } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import LinkAccountPanel from './LinkAccountPanel.vue';
import { useSessionStore } from '../stores/session';
import { startLinkRequest } from '../api';
import type { MeDto } from '../types';

vi.mock('../api', () => ({
  fetchMe: vi.fn(),
  startLinkRequest: vi.fn(),
  cancelLinkRequest: vi.fn(),
}));

const mockedStart = vi.mocked(startLinkRequest);

const unlinkedMe: MeDto = {
  discordUserId: '42',
  isAdmin: false,
  linked: null,
  club: null,
  openLinkRequest: null,
};

function mountPanel(me: MeDto) {
  const pinia = createPinia();
  setActivePinia(pinia);
  const session = useSessionStore();
  session.me = me;
  return mount(LinkAccountPanel, { global: { plugins: [pinia] } });
}

describe('LinkAccountPanel', () => {
  beforeEach(() => {
    mockedStart.mockReset();
  });

  it('shows the start form when there is no open request', () => {
    const wrapper = mountPanel(unlinkedMe);

    expect(wrapper.find('[data-testid="link-profile-input"]').exists()).toBe(true);
    expect(wrapper.find('[data-testid="link-otp"]').exists()).toBe(false);
  });

  it('shows the OTP and cancel action for an open request', () => {
    const wrapper = mountPanel({
      ...unlinkedMe,
      openLinkRequest: { geoGuessrUserId: 'a'.repeat(24), oneTimePassword: 'otp-1234' },
    });

    expect(wrapper.find('[data-testid="link-otp"]').text()).toContain('otp-1234');
    expect(wrapper.find('[data-testid="link-cancel"]').exists()).toBe(true);
    expect(wrapper.find('[data-testid="link-profile-input"]').exists()).toBe(false);
  });

  it('parses the user id out of a profile share link and starts the request', async () => {
    const userId = '62c353a29d0d57e7b9a3383f';
    mockedStart.mockResolvedValue({ geoGuessrUserId: userId, oneTimePassword: 'otp' });
    const wrapper = mountPanel(unlinkedMe);

    await wrapper.find('[data-testid="link-profile-input"]').setValue(`https://www.geoguessr.com/user/${userId}`);
    await wrapper.find('form').trigger('submit');

    expect(mockedStart).toHaveBeenCalledWith(userId);
  });

  it('rejects a malformed profile link without calling the API', async () => {
    const wrapper = mountPanel(unlinkedMe);

    await wrapper.find('[data-testid="link-profile-input"]').setValue('https://example.com/not-a-profile');
    await wrapper.find('form').trigger('submit');

    expect(mockedStart).not.toHaveBeenCalled();
    expect(wrapper.find('[data-testid="link-error"]').exists()).toBe(true);
  });
});
