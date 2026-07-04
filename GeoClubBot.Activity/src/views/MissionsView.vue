<script setup lang="ts">
import { onMounted } from 'vue';
import { storeToRefs } from 'pinia';
import ReminderPanel from '../components/ReminderPanel.vue';
import { useMissionsStore } from '../stores/missions';
import { formatPercent, formatXp } from '../format';
import { usePolling } from '../composables/usePolling';

const store = useMissionsStore();
const { stats, todaysXp, loading, error } = storeToRefs(store);

onMounted(() => {
  void store.load(stats.value === null && todaysXp.value === null);
});

usePolling(() => {
  void store.load(false);
});
</script>

<template>
  <div v-if="loading && !stats && !todaysXp" class="screen-message" data-testid="missions-loading">
    <div class="spinner" />
  </div>

  <template v-else>
    <p v-if="error" class="error-banner" data-testid="error-banner">⚠️ {{ error }}</p>

    <main class="panels" data-testid="missions-view">
      <section class="panel" data-testid="todays-xp-tile">
        <h2 class="panel-title">⚡ Today's club XP</h2>
        <p class="stat-value">{{ todaysXp?.xp != null ? formatXp(todaysXp.xp) : '—' }}</p>
        <p v-if="todaysXp?.clubName" class="stat-caption">earned by {{ todaysXp.clubName }} today</p>
      </section>

      <section class="panel" data-testid="mission-stats-panel">
        <h2 class="panel-title">🎯 Daily missions</h2>
        <template v-if="stats && stats.daysWithMissionData > 0">
          <div class="fact-row">
            <span>Average completion</span>
            <strong>{{ formatPercent(stats.averageDayCompletionRate) }}</strong>
          </div>
          <div class="fact-row">
            <span>Days with data</span>
            <strong>{{ stats.daysWithMissionData }}</strong>
          </div>
          <div class="fact-row">
            <span>Mission appearances</span>
            <strong>{{ stats.totalMissionAppearances }}</strong>
          </div>
          <p class="stat-caption">
            {{ stats.clubName ?? 'All clubs' }} · {{ stats.fromDay }} → {{ stats.toDay }}
          </p>
        </template>
        <p v-else class="empty-state" data-testid="mission-stats-empty">No mission data yet.</p>
      </section>

      <section class="panel" data-testid="mission-kinds-panel">
        <h2 class="panel-title">📋 Mission kinds</h2>
        <ul v-if="stats && stats.kinds.length > 0" class="rows">
          <li
            v-for="kind in stats.kinds"
            :key="`${kind.type}-${kind.gameMode}`"
            class="row"
            :data-testid="`mission-kind-${kind.type}-${kind.gameMode}`"
          >
            <span class="rank">•</span>
            <span class="name">{{ kind.type }} · {{ kind.gameMode }}</span>
            <span class="value">{{ formatPercent(kind.averageDayCompletionRateWhenPresent) }}</span>
            <span class="sub">seen {{ kind.appearanceCount }}×</span>
          </li>
        </ul>
        <p v-else class="empty-state" data-testid="mission-kinds-empty">No missions logged yet.</p>
      </section>

      <ReminderPanel />
    </main>
  </template>
</template>
