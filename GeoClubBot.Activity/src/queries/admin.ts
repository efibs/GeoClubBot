import { computed, reactive, ref, type Ref } from 'vue';
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/vue-query';
import {
  ApiError,
  adminAddExcuse,
  adminAddStrike,
  adminCancelLinkRequest,
  adminCompleteLinkRequest,
  adminFetchClubStatistics,
  adminFetchExcuses,
  adminFetchLastCheckTime,
  adminFetchLinkRequests,
  adminFetchMemberActivity,
  adminFetchMemberStatistics,
  adminFetchMemberStrikes,
  adminFetchRelevantStrikes,
  adminFetchStrikes,
  adminRemoveExcuse,
  adminRevokeStrike,
  adminUnlinkAccounts,
  adminUnrevokeStrike,
  adminUpdateExcuse,
  toErrorMessage,
} from '../api';
import { memberLookupNickname } from '../state/ui';
import { queryKeys } from './keys';
import type { AdminMemberStrikesDto, PlayerStatisticsDto, WeekActivityDto } from '../types';

type SectionKey = readonly (string | number)[];

/**
 * Builds a write mutation for one admin section. Clears the section's shared error on start, records
 * it on failure, and on success invalidates the section's list — returning that promise keeps the
 * mutation `pending` (and its button busy) until the list has refreshed. On failure nothing is
 * invalidated, so the previous list stays on screen, matching the old store behavior.
 */
function useSectionMutation<TVars>(
  error: Ref<string | null>,
  sectionKey: SectionKey,
  mutationFn: (vars: TVars) => Promise<unknown>,
) {
  const client = useQueryClient();
  return useMutation({
    mutationFn,
    onMutate: () => {
      error.value = null;
    },
    onError: (err) => {
      error.value = toErrorMessage(err, 'The change failed.');
    },
    onSuccess: () => client.invalidateQueries({ queryKey: sectionKey }),
  });
}

/** Prefers a live mutation error, then the section's load error. */
function sectionError(mutationError: Ref<string | null>, loadError: Ref<Error | null>) {
  return computed(
    () =>
      mutationError.value ??
      (loadError.value ? toErrorMessage(loadError.value, 'Failed to load.') : null),
  );
}

/** The id of the row a mutation is currently acting on, or null (for per-row spinners). */
function pendingVariable<T>(mutation: { isPending: Ref<boolean>; variables: Ref<T | undefined> }) {
  return computed(() => (mutation.isPending.value ? (mutation.variables.value ?? null) : null));
}

export function useAdminStrikes() {
  const query = useQuery({
    queryKey: queryKeys.admin.strikes,
    queryFn: async () => {
      const [all, relevant] = await Promise.all([adminFetchStrikes(), adminFetchRelevantStrikes()]);
      return { all, relevant };
    },
  });

  const mutationError = ref<string | null>(null);
  const add = useSectionMutation(
    mutationError,
    queryKeys.admin.strikes,
    (vars: { nickname: string; strikeDate: string }) =>
      adminAddStrike(vars.nickname, vars.strikeDate),
  );
  const revoke = useSectionMutation(mutationError, queryKeys.admin.strikes, (strikeId: string) =>
    adminRevokeStrike(strikeId),
  );
  const unrevoke = useSectionMutation(mutationError, queryKeys.admin.strikes, (strikeId: string) =>
    adminUnrevokeStrike(strikeId),
  );
  const revoking = pendingVariable(revoke);
  const unrevoking = pendingVariable(unrevoke);

  return reactive({
    data: query.data,
    isPending: query.isPending,
    error: sectionError(mutationError, query.error),
    busy: anyPending(add, revoke, unrevoke),
    addPending: add.isPending,
    pendingStrikeId: computed(() => revoking.value ?? unrevoking.value),
    add: (nickname: string, strikeDate: string) => add.mutateAsync({ nickname, strikeDate }),
    revoke: (strikeId: string) => revoke.mutateAsync(strikeId),
    unrevoke: (strikeId: string) => unrevoke.mutateAsync(strikeId),
  });
}

export function useAdminExcuses() {
  const query = useQuery({
    queryKey: queryKeys.admin.excuses,
    queryFn: () => adminFetchExcuses(),
  });

  const mutationError = ref<string | null>(null);
  const add = useSectionMutation(
    mutationError,
    queryKeys.admin.excuses,
    (vars: { nickname: string; from: string; to: string }) =>
      adminAddExcuse(vars.nickname, vars.from, vars.to),
  );
  const update = useSectionMutation(
    mutationError,
    queryKeys.admin.excuses,
    (vars: { excuseId: string; from: string; to: string }) =>
      adminUpdateExcuse(vars.excuseId, vars.from, vars.to),
  );
  const remove = useSectionMutation(mutationError, queryKeys.admin.excuses, (excuseId: string) =>
    adminRemoveExcuse(excuseId),
  );

  return reactive({
    data: query.data,
    isPending: query.isPending,
    error: sectionError(mutationError, query.error),
    busy: anyPending(add, update, remove),
    submitting: computed(() => add.isPending.value || update.isPending.value),
    removingId: pendingVariable(remove),
    add: (nickname: string, from: string, to: string) => add.mutateAsync({ nickname, from, to }),
    update: (excuseId: string, from: string, to: string) =>
      update.mutateAsync({ excuseId, from, to }),
    remove: (excuseId: string) => remove.mutateAsync(excuseId),
  });
}

