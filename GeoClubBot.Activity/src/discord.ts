import { DiscordSDK } from '@discord/embedded-app-sdk';
import { discordClientId, isBypass } from './config';
import { exchangeToken, setAccessToken } from './api';

export interface AuthResult {
  accessToken: string;
}

/**
 * Runs the Discord Embedded App SDK handshake and returns the resolved access token:
 * `ready()` → `authorize(identify)` → backend code-for-token exchange → `authenticate()`.
 *
 * In bypass mode (local dev / E2E outside Discord) the SDK is skipped entirely and a placeholder
 * token is used; the backend is then exercised through the Vite proxy or mocked by the test.
 */
export async function initializeDiscord(): Promise<AuthResult> {
  if (isBypass) {
    const token = 'dev-bypass-token';
    setAccessToken(token);
    return { accessToken: token };
  }

  if (!discordClientId) {
    throw new Error('VITE_DISCORD_CLIENT_ID is not configured.');
  }

  const sdk = new DiscordSDK(discordClientId);
  await sdk.ready();

  const { code } = await sdk.commands.authorize({
    client_id: discordClientId,
    response_type: 'code',
    state: '',
    prompt: 'none',
    scope: ['identify'],
  });

  const accessToken = await exchangeToken(code);
  setAccessToken(accessToken);

  await sdk.commands.authenticate({ access_token: accessToken });

  return { accessToken };
}
