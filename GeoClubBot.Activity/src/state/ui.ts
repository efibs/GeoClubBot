import { ref } from 'vue';
import { defaultHistoryDepth } from '../format';

/**
 * Client-only UI state that must outlive individual view mounts (so a tab switch and return keeps
 * the chosen leaderboard period and the last member lookup). Module-level singletons, following the
 * same pattern as `composables/useConfirm.ts` — small enough that a full store isn't warranted.
 */

/** Selected leaderboard/history depth for the Overview dashboard. */
export const historyDepth = ref(defaultHistoryDepth);

/** The nickname currently driving the admin member lookup ('' means no lookup yet). */
export const memberLookupNickname = ref('');

/** Resets UI state to defaults. Used by tests to isolate cases. */
export function resetUiState(): void {
  historyDepth.value = defaultHistoryDepth;
  memberLookupNickname.value = '';
}