export function useAdminLinking() {
  const query = useQuery({
    queryKey: queryKeys.admin.linking,
    queryFn: () => adminFetchLinkRequests(),
  });

  const mutationError = ref<string | null>(null);
  const complete = useSectionMutation(
    mutationError,
    queryKeys.admin.linking,
    (vars: { discordUserId: string; geoGuessrUserId: string; oneTimePassword: string }) =>
      adminCompleteLinkRequest(vars.discordUserId, vars.geoGuessrUserId, vars.oneTimePassword),
  );
  const cancel = useSectionMutation(
    mutationError,
    queryKeys.admin.linking,
    (vars: { discordUserId: string; geoGuessrUserId: string }) =>
      adminCancelLinkRequest(vars.discordUserId, vars.geoGuessrUserId),
  );
  const unlink = useSectionMutation(
    mutationError,
    queryKeys.admin.linking,
    (vars: { discordUserId: string; geoGuessrUserId: string }) =>
      adminUnlinkAccounts(vars.discordUserId, vars.geoGuessrUserId),
  );

  const keyOf = (vars?: { discordUserId: string; geoGuessrUserId: string }) =>
    vars ? `${vars.discordUserId}:${vars.geoGuessrUserId}` : null;

  return reactive({
    data: query.data,
    isPending: query.isPending,
    error: sectionError(mutationError, query.error),
    busy: anyPending(complete, cancel, unlink),
    unlinking: unlink.isPending,
    pendingKey: computed(() => {
      if (complete.isPending.value) return keyOf(complete.variables.value);
      if (cancel.isPending.value) return keyOf(cancel.variables.value);
      return null;
    }),
    complete: (discordUserId: string, geoGuessrUserId: string, oneTimePassword: string) =>
      complete.mutateAsync({ discordUserId, geoGuessrUserId, oneTimePassword }),
    cancel: (discordUserId: string, geoGuessrUserId: string) =>
      cancel.mutateAsync({ discordUserId, geoGuessrUserId }),
    unlink: (discordUserId: string, geoGuessrUserId: string) =>
      unlink.mutateAsync({ discordUserId, geoGuessrUserId }),
  });
}

/** True while any of the given mutations is in flight. */
function anyPending(...mutations: { isPending: Ref<boolean> }[]) {
  return computed(() => mutations.some((mutation) => mutation.isPending.value));
}

interface MemberLookupResult {
  nickname: string;
  strikes: AdminMemberStrikesDto | null;
  activity: WeekActivityDto | null;
  statistics: PlayerStatisticsDto | null;
}

/** A 404 on any part just means "nothing recorded there"; anything else is a real lookup failure. */
async function lookupMember(nickname: string): Promise<MemberLookupResult> {
  const [strikes, activity, statistics] = await Promise.allSettled([
    adminFetchMemberStrikes(nickname),
    adminFetchMemberActivity(nickname),
    adminFetchMemberStatistics(nickname),
  ]);

  const failure = [strikes, activity, statistics].find(
    (result): result is PromiseRejectedResult =>
      result.status === 'rejected' &&
      !(result.reason instanceof ApiError && result.reason.status === 404),
  );
  if (failure) {
    throw failure.reason;
  }

  return {
    nickname,
    strikes: strikes.status === 'fulfilled' ? strikes.value : null,
    activity: activity.status === 'fulfilled' ? activity.value : null,
    statistics: statistics.status === 'fulfilled' ? statistics.value : null,
  };
}

/** Member lookup driven by a persisted nickname ref, so the result survives a tab switch. */
export function useAdminMemberLookup() {
  const nickname = memberLookupNickname;
  const query = useQuery({
    queryKey: computed(() => queryKeys.admin.lookup(nickname.value)),
    queryFn: () => lookupMember(nickname.value),
    enabled: computed(() => nickname.value.trim().length > 0),
    placeholderData: keepPreviousData,
  });

  return reactive({
    data: query.data,
    loading: query.isFetching,
    error: computed(() =>
      query.error.value ? toErrorMessage(query.error.value, 'Failed to look up the member.') : null,
    ),
    nickname,
    search: (value: string) => {
      nickname.value = value.trim();
    },
  });
}

export function useClubStatsQuery() {
  return useQuery({
    queryKey: queryKeys.admin.clubStats,
    // "No club history yet" is an empty state, never an error banner.
    queryFn: async () => {
      try {
        return await adminFetchClubStatistics();
      } catch {
        return null;
      }
    },
  });
}

export function useLastCheckTimeQuery() {
  return useQuery({
    queryKey: queryKeys.admin.lastCheckTime,
    queryFn: async () => {
      try {
        return (await adminFetchLastCheckTime()).lastCheckTime;
      } catch {
        return null;
      }
    },
  });
}
