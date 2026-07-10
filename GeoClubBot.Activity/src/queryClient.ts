import { QueryClient } from '@tanstack/vue-query';

/**
 * Shared TanStack Query client. Exported as a singleton so code outside a component setup (the
 * router's admin guard, App bootstrap) can read/prime the cache; the same instance is handed to
 * `VueQueryPlugin` in main.ts.
 *
 * Defaults mirror the pre-Query behavior: no automatic retries (the old fetch layer never retried)
 * and no refetch-on-focus (avoids surprise reloads inside the Discord iframe and keeps E2E
 * deterministic). Views opt into background polling with a per-query `refetchInterval`.
 */
export const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      retry: false,
      refetchOnWindowFocus: false,
    },
  },
});
