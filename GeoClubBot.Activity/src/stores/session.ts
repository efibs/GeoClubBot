import { defineStore } from 'pinia';
import { fetchMe } from '../api';
import type { LinkRequestDto, MeDto } from '../types';

interface SessionState {
  me: MeDto | null;
  loading: boolean;
  error: string | null;
}

/**
 * The viewer's own session context, loaded once after the Discord handshake. Drives which tabs are
 * offered (the admin flag here is cosmetic — the server re-checks the policy on every admin call)
 * and the linked/unlinked UI states. A load failure is non-fatal: the shell still renders and the
 * viewer simply gets the non-admin, unlinked defaults.
 */
export const useSessionStore = defineStore('session', {
  state: (): SessionState => ({
    me: null,
    loading: false,
    error: null,
  }),
  getters: {
    isAdmin: (state): boolean => state.me?.isAdmin === true,
    isLinked: (state): boolean => state.me?.linked != null,
    nickname: (state): string | null => state.me?.linked?.nickname ?? null,
    openLinkRequest: (state): LinkRequestDto | null => state.me?.openLinkRequest ?? null,
  },
  actions: {
    async loadMe(): Promise<void> {
      this.loading = true;
      this.error = null;
      try {
        this.me = await fetchMe();
      } catch (err) {
        this.error = err instanceof Error ? err.message : 'Failed to load your profile.';
      } finally {
        this.loading = false;
      }
    },
  },
});
