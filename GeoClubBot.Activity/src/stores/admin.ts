import { defineStore } from 'pinia';
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
} from '../api';
import type {
  AdminExcuseDto,
  AdminLinkRequestDto,
  AdminMemberStrikesDto,
  AdminRelevantStrikeDto,
  AdminStrikeDto,
  ClubStatisticsDto,
  PlayerStatisticsDto,
  WeekActivityDto,
} from '../types';

interface SectionState<T> {
  data: T | null;
  loading: boolean;
  error: string | null;
}

interface AdminState {
  lastCheckTime: string | null;
  strikes: SectionState<{ all: AdminStrikeDto[]; relevant: AdminRelevantStrikeDto[] }>;
  excuses: SectionState<AdminExcuseDto[]>;
  linking: SectionState<AdminLinkRequestDto[]>;
  clubStats: SectionState<ClubStatisticsDto>;
  lookup: SectionState<{
    nickname: string;
    strikes: AdminMemberStrikesDto | null;
    activity: WeekActivityDto | null;
    statistics: PlayerStatisticsDto | null;
  }>;
}

function section<T>(): SectionState<T> {
  return { data: null, loading: false, error: null };
}

async function loadSection<T>(state: SectionState<T>, fetcher: () => Promise<T>): Promise<void> {
  state.loading = true;
  state.error = null;
  try {
    state.data = await fetcher();
  } catch (err) {
    state.error = err instanceof Error ? err.message : 'Failed to load.';
  } finally {
    state.loading = false;
  }
}

/**
 * State of the admin area, one section per admin view. All data here comes from the policy-gated
 * `/admin/*` endpoints — a non-admin gets 403s and empty sections, never data.
 */
export const useAdminStore = defineStore('admin', {
  state: (): AdminState => ({
    lastCheckTime: null,
    strikes: section(),
    excuses: section(),
    linking: section(),
    clubStats: section(),
    lookup: section(),
  }),
  actions: {
    async loadLastCheckTime(): Promise<void> {
      try {
        this.lastCheckTime = (await adminFetchLastCheckTime()).lastCheckTime;
      } catch {
        this.lastCheckTime = null;
      }
    },

    async loadStrikes(): Promise<void> {
      await loadSection(this.strikes, async () => {
        const [all, relevant] = await Promise.all([adminFetchStrikes(), adminFetchRelevantStrikes()]);
        return { all, relevant };
      });
    },

    async loadExcuses(nickname?: string): Promise<void> {
      await loadSection(this.excuses, () => adminFetchExcuses(nickname));
    },

    async loadLinking(): Promise<void> {
      await loadSection(this.linking, () => adminFetchLinkRequests());
    },

    async loadClubStats(): Promise<void> {
      await loadSection(this.clubStats, () => adminFetchClubStatistics());
      // "No history yet" is an empty state, not an error.
      if (this.clubStats.error !== null && this.clubStats.data === null) {
        this.clubStats.error = null;
      }
    },

    // Write actions: run the mutation, surface a ProblemDetails message on the owning section,
    // and re-fetch the section on success so the list always reflects the server state.

    async mutateStrikes(mutation: () => Promise<unknown>): Promise<boolean> {
      this.strikes.error = null;
      try {
        await mutation();
      } catch (err) {
        this.strikes.error = err instanceof Error ? err.message : 'The change failed.';
        return false;
      }
      await this.loadStrikes();
      return true;
    },

    addStrike(nickname: string, strikeDate: string): Promise<boolean> {
      return this.mutateStrikes(() => adminAddStrike(nickname, strikeDate));
    },

    revokeStrike(strikeId: string): Promise<boolean> {
      return this.mutateStrikes(() => adminRevokeStrike(strikeId));
    },

    unrevokeStrike(strikeId: string): Promise<boolean> {
      return this.mutateStrikes(() => adminUnrevokeStrike(strikeId));
    },

    async mutateExcuses(mutation: () => Promise<unknown>): Promise<boolean> {
      this.excuses.error = null;
      try {
        await mutation();
      } catch (err) {
        this.excuses.error = err instanceof Error ? err.message : 'The change failed.';
        return false;
      }
      await this.loadExcuses();
      return true;
    },

    addExcuse(nickname: string, from: string, to: string): Promise<boolean> {
      return this.mutateExcuses(() => adminAddExcuse(nickname, from, to));
    },

    updateExcuse(excuseId: string, from: string, to: string): Promise<boolean> {
      return this.mutateExcuses(() => adminUpdateExcuse(excuseId, from, to));
    },

    removeExcuse(excuseId: string): Promise<boolean> {
      return this.mutateExcuses(() => adminRemoveExcuse(excuseId));
    },

    async mutateLinking(mutation: () => Promise<unknown>): Promise<boolean> {
      this.linking.error = null;
      try {
        await mutation();
      } catch (err) {
        this.linking.error = err instanceof Error ? err.message : 'The change failed.';
        return false;
      }
      await this.loadLinking();
      return true;
    },

    completeLinkRequest(discordUserId: string, geoGuessrUserId: string, oneTimePassword: string): Promise<boolean> {
      return this.mutateLinking(() =>
        adminCompleteLinkRequest(discordUserId, geoGuessrUserId, oneTimePassword),
      );
    },

    cancelLinkRequest(discordUserId: string, geoGuessrUserId: string): Promise<boolean> {
      return this.mutateLinking(() => adminCancelLinkRequest(discordUserId, geoGuessrUserId));
    },

    unlinkAccounts(discordUserId: string, geoGuessrUserId: string): Promise<boolean> {
      return this.mutateLinking(() => adminUnlinkAccounts(discordUserId, geoGuessrUserId));
    },

    async lookupMember(nickname: string): Promise<void> {
      this.lookup.loading = true;
      this.lookup.error = null;
      try {
        const [strikes, activity, statistics] = await Promise.allSettled([
          adminFetchMemberStrikes(nickname),
          adminFetchMemberActivity(nickname),
          adminFetchMemberStatistics(nickname),
        ]);

        this.lookup.data = {
          nickname,
          strikes: strikes.status === 'fulfilled' ? strikes.value : null,
          activity: activity.status === 'fulfilled' ? activity.value : null,
          statistics: statistics.status === 'fulfilled' ? statistics.value : null,
        };

        // A 404 just means "nothing recorded for that part"; anything else is a real error.
        const failure = [strikes, activity, statistics].find(
          (result): result is PromiseRejectedResult =>
            result.status === 'rejected' && !(result.reason instanceof ApiError && result.reason.status === 404),
        );
        if (failure) {
          this.lookup.error =
            failure.reason instanceof Error ? failure.reason.message : 'Failed to look up the member.';
        }
      } finally {
        this.lookup.loading = false;
      }
    },
  },
});
