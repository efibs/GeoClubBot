import { expect, test } from '@playwright/test';
import { mockDashboard, mockMe, mockMemberTabs, memberMe } from './fixtures';

test('sets a daily reminder from the missions tab', async ({ page }) => {
  await mockMe(page);
  await mockDashboard(page);
  await mockMemberTabs(page);

  let putBody: unknown = null;
  await page.route('**/api/v1/activity/me/reminder', (route) => {
    if (route.request().method() === 'PUT') {
      putBody = route.request().postDataJSON();
      return route.fulfill({
        json: {
          reminder: { timeUtc: '17:30', timeZoneId: 'Europe/Berlin', customMessage: null },
          dmDelivered: true,
          dmErrorCode: null,
        },
      });
    }
    return route.fulfill({ json: { reminder: null } });
  });

  await page.goto('/');
  await page.getByTestId('tab-missions').click();
  await expect(page.getByTestId('reminder-panel')).toBeVisible();

  await page.getByTestId('reminder-time').fill('19:30');
  await page.getByTestId('reminder-save').click();

  await expect(page.getByTestId('reminder-status')).toContainText('17:30 UTC');
  await expect.poll(() => putBody).toMatchObject({ localTime: '19:30' });
});

test('shows a warning when the confirmation DM is undeliverable', async ({ page }) => {
  await mockMe(page);
  await mockDashboard(page);
  await mockMemberTabs(page);
  await page.route('**/api/v1/activity/me/reminder', (route) => {
    if (route.request().method() === 'PUT') {
      return route.fulfill({
        json: {
          reminder: { timeUtc: '17:30', timeZoneId: null, customMessage: null },
          dmDelivered: false,
          dmErrorCode: 'discord.dm.disabled',
        },
      });
    }
    return route.fulfill({ json: { reminder: null } });
  });

  await page.goto('/');
  await page.getByTestId('tab-missions').click();
  await page.getByTestId('reminder-time').fill('19:30');
  await page.getByTestId('reminder-save').click();

  await expect(page.getByTestId('reminder-dm-warning')).toBeVisible();
});

test('walks through the account-linking flow', async ({ page }) => {
  const userId = '62c353a29d0d57e7b9a3383f';
  const unlinked = { ...memberMe, linked: null, club: null };
  const withRequest = {
    ...unlinked,
    openLinkRequest: { geoGuessrUserId: userId, oneTimePassword: 'otp-9876' },
  };

  // /me flips to the open-request shape once the POST happened.
  let started = false;
  await page.route('**/api/v1/activity/me', (route) =>
    route.fulfill({ json: started ? withRequest : unlinked }),
  );
  await page.route('**/api/v1/activity/me/link-request', (route) => {
    if (route.request().method() === 'POST') {
      started = true;
      return route.fulfill({ json: { geoGuessrUserId: userId, oneTimePassword: 'otp-9876' } });
    }
    started = false;
    return route.fulfill({ status: 204 });
  });
  await mockDashboard(page, { club: null, viewer: null, leaderboard: [], challenges: [], streaks: [] });

  await page.goto('/');
  await page.getByTestId('tab-me').click();
  await expect(page.getByTestId('link-account-panel')).toBeVisible();

  await page.getByTestId('link-profile-input').fill(`https://www.geoguessr.com/user/${userId}`);
  await page.getByTestId('link-start').click();

  await expect(page.getByTestId('link-otp')).toContainText('otp-9876');

  await page.getByTestId('link-cancel').click();
  await expect(page.getByTestId('link-profile-input')).toBeVisible();
});
