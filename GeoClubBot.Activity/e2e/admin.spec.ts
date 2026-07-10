import { expect, test } from '@playwright/test';
import { adminMe, mockAdminArea, mockDashboard, mockMe } from './fixtures';

test('hides the admin tab from regular members and redirects direct navigation', async ({
  page,
}) => {
  await mockMe(page); // non-admin member
  await mockDashboard(page);

  await page.goto('/');
  await expect(page.getByTestId('tab-nav')).toBeVisible();
  await expect(page.getByTestId('tab-admin')).toHaveCount(0);

  // Deep-linking straight into the admin area must bounce back to the overview. (This is UX only —
  // the admin endpoints themselves enforce the permission server-side.)
  await page.goto('/#/admin');
  await expect(page.getByTestId('club-name')).toBeVisible();
  await expect(page.getByTestId('admin-view')).toHaveCount(0);
});

test('shows the admin area with strikes, excuses and linking sections', async ({ page }) => {
  await mockMe(page, adminMe);
  await mockDashboard(page);
  await mockAdminArea(page);

  await page.goto('/');
  await expect(page.getByTestId('tab-admin')).toBeVisible();

  // Strikes is the landing section.
  await page.getByTestId('tab-admin').click();
  await expect(page.getByTestId('admin-view')).toBeVisible();
  await expect(page.getByTestId('admin-strikes-view')).toBeVisible();
  await expect(page.getByTestId('relevant-strikes-panel')).toContainText('Ada');
  await expect(page.getByTestId('all-strikes-panel')).toContainText('expires');
  await expect(page.getByTestId('admin-last-check')).toContainText('Last activity check');

  await page.getByTestId('admin-tab-excuses').click();
  await expect(page.getByTestId('excuses-panel')).toContainText('Ada');

  await page.getByTestId('admin-tab-linking').click();
  await expect(page.getByTestId('link-requests-panel')).toContainText('Discord 77');

  // Members section renders the empty club-stats state (404 mocked → empty, not an error).
  await page.getByTestId('admin-tab-members').click();
  await expect(page.getByTestId('club-stats-empty')).toBeVisible();
});

test('adds and revokes a strike with confirmation', async ({ page }) => {
  await mockMe(page, adminMe);
  await mockDashboard(page);
  await mockAdminArea(page);

  const writes: string[] = [];
  await page.route('**/api/v1/activity/admin/strikes', (route) => {
    if (route.request().method() === 'POST') {
      writes.push(`POST ${route.request().url()}`);
      return route.fulfill({
        status: 201,
        json: { strikeId: '33333333-3333-3333-3333-333333333333' },
      });
    }
    return route.fulfill({
      json: [
        {
          strikeId: '11111111-1111-1111-1111-111111111111',
          nickname: 'Ada',
          timestamp: '2026-06-20T00:00:00Z',
          revoked: false,
          expiresAt: '2026-09-20T00:00:00Z',
        },
      ],
    });
  });
  await page.route('**/api/v1/activity/admin/strikes/*/revoke', (route) => {
    writes.push(`POST ${route.request().url()}`);
    return route.fulfill({
      json: {
        strikeId: '11111111-1111-1111-1111-111111111111',
        nickname: 'Ada',
        timestamp: '2026-06-20T00:00:00Z',
        revoked: true,
        expiresAt: '2026-09-20T00:00:00Z',
      },
    });
  });

  await page.goto('/');
  await page.getByTestId('tab-admin').click();
  await expect(page.getByTestId('all-strikes-panel')).toContainText('Ada');

  // Add a strike — confirmation is an in-app dialog (window.confirm is suppressed in Discord's iframe).
  await page.getByTestId('add-strike-nickname').fill('Ada');
  await page.getByTestId('add-strike-submit').click();
  await page.getByTestId('confirm-accept').click();
  await expect.poll(() => writes.some((w) => w.endsWith('/admin/strikes'))).toBe(true);

  // Revoke the listed strike.
  await page.getByTestId('strike-revoke').click();
  await page.getByTestId('confirm-accept').click();
  await expect.poll(() => writes.some((w) => w.includes('/revoke'))).toBe(true);
});

test('completes a linking request with the OTP', async ({ page }) => {
  await mockMe(page, adminMe);
  await mockDashboard(page);
  await mockAdminArea(page);

  let completeBody: unknown = null;
  await page.route('**/api/v1/activity/admin/link-requests/complete', (route) => {
    completeBody = route.request().postDataJSON();
    return route.fulfill({ json: { geoGuessrUserId: 'a'.repeat(24), nickname: 'Ada' } });
  });

  await page.goto('/');
  await page.getByTestId('tab-admin').click();
  await page.getByTestId('admin-tab-linking').click();
  await expect(page.getByTestId('link-requests-panel')).toContainText('Discord 77');

  await page.getByTestId('link-complete').click();
  await page.getByTestId('complete-otp-input').fill('otp-from-geoguessr');
  await page.getByTestId('complete-confirm').click();

  await expect
    .poll(() => completeBody)
    .toMatchObject({ discordUserId: '77', oneTimePassword: 'otp-from-geoguessr' });
});
