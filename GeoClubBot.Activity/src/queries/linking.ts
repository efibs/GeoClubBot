import { useMutation, useQueryClient } from '@tanstack/vue-query';
import { cancelLinkRequest, startLinkRequest } from '../api';
import { queryKeys } from './keys';

/**
 * The viewer's own account-linking actions. Both invalidate the session so the open request (and
 * its OTP) reflects the server; returning that promise keeps the mutation `pending` — and thus the
 * button spinner — until `/me` has refreshed.
 */
export function useStartLinkMutation() {
  const client = useQueryClient();
  return useMutation({
    mutationFn: (geoGuessrUserId: string) => startLinkRequest(geoGuessrUserId),
    onSuccess: () => client.invalidateQueries({ queryKey: queryKeys.session }),
  });
}

export function useCancelLinkMutation() {
  const client = useQueryClient();
  return useMutation({
    mutationFn: () => cancelLinkRequest(),
    onSuccess: () => client.invalidateQueries({ queryKey: queryKeys.session }),
  });
}
