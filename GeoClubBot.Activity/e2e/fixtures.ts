import type { Page } from '@playwright/test';

/** A linked club member without admin rights — the default viewer for e2e scenarios. */
export const memberMe = {
  discordUserId: '42',
  isAdmin: false,
  linked: { geoGuessrUserId: 'gg-1', nickname: 'You' },
  club: { name: 'Globetrotters', level: 12 },
  openLinkRequest: null,
};

/** The same viewer with the admin flag — used to exercise the admin tab/area. */
export const adminMe = { ...memberMe, isAdmin: true };

export const baseDashboard = {
  club: { name: 'Globetrotters', level: 12 },
  viewer: { nickname: 'You' },
  leaderboard: [
    { rank: 1, nickname: 'Ada', averageXp: 1500 },
    { rank: 2, nickname: 'You', averageXp: 1400 },
  ],
  challenges: [
    {
      difficulty: 'Hard',
      players: [{ rank: 1, nickname: 'Ada', totalScore: '24000 points', totalDistance: '12km' }],
    },
  ],
  streaks: [{ nickname: 'You', currentStreak: 9, longestStreak: 30 }],
};

export const baseMissionStats = {
  clubName: 'Globetrotters',
  fromDay: '2026-06-05',
  toDay: '2026-07-04',
  daysWithMissionData: 12,
  totalMissionAppearances: 36,
  averageDayCompletionRate: 0.62,
  averageDayChallengeRate: 0.5,
  daysWithChallengeData: 6,
  challengeTrackedFrom: '2026-06-29',
  kinds: [
    {
      type: 'DailyChallenge',
      gameMode: 'Standard',
      appearanceCount: 12,
      appearanceDayShare: 1,
      averageTargetProgress: 3,
      lastAppearance: '2026-07-04',
      averageDayCompletionRateWhenPresent: 0.62,
    },
  ],
};

export const baseProfile = {
  nickname: 'You',
  countryCode: 'de',
  createdAt: '2020-05-01T00:00:00Z',
  isProUser: true,
  level: 87,
  profileUrl: '/user/gg-1',
  ranked: { rating: 1200, divisionName: 'Gold II', tier: 'Gold', peakRating: 1350 },
};

export const baseWeekActivity = {
  totalXp: 4200,
  // A "done" day needs both awards; 28th/1st/4th have only the mission, so 2 full days.
  numDaysDone: 2,
  numMissionDaysDone: 5,
  numChallengeDaysDone: 3,
  joinedThisWeek: false,
  joinedAt: '2024-01-01T00:00:00Z',
  days: [
    { date: '2026-06-28', missionCompleted: true, challengeCompleted: false },
    { date: '2026-06-29', missionCompleted: true, challengeCompleted: true },
    { date: '2026-06-30', missionCompleted: false, challengeCompleted: false },
    { date: '2026-07-01', missionCompleted: true, challengeCompleted: false },
    { date: '2026-07-02', missionCompleted: true, challengeCompleted: true },
    { date: '2026-07-03', missionCompleted: false, challengeCompleted: true },
    { date: '2026-07-04', missionCompleted: true, challengeCompleted: false },
  ],
};

export async function mockMe(page: Page, me: unknown = memberMe): Promise<void> {
  await page.route('**/api/v1/activity/me', (route) => route.fulfill({ json: me as object }));
}

export async function mockDashboard(page: Page, json: unknown = baseDashboard): Promise<void> {
  await page.route('**/api/v1/activity/dashboard**', (route) =>
    route.fulfill({ json: json as object }),
  );
}

/** Mocks the admin read endpoints with one strike, one excuse, and one open link request. */
export async function mockAdminArea(page: Page): Promise<void> {
  await page.route('**/api/v1/activity/admin/last-check-time', (route) =>
    route.fulfill({ json: { lastCheckTime: '2026-07-04T06:00:00Z' } }),
  );
  await page.route('**/api/v1/activity/admin/strikes', (route) =>
    route.fulfill({
      json: [
        {
          strikeId: '11111111-1111-1111-1111-111111111111',
          nickname: 'Ada',
          timestamp: '2026-06-20T00:00:00Z',
          revoked: false,
          expiresAt: '2026-09-20T00:00:00Z',
        },
      ],
    }),
  );
  await page.route('**/api/v1/activity/admin/strikes/relevant', (route) =>
    route.fulfill({ json: [{ nickname: 'Ada', numActiveStrikes: 1 }] }),
  );
  await page.route('**/api/v1/activity/admin/excuses**', (route) =>
    route.fulfill({
      json: [
        {
          excuseId: '22222222-2222-2222-2222-222222222222',
          nickname: 'Ada',
          from: '2026-07-01T00:00:00Z',
          to: '2026-07-10T00:00:00Z',
        },
      ],
    }),
  );
  await page.route('**/api/v1/activity/admin/link-requests', (route) =>
    route.fulfill({ json: [{ discordUserId: '77', geoGuessrUserId: 'a'.repeat(24) }] }),
  );
  await page.route('**/api/v1/activity/admin/club/statistics', (route) =>
    route.fulfill({ status: 404, json: { title: 'Not found' } }),
  );
}

/** Mocks everything the missions + profile tabs fetch. */
export async function mockMemberTabs(page: Page): Promise<void> {
  await page.route('**/api/v1/activity/missions/stats**', (route) =>
    route.fulfill({ json: baseMissionStats }),
  );
  await page.route('**/api/v1/activity/club/todays-xp**', (route) =>
    route.fulfill({ json: { xp: 51230, clubName: 'Globetrotters' } }),
  );
  await page.route('**/api/v1/activity/me/profile', (route) =>
    route.fulfill({ json: baseProfile }),
  );
  await page.route('**/api/v1/activity/me/activity**', (route) =>
    route.fulfill({ json: baseWeekActivity }),
  );
}
