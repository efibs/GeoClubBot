import { defineStore } from 'pinia';
import { ApiError, fetchMyActivity, fetchMyProfile } from '../api';
import type { ProfileDto, WeekActivityDto } from '../types';

interface ProfileState {
  profile: ProfileDto | null;
  activity: WeekActivityDto | null;
  loading: boolean;
  error: string | null;
}

/**
 * The viewer's own profile + activity window. Only loaded when the session says the viewer is
 * linked; a 404 (race with unlinking) is treated as "not linked", not as an error.
 */
export const useProfileStore = defineStore('profile', {
  state: (): ProfileState => ({
    profile: null,
    activity: null,
    loading: false,
    error: null,
  }),
  actions: {
    async load(showSpinner = true): Promise<void> {
      if (showSpinner) {
        this.loading = true;
      }
      this.error = null;
      const [profile, activity] = await Promise.allSettled([fetchMyProfile(), fetchMyActivity()]);
      if (profile.status === 'fulfilled') {
        this.profile = profile.value;
      }
      if (activity.status === 'fulfilled') {
        this.activity = activity.value;
      }

      const failure = [profile, activity].find(
        (result): result is PromiseRejectedResult =>
          result.status === 'rejected' && !(result.reason instanceof ApiError && result.reason.status === 404),
      );
      if (failure) {
        this.error =
          failure.reason instanceof Error ? failure.reason.message : 'Failed to load your profile.';
      }
      this.loading = false;
    },
  },
});
