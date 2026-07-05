import { createApp, type App } from 'vue';
import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query';

/**
 * Runs a composable inside a real (headless) component so `useQuery`/`useMutation` have an app
 * context and an injected client. Returns the composable's result plus the app (call `app.unmount()`
 * to dispose query observers when a test is done).
 */
export function withSetup<T>(composable: () => T, client: QueryClient): { result: T; app: App } {
  let result!: T;
  const app = createApp({
    setup() {
      result = composable();
      return () => null;
    },
  });
  app.use(VueQueryPlugin, { queryClient: client });
  app.mount(document.createElement('div'));
  return { result, app };
}

/** A QueryClient tuned for tests: no retries, no background refetches. */
export function testClient(): QueryClient {
  return new QueryClient({
    defaultOptions: { queries: { retry: false, refetchOnWindowFocus: false } },
  });
}
