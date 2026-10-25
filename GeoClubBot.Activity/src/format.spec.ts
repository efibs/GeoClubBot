import { describe, expect, it } from 'vitest';
import {
  defaultHistoryDepth,
  depthOptions,
  formatHours,
  formatMissionCompletion,
  formatStreak,
  formatXp,
  rankBadge,
  streakFlames,
  toDateInputValue,
} from './format';

describe('format', () => {
  it('shows the two largest units of a duration', () => {
    expect(formatHours(0)).toBe('<1m');
    expect(formatHours(0.5)).toBe('30m');
    expect(formatHours(4.2)).toBe('4h 12m');
    expect(formatHours(5)).toBe('5h');
    expect(formatHours(51)).toBe('2d 3h');
    expect(formatHours(48.9)).toBe('2d');
  });

  it('describes mission completion with unfinished claims apart from the average', () => {
    expect(
      formatMissionCompletion({
        completedCount: 5,
        averageCompletionHours: 28,
        abandonedCount: 1,
        openClaimHours: 51,
      }),
    ).toBe('avg 1d 4h over 5 missions · 1 never finished · open claim held 2d 3h');
    expect(
      formatMissionCompletion({
        completedCount: 0,
        averageCompletionHours: null,
        abandonedCount: 0,
        openClaimHours: 2,
      }),
    ).toBe('no claimed mission finished · open claim held 2h');
  });

  it('rounds and separates XP values', () => {
    expect(formatXp(1234.6)).toBe('1,235 XP');
    expect(formatXp(0)).toBe('0 XP');
  });

  it('badges medals for the top three and falls back to #N', () => {
    expect(rankBadge(1)).toBe('🥇');
    expect(rankBadge(2)).toBe('🥈');
    expect(rankBadge(3)).toBe('🥉');
    expect(rankBadge(4)).toBe('#4');
  });

  it('scales flames with the streak length', () => {
    expect(streakFlames(0)).toBe('');
    expect(streakFlames(1)).toBe('🔥');
    expect(streakFlames(7)).toBe('🔥');
    expect(streakFlames(8)).toBe('🔥🔥');
    expect(streakFlames(50)).toBe('🔥🔥🔥');
  });

  it('pluralizes day counts', () => {
    expect(formatStreak(1)).toBe('1 day');
    expect(formatStreak(2)).toBe('2 days');
  });

  it('defaults the depth to the first option', () => {
    expect(defaultHistoryDepth).toBe(depthOptions[0].value);
  });

  it('extracts the local calendar date for date inputs', () => {
    // Built and read back in local time, so the round-trip is timezone-independent.
    const local = new Date(2026, 6, 1, 12, 0, 0); // 2026-07-01, local noon
    expect(toDateInputValue(local.toISOString())).toBe('2026-07-01');
  });
});
