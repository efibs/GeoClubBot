import { type Ref } from 'vue';
import { useQuery } from '@tanstack/vue-query';
import { ApiError, fetchMyActivity, fetchMyProfile } from '../api';
import { refreshIntervalMs } from '../config';
import { queryKeys } from './keys';

/** Maps a 404 (viewer not linked, or a race with unlinking) to null rather than an error. */
async function orNullOn404<T>(fetcher: () => Promise<T>): Promise<T | null> {
  try {
    return await fetcher();
  } catch (err) {
    if (err instanceof ApiError && err.status === 404) {
      return null;
    }
    throw err;
  }
}

/**
 * The viewer's own profile / activity window. Enabled only while linked (so an unlinked viewer
 * never polls the endpoints); a 404 still degrades to null in case of a link/unlink race.
 */
export function useProfileQuery(enabled: Ref<boolean>) {
  return useQuery({
    queryKey: queryKeys.profile,
    queryFn: () => orNullOn404(fetchMyProfile),
    enabled,
    refetchInterval: refreshIntervalMs,
  });
}

export function useMyActivityQuery(enabled: Ref<boolean>) {
  return useQuery({
    queryKey: queryKeys.myActivity,
    queryFn: () => orNullOn404(() => fetchMyActivity()),
    enabled,
    refetchInterval: refreshIntervalMs,
  });
}
