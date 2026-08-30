<script setup lang="ts">
import { computed } from 'vue';
import PanelSection from '../components/PanelSection.vue';
import FactRow from '../components/FactRow.vue';
import ReminderPanel from '../components/ReminderPanel.vue';
import LoadingScreen from '../components/LoadingScreen.vue';
import ErrorBanner from '../components/ErrorBanner.vue';
import { useMissionStatsQuery, useTodaysXpQuery } from '../queries/missions';
import { formatPercent, formatXp } from '../format';
import { toErrorMessage } from '../api';

const statsQuery = useMissionStatsQuery();
const todaysXpQuery = useTodaysXpQuery();

const { data: stats } = statsQuery;
const { data: todaysXp } = todaysXpQuery;

// First load shows a spinner; once either query has data the view renders (a later failure surfaces
// in the inline banner without blanking what loaded).
const isLoading = computed(() => statsQuery.isPending.value && todaysXpQuery.isPending.value);
const errorMessage = computed(() => {
  const err = statsQuery.error.value ?? todaysXpQuery.error.value;
  return err ? toErrorMessage(err, 'Failed to load mission statistics.') : null;
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
          earned by {{ todaysXp.clubName }} today
        </p>
        <!-- Two independent awards, so two counts: one number would hide half the picture. -->
        <FactRow v-if="todaysXp?.totalMemberCount != null" label="Daily mission">
          {{ todaysXp.missionMemberCount }} / {{ todaysXp.totalMemberCount }}
        </FactRow>
        <FactRow v-if="todaysXp?.totalMemberCount != null" label="Challenge or duel">
          {{ todaysXp.challengeMemberCount }} / {{ todaysXp.totalMemberCount }}
        </FactRow>
      </PanelSection>

      <PanelSection title="🎯 Daily missions" data-testid="mission-stats-panel">
        <template v-if="stats && stats.daysWithMissionData > 0">
          <FactRow label="Average completion">
            {{ formatPercent(stats.averageDayCompletionRate) }}
          </FactRow>
          <FactRow label="Days with data">{{ stats.daysWithMissionData }}</FactRow>
          <FactRow label="Mission appearances">{{ stats.totalMissionAppearances }}</FactRow>
          <!-- Null before the bot started tracking the daily challenge; say so rather than 0%. -->
          <FactRow label="Challenge or duel played">
            {{
              stats.averageDayChallengeRate != null
                ? `${formatPercent(stats.averageDayChallengeRate)} (since ${stats.challengeTrackedFrom})`
                : 'not tracked yet'
            }}
          </FactRow>
          <p class="stat-caption">
            {{ stats.clubName ?? 'All clubs' }} · {{ stats.fromDay }} → {{ stats.toDay }}
          </p>
        </template>
        <p v-else class="empty-state" data-testid="mission-stats-empty">No mission data yet.</p>
      </PanelSection>

      <PanelSection title="📋 Mission kinds" data-testid="mission-kinds-panel">
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
      </PanelSection>

      <ReminderPanel />
    </main>
  </template>
</template>
