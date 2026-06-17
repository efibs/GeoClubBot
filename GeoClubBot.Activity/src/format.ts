/** Leaderboard depth presets. `historyDepth` is the number of recent XP intervals averaged. */
export interface DepthOption {
  label: string;
  value: number;
}

export const depthOptions: DepthOption[] = [
  { label: 'Recent', value: 4 },
  { label: 'Standard', value: 8 },
  { label: 'Extended', value: 14 },
];

export const defaultHistoryDepth = depthOptions[0].value;

/** Rounds and formats an average-XP value, e.g. `1,234 XP`. */
export function formatXp(value: number): string {
  return `${Math.round(value).toLocaleString('en-US')} XP`;
}

/** A medal for the top three ranks, otherwise `#N`. */
export function rankBadge(rank: number): string {
  switch (rank) {
    case 1:
      return '🥇';
    case 2:
      return '🥈';
    case 3:
      return '🥉';
    default:
      return `#${rank}`;
  }
}

/** A flame run scaled (loosely) to the streak length, for a bit of celebratory flair. */
export function streakFlames(currentStreak: number): string {
  if (currentStreak <= 0) {
    return '';
  }
  const count = Math.min(3, Math.ceil(currentStreak / 7));
  return '🔥'.repeat(count);
}

export function formatStreak(days: number): string {
  return `${days} ${days === 1 ? 'day' : 'days'}`;
}
