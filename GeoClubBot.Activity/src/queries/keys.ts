/**
 * Central query-key factory. Keeping keys in one place keeps `useQuery` callers and the
 * `invalidateQueries` calls in mutations in sync, and documents the cache layout at a glance.
 */
export const queryKeys = {
  session: ['session'] as const,
  dashboard: (historyDepth: number) => ['dashboard', historyDepth] as const,
  missionStats: ['mission-stats'] as const,
  todaysXp: ['todays-xp'] as const,
  profile: ['profile'] as const,
  myActivity: ['my-activity'] as const,
  reminders: ['reminders'] as const,
  admin: {
    lastCheckTime: ['admin', 'last-check-time'] as const,
    strikes: ['admin', 'strikes'] as const,
    excuses: ['admin', 'excuses'] as const,
    linking: ['admin', 'linking'] as const,
    clubStats: ['admin', 'club-stats'] as const,
    lookup: (nickname: string) => ['admin', 'lookup', nickname] as const,
  },
} as const;
