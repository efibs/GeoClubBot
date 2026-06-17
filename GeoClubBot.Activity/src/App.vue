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
const { data, loading, error, historyDepth, lastUpdated, viewerNickname, clubName, clubLevel } =
  storeToRefs(store);

const initializing = ref(true);
const initError = ref<string | null>(null);

const leaderboard = computed(() => data.value?.leaderboard ?? []);
const challenges = computed(() => data.value?.challenges ?? []);
const streaks = computed(() => data.value?.streaks ?? []);

let refreshTimer: ReturnType<typeof setInterval> | undefined;

onMounted(async () => {
  try {
    await initializeDiscord();
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
      <p>Connecting to the club…</p>
    </div>

    <div v-else-if="initError" class="screen-message error" data-testid="init-error">
      <p>⚠️ {{ initError }}</p>
    </div>

    <template v-else>
      <DashboardHeader
        :club-name="clubName"
        :club-level="clubLevel"
        :last-updated="lastUpdated"
        :loading="loading"
        @refresh="store.load()"
      />

      <div class="toolbar">
        <PeriodFilter
          :model-value="historyDepth"
          :options="depthOptions"
          @update:model-value="store.setHistoryDepth"
        />
      </div>

      <p v-if="error" class="error-banner" data-testid="error-banner">⚠️ {{ error }}</p>

      <main class="panels">
        <LeaderboardPanel :entries="leaderboard" :viewer-nickname="viewerNickname" />
        <ChallengePanel :challenges="challenges" :viewer-nickname="viewerNickname" />
        <StreaksPanel :streaks="streaks" :viewer-nickname="viewerNickname" />
      </main>
    </template>
  </div>
</template>
