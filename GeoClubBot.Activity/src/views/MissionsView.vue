<script setup lang="ts">
import { computed } from 'vue';
import PanelSection from '../components/PanelSection.vue';
import FactRow from '../components/FactRow.vue';
import BoardPanel from '../components/BoardPanel.vue';
import ReminderPanel from '../components/ReminderPanel.vue';
import LoadingScreen from '../components/LoadingScreen.vue';
import ErrorBanner from '../components/ErrorBanner.vue';
import { useMissionBoardQuery, useTodaysXpQuery } from '../queries/missions';
import { formatXp } from '../format';
import { ApiError, toErrorMessage } from '../api';

const boardQuery = useMissionBoardQuery();
const todaysXpQuery = useTodaysXpQuery();

const { data: board } = boardQuery;
const { data: todaysXp } = todaysXpQuery;

// A 404 on the board means GeoGuessr couldn't be read right now — the panel says so itself, so it
// is not an error worth a banner.
const boardUnavailable = computed(
  () => boardQuery.error.value instanceof ApiError && boardQuery.error.value.status === 404,
);

// First load shows a spinner; once either query has data the view renders (a later failure surfaces
// in the inline banner without blanking what loaded).
const isLoading = computed(() => boardQuery.isPending.value && todaysXpQuery.isPending.value);
const errorMessage = computed(() => {
  const err = (boardUnavailable.value ? null : boardQuery.error.value) ?? todaysXpQuery.error.value;
  return err ? toErrorMessage(err, 'Failed to load the club missions.') : null;
});
</script>

<template>
  <LoadingScreen v-if="isLoading" data-testid="missions-loading" />

  <template v-else>
    <ErrorBanner v-if="errorMessage" data-testid="error-banner">{{ errorMessage }}</ErrorBanner>

    <main class="panels" data-testid="missions-view">
      <PanelSection title="⚡ Today's club XP" data-testid="todays-xp-tile">
        <p class="stat-value">{{ todaysXp?.xp != null ? formatXp(todaysXp.xp) : '—' }}</p>
        <p v-if="todaysXp?.clubName" class="stat-caption">
          earned by {{ todaysXp.clubName }} today (UTC)
        </p>
        <!-- The streak and the board are independent, so separate counts: one number would hide half the picture. -->
        <FactRow v-if="todaysXp?.totalMemberCount != null" label="Streak kept">
          {{ todaysXp.challengeMemberCount }} / {{ todaysXp.totalMemberCount }}
        </FactRow>
        <FactRow v-if="todaysXp?.boardMissionCount != null" label="Missions finished">
          {{ todaysXp.boardMissionCount }}
        </FactRow>
        <FactRow
          v-if="todaysXp?.claimMemberCount != null && todaysXp?.totalMemberCount != null"
          label="Claimed this claim cycle"
        >
          {{ todaysXp.claimMemberCount }} / {{ todaysXp.totalMemberCount }}
        </FactRow>
      </PanelSection>

      <BoardPanel v-if="board" :board="board" />
      <PanelSection
        v-else-if="boardUnavailable"
        title="🗺️ Mission board"
        data-testid="board-unavailable"
      >
        <p class="empty-state">The mission board can't be read right now.</p>
      </PanelSection>

      <ReminderPanel />
    </main>
  </template>
</template>
