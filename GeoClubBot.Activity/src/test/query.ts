import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query';
import { queryKeys } from '../queries/keys';
import type { MeDto } from '../types';

/** A fresh QueryClient for a test, optionally pre-seeded with the session (`/me`) cache. */
export function testQueryClient(session?: MeDto | null): QueryClient {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false, refetchOnWindowFocus: false } },
  });
  if (session !== undefined) {
    client.setQueryData(queryKeys.session, session);
  }
  return client;
}

/** `@vue/test-utils` plugin entry that installs Vue Query with the given client. */
export function queryPlugin(
  client: QueryClient,
): [typeof VueQueryPlugin, { queryClient: QueryClient }] {
  return [VueQueryPlugin, { queryClient: client }];
}
