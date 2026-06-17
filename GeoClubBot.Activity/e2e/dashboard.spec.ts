import { expect, test, type Page } from '@playwright/test';

const baseDashboard = {
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

const emptyDashboard = {
  club: { name: 'Empty Club', level: 1 },
  viewer: null,
  leaderboard: [],
  challenges: [],
  streaks: [],
};

async function mockDashboard(page: Page, json: unknown): Promise<void> {
  await page.route('**/api/v1/activity/dashboard**', (route) => route.fulfill({ json: json as object }));
}

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

test('shows empty states and no highlight for an unlinked viewer', async ({ page }) => {
  await mockDashboard(page, emptyDashboard);
  await page.goto('/');

  await expect(page.getByTestId('leaderboard-empty')).toBeVisible();
  await expect(page.getByTestId('challenge-empty')).toBeVisible();
  await expect(page.getByTestId('streaks-empty')).toBeVisible();
  await expect(page.getByTestId('viewer-row')).toHaveCount(0);
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
