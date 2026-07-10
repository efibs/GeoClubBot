<script setup lang="ts">
import { computed } from 'vue';
import DashboardHeader from '../components/DashboardHeader.vue';
import PeriodFilter from '../components/PeriodFilter.vue';
import LeaderboardPanel from '../components/LeaderboardPanel.vue';
import ChallengePanel from '../components/ChallengePanel.vue';
import StreaksPanel from '../components/StreaksPanel.vue';
import LoadingScreen from '../components/LoadingScreen.vue';
import ErrorScreen from '../components/ErrorScreen.vue';
import ErrorBanner from '../components/ErrorBanner.vue';
import { useDashboardQuery } from '../queries/dashboard';
import { historyDepth } from '../state/ui';
import { depthOptions } from '../format';
import { toErrorMessage } from '../api';

const { data, isPending, isFetching, error, refetch, dataUpdatedAt } =
  useDashboardQuery(historyDepth);

const leaderboard = computed(() => data.value?.leaderboard ?? []);
const challenges = computed(() => data.value?.challenges ?? []);
const streaks = computed(() => data.value?.streaks ?? []);
const viewerNickname = computed(() => data.value?.viewer?.nickname ?? null);
const clubName = computed(() => data.value?.club?.name ?? 'Club Dashboard');
const clubLevel = computed(() => data.value?.club?.level ?? null);
const hasClub = computed(() => data.value?.club != null);
const lastUpdated = computed(() => (dataUpdatedAt.value ? new Date(dataUpdatedAt.value) : null));
const errorMessage = computed(() =>
  error.value ? toErrorMessage(error.value, 'Failed to load the dashboard.') : null,
);

function setDepth(value: number): void {
  historyDepth.value = value;
}
</script>

<template>
  <LoadingScreen v-if="isPending" data-testid="overview-loading" />

  <ErrorScreen v-else-if="errorMessage && !data" :message="errorMessage" data-testid="load-error" />

  <template v-else>
    <DashboardHeader
      :club-name="clubName"
      :club-level="clubLevel"
      :last-updated="lastUpdated"
      :loading="isFetching"
      @refresh="refetch()"
    />

    <div v-if="hasClub" class="toolbar">
      <PeriodFilter
        :model-value="historyDepth"
        :options="depthOptions"
        @update:model-value="setDepth"
      />
    </div>

    <ErrorBanner v-if="errorMessage" data-testid="error-banner">{{ errorMessage }}</ErrorBanner>

    <main class="panels panels-fill">
      <!-- Leaderboard and streaks are club-scoped; only shown when the viewer is in a club. The
           daily challenge is club-independent and shown to everyone. -->
      <LeaderboardPanel v-if="hasClub" :entries="leaderboard" :viewer-nickname="viewerNickname" />
      <ChallengePanel :challenges="challenges" :viewer-nickname="viewerNickname" />
      <StreaksPanel v-if="hasClub" :streaks="streaks" :viewer-nickname="viewerNickname" />

      <p v-if="!hasClub" class="no-club-note" data-testid="no-club">
        🌍 Club rankings and mission streaks appear here once you've linked your GeoGuessr account
        and joined a club. The daily challenge above is open to everyone.
      </p>
    </main>
  </template>
</template>

<style scoped>
.no-club-note {
  grid-column: 1 / -1;
  /* The panels grid stretches its rows to fill the viewport; keep the note at its natural height
     instead of ballooning to a full row when it's the only club-scoped content. */
  align-self: start;
  margin: 0;
  padding: 18px;
  border: 1px dashed var(--border);
  border-radius: 16px;
  color: var(--text-muted);
  font-size: 0.95rem;
  text-align: center;
}
</style>
