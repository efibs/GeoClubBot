import { describe, expect, it } from 'vitest';
import { mount } from '@vue/test-utils';
import LeaderboardPanel from './LeaderboardPanel.vue';

const entries = [
  { rank: 1, nickname: 'Alice', averageXp: 1200 },
  { rank: 2, nickname: 'Bob', averageXp: 1100 },
];

describe('LeaderboardPanel', () => {
  it('renders ranked rows in order with a medal for the top spot', () => {
    const wrapper = mount(LeaderboardPanel, { props: { entries, viewerNickname: null } });

    expect(wrapper.findAll('.name').map((n) => n.text())).toEqual(['Alice', 'Bob']);
    expect(wrapper.text()).toContain('🥇');
    expect(wrapper.text()).toContain('1,200 XP');
  });

  it('highlights the viewer row', () => {
    const wrapper = mount(LeaderboardPanel, { props: { entries, viewerNickname: 'Bob' } });

    const viewerRow = wrapper.find('[data-testid="viewer-row"]');
    expect(viewerRow.exists()).toBe(true);
    expect(viewerRow.text()).toContain('Bob');
  });

  it('shows the empty state when there are no entries', () => {
    const wrapper = mount(LeaderboardPanel, { props: { entries: [], viewerNickname: null } });

    expect(wrapper.find('[data-testid="leaderboard-empty"]').exists()).toBe(true);
  });
});
