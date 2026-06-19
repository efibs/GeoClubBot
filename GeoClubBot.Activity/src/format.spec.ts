import { describe, expect, it } from 'vitest';
import {
  defaultHistoryDepth,
  depthOptions,
  formatStreak,
  formatXp,
  rankBadge,
  streakFlames,
} from './format';

describe('format', () => {
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
});
