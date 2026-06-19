import { describe, expect, it } from 'vitest';
import { mount } from '@vue/test-utils';
import ChallengePanel from './ChallengePanel.vue';

describe('ChallengePanel', () => {
  it('shows the empty state with no active challenges', () => {
    const wrapper = mount(ChallengePanel, { props: { challenges: [], viewerNickname: null } });

    expect(wrapper.find('[data-testid="challenge-empty"]').exists()).toBe(true);
  });

  it('renders each difficulty with its players and highlights the viewer', () => {
    const challenges = [
      {
        difficulty: 'Easy',
        players: [{ rank: 1, nickname: 'Alice', totalScore: '5000 points', totalDistance: '10km' }],
      },
    ];

    const wrapper = mount(ChallengePanel, { props: { challenges, viewerNickname: 'Alice' } });

    expect(wrapper.text()).toContain('Easy');
    expect(wrapper.text()).toContain('Alice');
    expect(wrapper.text()).toContain('5000 points');
    expect(wrapper.find('.is-viewer').exists()).toBe(true);
  });
});
