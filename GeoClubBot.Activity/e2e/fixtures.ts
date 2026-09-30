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

function tile(state: 'free' | 'claimed' | 'completed', index: number, extra: object = {}) {
  return {
    missionId: `00000000-0000-0000-0000-00000000000${index}`,
    title: 'Win 2 Ranked Duels',
    currentProgress: state === 'completed' ? 2 : 0,
    targetProgress: 2,
    state,
    claimerNickname: state === 'free' ? null : 'Ada',
    claimedAt: state === 'free' ? null : '2026-07-03T12:00:00Z',
    helpRequested: false,
    helperCount: 0,
    claimedByViewer: false,
    helpedByViewer: false,
    ...extra,
  };
}

/** Board 1 of 2 in progress: one mission done, the viewer's open claim with help requested, two free. */
export const baseMissionBoard = {
  clubName: 'Globetrotters',
  periodStart: '2026-07-01T11:00:00Z',
  periodEnd: '2026-07-08T11:00:00Z',
  currentBoardNumber: 1,
  allBoardsCleared: false,
  claimCycleEnd: '2026-07-05T11:00:00Z',
  boards: [
    {
      number: 1,
      size: 2,
      completedCount: 1,
      clearedAt: null,
      tiles: [
        tile('completed', 1),
        tile('claimed', 2, { claimerNickname: 'You', claimedByViewer: true, helpRequested: true }),
        tile('free', 3),
        tile('free', 4),
      ],
    },
    { number: 2, size: 1, completedCount: 0, clearedAt: null, tiles: [tile('free', 5)] },
  ],
  viewer: {
    claimState: 'HoldingOpenMission',
    claimsThisWeek: 2,
    completedThisWeek: 1,
    helpedThisWeek: 3,
  },
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
  ruleXp: null,
  // The streak was kept on 5 of the 7 days; missions on the 29th (2) and the 2nd (1).
  numChallengeDaysDone: 5,
  boardMissions: 3,
  boardClearBonusXp: 100,
  joinedInPeriod: false,
  joinedAt: '2024-01-01T00:00:00Z',
  periodStart: '2026-06-28T00:00:00Z',
  days: [
    { date: '2026-06-28', challengeDone: true, boardMissions: 0, xp: 20 },
    { date: '2026-06-29', challengeDone: true, boardMissions: 2, xp: 60 },
    { date: '2026-06-30', challengeDone: false, boardMissions: 0, xp: 0 },
    { date: '2026-07-01', challengeDone: true, boardMissions: 0, xp: 20 },
    { date: '2026-07-02', challengeDone: true, boardMissions: 1, xp: 140 },
    { date: '2026-07-03', challengeDone: false, boardMissions: 0, xp: 0 },
    { date: '2026-07-04', challengeDone: true, boardMissions: 0, xp: 20 },
  ],
  requirements: [],
  helpedThisWeek: 3,
  helpedLastWeek: 1,
};

/** The current check period: the streak requirement met, one of two missions still missing. */
export const baseCurrentActivity = {
  ...baseWeekActivity,
  ruleXp: 120,
  requirements: [
    { label: 'streak', actual: 6, target: 6, met: true },
    { label: 'missions', actual: 1, target: 2, met: false },
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
  await page.route('**/api/v1/activity/club/board', (route) =>
    route.fulfill({ json: baseMissionBoard }),
  );
  await page.route('**/api/v1/activity/club/todays-xp**', (route) =>
    route.fulfill({
      json: {
        xp: 51230,
        clubName: 'Globetrotters',
        challengeMemberCount: 24,
        boardMissionCount: 17,
        claimMemberCount: 21,
        totalMemberCount: 30,
      },
    }),
  );
  await page.route('**/api/v1/activity/me/profile', (route) =>
    route.fulfill({ json: baseProfile }),
  );
  await page.route('**/api/v1/activity/me/activity**', (route) =>
    route.fulfill({ json: baseWeekActivity }),
  );
  // Registered last so it wins over the broader glob above (Playwright tries the newest route first).
  await page.route('**/api/v1/activity/me/activity/current', (route) =>
    route.fulfill({ json: baseCurrentActivity }),
  );
}
