import { expect, test } from '@playwright/test';
import { baseDashboard, mockDashboard, mockMe } from './fixtures';

// The shell always fetches /me after the handshake; keep it mocked so the overview scenarios below
// stay focused on the dashboard payload.
test.beforeEach(async ({ page }) => {
  await mockMe(page);
});

// In a club, but nothing to show yet (e.g. a brand-new club): panels render their empty states.
const emptyClubDashboard = {
  club: { name: 'Empty Club', level: 1 },
  viewer: null,
  leaderboard: [],
  challenges: [],
  streaks: [],
};

// Viewer can't be tied to a club (unlinked / not a member): club panels are suppressed, but the
// club-independent daily challenge is still shown to everyone.
const noClubDashboard = {
  club: null,
  viewer: null,
  leaderboard: [],
  challenges: [
    {
      difficulty: 'Hard',
      players: [{ rank: 1, nickname: 'Ada', totalScore: '24000 points', totalDistance: '12km' }],
    },
  ],
  streaks: [],
};

test('renders the three panels and highlights the viewer', async ({ page }) => {
  await mockDashboard(page, baseDashboard);
  await page.goto('/');

  await expect(page.getByTestId('club-name')).toHaveText('Globetrotters');
  await expect(page.getByTestId('club-level')).toContainText('12');
  await expect(page.getByTestId('leaderboard-panel')).toContainText('Ada');
  await expect(page.getByTestId('challenge-panel')).toContainText('Hard');
  await expect(page.getByTestId('streaks-panel')).toContainText('best 30 days');
  await expect(page.getByTestId('viewer-row')).toContainText('You');
});

test('shows panel empty states and no highlight when the club has no data yet', async ({
  page,
}) => {
  await mockDashboard(page, emptyClubDashboard);
  await page.goto('/');

  await expect(page.getByTestId('club-name')).toHaveText('Empty Club');
  await expect(page.getByTestId('leaderboard-empty')).toBeVisible();
  await expect(page.getByTestId('challenge-empty')).toBeVisible();
  await expect(page.getByTestId('streaks-empty')).toBeVisible();
  await expect(page.getByTestId('viewer-row')).toHaveCount(0);
});

test('hides club panels but still shows the daily challenge when the viewer has no club', async ({
  page,
}) => {
  await mockDashboard(page, noClubDashboard);
  await page.goto('/');

  // The club-independent challenge is shown to everyone.
  await expect(page.getByTestId('challenge-panel')).toContainText('Hard');
  // Club-scoped panels are suppressed, with an explanatory note in their place.
  await expect(page.getByTestId('no-club')).toBeVisible();
  await expect(page.getByTestId('leaderboard-panel')).toHaveCount(0);
  await expect(page.getByTestId('streaks-panel')).toHaveCount(0);
});

test('switching the period refetches with the new history depth', async ({ page }) => {
  let lastDepth = '';
  await page.route('**/api/v1/activity/dashboard**', (route) => {
    lastDepth = new URL(route.request().url()).searchParams.get('historyDepth') ?? '';
    return route.fulfill({ json: baseDashboard });
  });

  await page.goto('/');
  await expect(page.getByTestId('club-name')).toBeVisible();

  await page.locator('.period-button').nth(1).click();

  await expect.poll(() => lastDepth).toBe('8');
});

test('manual refresh refetches the dashboard', async ({ page }) => {
  let calls = 0;
  await page.route('**/api/v1/activity/dashboard**', (route) => {
    calls += 1;
    return route.fulfill({ json: baseDashboard });
  });

  await page.goto('/');
  await expect(page.getByTestId('club-name')).toBeVisible();

  const before = calls;
  await page.getByTestId('refresh-button').click();

  await expect.poll(() => calls).toBeGreaterThan(before);
});
