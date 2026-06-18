import { DiscordSDK } from '@discord/embedded-app-sdk';
import { discordClientId, isBypass } from './config';
import { exchangeToken, setAccessToken } from './api';

export interface AuthResult {
  accessToken: string;
}

/** Rejects with a descriptive error if the wrapped promise hasn't settled in time. */
function withTimeout<T>(promise: Promise<T>, ms: number, label: string): Promise<T> {
  return Promise.race([
    promise,
    new Promise<T>((_, reject) =>
      setTimeout(() => reject(new Error(`${label} timed out after ${ms / 1000}s.`)), ms),
    ),
  ]);
}

/**
 * Runs the Discord Embedded App SDK handshake and returns the resolved access token:
 * `ready()` → `authorize(identify)` → backend code-for-token exchange → `authenticate()`.
 *
 * Each step is bounded by a timeout and reports progress via {@link onStep} and the console, so a
 * stalled handshake surfaces as a visible error instead of an endless "connecting" state.
 *
 * In bypass mode (local dev / E2E outside Discord) the SDK is skipped entirely and a placeholder
 * token is used; the backend is then exercised through the Vite proxy or mocked by the test.
 */
export async function initializeDiscord(onStep?: (step: string) => void): Promise<AuthResult> {
  if (isBypass) {
    const token = 'dev-bypass-token';
    setAccessToken(token);
    return { accessToken: token };
  }

  if (!discordClientId) {
    throw new Error('VITE_DISCORD_CLIENT_ID is not set in this build.');
  }

  const sdk = new DiscordSDK(discordClientId);

  onStep?.('Connecting to Discord…');
  console.info('[activity] sdk.ready()…');
  await withTimeout(sdk.ready(), 15_000, 'Discord handshake (ready)');

  onStep?.('Authorizing…');
  console.info('[activity] authorize()…');
  const { code } = await withTimeout(
    sdk.commands.authorize({
      client_id: discordClientId,
      response_type: 'code',
      state: '',
      prompt: 'none',
      scope: ['identify'],
    }),
    20_000,
    'Discord authorization',
  );

  onStep?.('Signing in…');
  console.info('[activity] exchanging code for token…');
  const accessToken = await withTimeout(exchangeToken(code), 15_000, 'Token exchange');
  setAccessToken(accessToken);

  onStep?.('Finishing up…');
  console.info('[activity] authenticate()…');
  await withTimeout(
    sdk.commands.authenticate({ access_token: accessToken }),
    15_000,
    'Discord authentication',
  );

  console.info('[activity] handshake complete');
  return { accessToken };
}
