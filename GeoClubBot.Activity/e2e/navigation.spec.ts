import { expect, test } from '@playwright/test';
import { mockDashboard, mockMe, mockMemberTabs } from './fixtures';

test('switches between the member tabs', async ({ page }) => {
  await mockMe(page);
  await mockDashboard(page);
  await mockMemberTabs(page);
  await page.goto('/');

  // Overview is the landing tab.
  await expect(page.getByTestId('club-name')).toBeVisible();

  await page.getByTestId('tab-missions').click();
  await expect(page.getByTestId('missions-view')).toBeVisible();
  await expect(page.getByTestId('todays-xp-tile')).toContainText('51,230 XP');
  await expect(page.getByTestId('mission-stats-panel')).toContainText('62%');

  await page.getByTestId('tab-me').click();
  await expect(page.getByTestId('me-view')).toBeVisible();
  await expect(page.getByTestId('profile-panel')).toContainText('Gold II');
  await expect(page.getByTestId('my-activity-panel')).toContainText('4,200 XP');
  // The seven days of baseWeekActivity: both awards on 2, exactly one on 4, neither on 1.
  const dayStrip = page.getByTestId('day-strip');
  await expect(dayStrip.locator('.day-cell')).toHaveCount(7);
  await expect(dayStrip.locator('.day-cell.done')).toHaveCount(2);
  await expect(dayStrip.locator('.day-cell.partial')).toHaveCount(4);
  await expect(dayStrip.locator('.day-cell:not(.done):not(.partial)')).toHaveCount(1);

  await page.getByTestId('tab-overview').click();
  await expect(page.getByTestId('club-name')).toBeVisible();
});

test('the me tab shows the not-linked state for an unlinked viewer', async ({ page }) => {
  await mockMe(page, {
    discordUserId: '42',
    isAdmin: false,
    linked: null,
    club: null,
    openLinkRequest: null,
  });
  await mockDashboard(page, {
    club: null,
    viewer: null,
    leaderboard: [],
    challenges: [],
    streaks: [],
  });

  await page.goto('/');
  await page.getByTestId('tab-me').click();

  await expect(page.getByTestId('link-account-panel')).toBeVisible();
  await expect(page.getByTestId('profile-panel')).toHaveCount(0);
});

test('overview data survives a tab round-trip without refetching eagerly', async ({ page }) => {
  let dashboardCalls = 0;
  await mockMe(page);
  await page.route('**/api/v1/activity/dashboard**', (route) => {
    dashboardCalls += 1;
    return route.fulfill({
      json: {
        club: { name: 'Globetrotters', level: 12 },
        viewer: null,
        leaderboard: [],
        challenges: [],
        streaks: [],
      },
    });
  });

  await page.goto('/');
  await expect(page.getByTestId('club-name')).toBeVisible();
  const before = dashboardCalls;

  await page.getByTestId('tab-missions').click();
  await expect(page.getByTestId('missions-view')).toBeVisible();
  await page.getByTestId('tab-overview').click();

  // Returning refreshes silently (one background call), it does not refetch with a spinner.
  await expect(page.getByTestId('club-name')).toBeVisible();
  await expect.poll(() => dashboardCalls).toBe(before + 1);
});
