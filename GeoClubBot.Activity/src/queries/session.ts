import { computed } from 'vue';
import { useQuery } from '@tanstack/vue-query';
import { fetchMe } from '../api';
import { queryClient } from '../queryClient';
import { queryKeys } from './keys';
import type { MeDto } from '../types';

/**
 * The viewer's session (`/me`). `staleTime: Infinity` means it's fetched once and then only
 * refreshed when a link/unlink mutation explicitly invalidates it — matching the old "load once"
 * behavior. A failure is non-fatal: consumers fall back to the non-admin, unlinked defaults.
 */
export function useMeQuery() {
  return useQuery({
    queryKey: queryKeys.session,
    queryFn: fetchMe,
    staleTime: Infinity,
  });
}

/** The session query plus the derived flags the UI branches on. */
export function useSession() {
  const query = useMeQuery();
  const me = query.data;
  return {
    me,
    isPending: query.isPending,
    isAdmin: computed(() => me.value?.isAdmin === true),
    isLinked: computed(() => me.value?.linked != null),
    nickname: computed(() => me.value?.linked?.nickname ?? null),
    openLinkRequest: computed(() => me.value?.openLinkRequest ?? null),
  };
}

/** Reads the cached session outside a component (the router admin guard); undefined before load. */
export function cachedSession(): MeDto | undefined {
  return queryClient.getQueryData<MeDto>(queryKeys.session);
}
