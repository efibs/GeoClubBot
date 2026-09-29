import { describe, expect, it } from 'vitest';
import { mount } from '@vue/test-utils';
import BoardPanel from './BoardPanel.vue';
import type { BoardTileDto, MissionBoardDto } from '../types';

function tile(overrides: Partial<BoardTileDto> = {}): BoardTileDto {
  return {
    missionId: crypto.randomUUID(),
    title: 'Win 2 Ranked Duels',
    currentProgress: 0,
    targetProgress: 2,
    state: 'free',
    claimerNickname: null,
    claimedAt: null,
    helpRequested: false,
    helperCount: 0,
    claimedByViewer: false,
    helpedByViewer: false,
    ...overrides,
  };
}

function board(overrides: Partial<MissionBoardDto> = {}): MissionBoardDto {
  return {
    clubName: 'Dragon',
    periodStart: '2026-09-23T11:00:00Z',
    periodEnd: '2026-09-30T11:00:00Z',
    currentBoardNumber: 2,
    allBoardsCleared: false,
    claimCycleEnd: '2026-09-29T11:00:00Z',
    boards: [
      {
        number: 1,
        size: 1,
        completedCount: 1,
        clearedAt: '2026-09-23T14:00:00Z',
        tiles: [tile({ state: 'completed', claimerNickname: 'Zoe' })],
      },
      {
        number: 2,
        size: 2,
        completedCount: 1,
        clearedAt: null,
        tiles: [
          tile({ state: 'completed' }),
          tile({
            state: 'claimed',
            claimerNickname: 'Alice',
            claimedAt: '2026-09-28T08:00:00Z',
            claimedByViewer: true,
          }),
          tile({
            state: 'claimed',
            claimerNickname: 'Bob',
            claimedAt: '2026-09-27T08:00:00Z',
            helpRequested: true,
            title: 'Do 2 5Ks',
          }),
          tile(),
        ],
      },
      { number: 3, size: 1, completedCount: 0, clearedAt: null, tiles: [tile()] },
    ],
    viewer: {
      claimState: 'HoldingOpenMission',
      claimsThisWeek: 3,
      completedThisWeek: 2,
      helpedThisWeek: 1,
    },
    ...overrides,
  };
}

describe('BoardPanel', () => {
  it('shows every board with its status and the current board as a grid', () => {
    const wrapper = mount(BoardPanel, { props: { board: board() } });

    expect(wrapper.find('[data-testid="board-1"]').classes()).toContain('cleared');
    expect(wrapper.find('[data-testid="board-2"]').classes()).toContain('current');
    expect(wrapper.find('[data-testid="board-3"]').classes()).toContain('locked');
    expect(wrapper.findAll('[data-testid="tile-grid"] .tile')).toHaveLength(4);
    expect(wrapper.findAll('[data-testid="tile-claimed"]')).toHaveLength(2);
  });

  it('lists open missions oldest first, with the help requests marked', () => {
    const wrapper = mount(BoardPanel, { props: { board: board() } });

    const rows = wrapper.findAll('[data-testid="open-claims"] .row');
    expect(rows.map((r) => r.find('.name').text())).toEqual([
      'Bob — Do 2 5Ks',
      'Alice — Win 2 Ranked Duels',
    ]);
    expect(rows[0].text()).toContain('🆘');
    expect(rows[1].classes()).toContain('is-viewer');
  });

  it("tells the viewer what they can do and how they've helped", () => {
    const wrapper = mount(BoardPanel, { props: { board: board() } });

    expect(wrapper.find('[data-testid="board-viewer-state"]').text()).toContain(
      'Finish your open mission',
    );
    expect(wrapper.text()).toContain('helped out on 1 (unverified)');
  });

  it('celebrates when every board is cleared', () => {
    const wrapper = mount(BoardPanel, {
      props: { board: board({ allBoardsCleared: true, currentBoardNumber: 6 }) },
    });

    expect(wrapper.find('[data-testid="board-all-cleared"]').exists()).toBe(true);
    expect(wrapper.find('[data-testid="tile-grid"]').exists()).toBe(false);
  });
});
