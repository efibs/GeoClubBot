import { describe, expect, it } from 'vitest';
import { mount } from '@vue/test-utils';
import StreaksPanel from './StreaksPanel.vue';

describe('StreaksPanel', () => {
  it('renders streaks with flames, current and best', () => {
    const streaks = [{ nickname: 'Alice', currentStreak: 8, longestStreak: 20 }];

    const wrapper = mount(StreaksPanel, { props: { streaks, viewerNickname: 'Alice' } });

    expect(wrapper.text()).toContain('Alice');
    expect(wrapper.text()).toContain('🔥');
    expect(wrapper.text()).toContain('best 20 days');
    expect(wrapper.find('.is-viewer').exists()).toBe(true);
  });

  it('shows the empty state', () => {
    const wrapper = mount(StreaksPanel, { props: { streaks: [], viewerNickname: null } });

    expect(wrapper.find('[data-testid="streaks-empty"]').exists()).toBe(true);
  });
});
