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

  // Regression: a long nickname used to squeeze the flames + day count onto extra lines because the
  // name sat in a content-sized grid column. The row must opt into the rank-less layout that makes
  // the *name* the flexible column, keeping the value cell (flames + days) intact on one line.
  it('keeps the flame/day count together for a very long nickname', () => {
    const longName = 'Averyveryverylongnicknamethatwouldoverflowtherow';
    const streaks = [{ nickname: longName, currentStreak: 8, longestStreak: 8 }];

    const wrapper = mount(StreaksPanel, { props: { streaks, viewerNickname: null } });

    const row = wrapper.find('.row');
    // The row uses the rank-less layout where the name column flexes and truncates.
    expect(row.classes()).toContain('row-no-rank');
    // The full name is still rendered (truncation is visual, via CSS ellipsis).
    expect(row.find('.name').text()).toBe(longName);
    // The flames and day count stay in a single value cell.
    const value = row.find('.value');
    expect(value.text()).toContain('🔥');
    expect(value.text()).toContain('8 days');
  });
});
