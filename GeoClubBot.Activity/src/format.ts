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

/** Turns an ISO country code into its flag emoji; falls back to a globe for odd values. */
export function countryFlag(countryCode: string): string {
  const code = countryCode.trim().toUpperCase();
  if (!/^[A-Z]{2}$/.test(code)) {
    return '🌍';
  }
  return String.fromCodePoint(...[...code].map((char) => 0x1f1e6 + char.charCodeAt(0) - 65));
}

/** Formats a 0..1 rate as a whole percent, with an em dash for missing values. */
export function formatPercent(rate: number | null | undefined): string {
  return rate == null ? '—' : `${Math.round(rate * 100)}%`;
}

/** The narrow weekday label ("M", "T", …) of an ISO date, computed in UTC like the backend. */
export function weekdayInitial(isoDate: string): string {
  return new Date(`${isoDate}T00:00:00Z`).toLocaleDateString('en-US', {
    weekday: 'narrow',
    timeZone: 'UTC',
  });
}

/** A compact date like "Jul 4, 2026" from an ISO timestamp. */
export function formatDate(iso: string): string {
  return new Date(iso).toLocaleDateString('en-US', { year: 'numeric', month: 'short', day: 'numeric' });
}

/**
 * The local calendar date ("yyyy-MM-dd") of an ISO timestamp, for `<input type="date">`. Uses the
 * same local time zone as {@link formatDate}, so a value edited in a form matches the one displayed
 * in the list — unlike `iso.slice(0, 10)`, which reads the UTC date and is a day off for instants
 * stored at local midnight.
 */
export function toDateInputValue(iso: string): string {
  const date = new Date(iso);
  const year = date.getFullYear();
  const month = String(date.getMonth() + 1).padStart(2, '0');
  const day = String(date.getDate()).padStart(2, '0');
  return `${year}-${month}-${day}`;
}
