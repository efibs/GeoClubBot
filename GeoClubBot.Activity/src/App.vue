<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref } from 'vue';
import { storeToRefs } from 'pinia';
import DashboardHeader from './components/DashboardHeader.vue';
import PeriodFilter from './components/PeriodFilter.vue';
import LeaderboardPanel from './components/LeaderboardPanel.vue';
import ChallengePanel from './components/ChallengePanel.vue';
import StreaksPanel from './components/StreaksPanel.vue';
import { useDashboardStore } from './stores/dashboard';
import { initializeDiscord } from './discord';
import { depthOptions } from './format';
import { refreshIntervalMs } from './config';

const store = useDashboardStore();
const { data, loading, error, historyDepth, lastUpdated, viewerNickname, clubName, clubLevel, hasClub } =
  storeToRefs(store);

const initializing = ref(true);
const initError = ref<string | null>(null);
const initStatus = ref('Connecting to the club…');

const leaderboard = computed(() => data.value?.leaderboard ?? []);
const challenges = computed(() => data.value?.challenges ?? []);
const streaks = computed(() => data.value?.streaks ?? []);

let refreshTimer: ReturnType<typeof setInterval> | undefined;

onMounted(async () => {
  try {
    await initializeDiscord((step) => {
      initStatus.value = step;
    });
    await store.load();
  } catch (err) {
    initError.value = err instanceof Error ? err.message : 'Failed to start the dashboard.';
  } finally {
    initializing.value = false;
  }

  refreshTimer = setInterval(() => {
    void store.load(false);
  }, refreshIntervalMs);
});

onUnmounted(() => {
  if (refreshTimer) {
    clearInterval(refreshTimer);
  }
});
</script>

<template>
  <div class="app">
    <div v-if="initializing" class="screen-message" data-testid="initializing">
      <div class="spinner" />
      <p>{{ initStatus }}</p>
    </div>

    <div v-else-if="initError" class="screen-message error" data-testid="init-error">
      <p>⚠️ {{ initError }}</p>
    </div>

    <div v-else-if="error && !data" class="screen-message error" data-testid="load-error">
      <p>⚠️ {{ error }}</p>
    </div>

    <template v-else>
      <DashboardHeader
        :club-name="clubName"
        :club-level="clubLevel"
        :last-updated="lastUpdated"
        :loading="loading"
        @refresh="store.load()"
      />

      <div v-if="hasClub" class="toolbar">
        <PeriodFilter
          :model-value="historyDepth"
          :options="depthOptions"
          @update:model-value="store.setHistoryDepth"
        />
      </div>

      <p v-if="error" class="error-banner" data-testid="error-banner">⚠️ {{ error }}</p>

      <main class="panels">
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
  </div>
</template>
