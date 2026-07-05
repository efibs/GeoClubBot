<script setup lang="ts">
import { computed } from 'vue';
import PanelSection from '../components/PanelSection.vue';
import FactRow from '../components/FactRow.vue';
import LinkAccountPanel from '../components/LinkAccountPanel.vue';
import ErrorBanner from '../components/ErrorBanner.vue';
import { useSession } from '../queries/session';
import { useMyActivityQuery, useProfileQuery } from '../queries/profile';
import { countryFlag, formatXp, weekdayInitial } from '../format';
import { toErrorMessage } from '../api';

const { isLinked, nickname } = useSession();

// The profile/activity queries are enabled only while linked; if the account gets linked while this
// tab is open (an admin completes the request), `isLinked` flips and they fetch on their own.
const profileQuery = useProfileQuery(isLinked);
const activityQuery = useMyActivityQuery(isLinked);
const { data: profile } = profileQuery;
const { data: activity } = activityQuery;
const loadingProfile = profileQuery.isPending;

const errorMessage = computed(() => {
  const err = profileQuery.error.value ?? activityQuery.error.value;
  return err ? toErrorMessage(err, 'Failed to load your profile.') : null;
});
</script>

<template>
  <main class="panels" data-testid="me-view">
    <LinkAccountPanel v-if="!isLinked" />

    <template v-else>
      <PanelSection
        :title="`👤 ${profile?.nickname ?? nickname ?? ''}`"
        data-testid="profile-panel"
      >
        <template v-if="profile">
          <FactRow label="Country">
            {{ countryFlag(profile.countryCode) }} {{ profile.countryCode.toUpperCase() }}
          </FactRow>
          <FactRow v-if="profile.level != null" label="Level">{{ profile.level }}</FactRow>
          <FactRow label="Playing since">{{ new Date(profile.createdAt).getFullYear() }}</FactRow>
          <FactRow v-if="profile.isProUser" label="Subscription">Pro</FactRow>
          <template v-if="profile.ranked">
            <FactRow v-if="profile.ranked.rating != null" label="Ranked rating">
              {{ profile.ranked.rating }}
            </FactRow>
            <FactRow v-if="profile.ranked.divisionName" label="Division">
              {{ profile.ranked.divisionName }}
            </FactRow>
            <FactRow v-if="profile.ranked.peakRating != null" label="Peak rating">
              {{ profile.ranked.peakRating }}
            </FactRow>
          </template>
        </template>
        <p v-else-if="loadingProfile" class="empty-state">Loading your profile…</p>
        <p v-else class="empty-state">Profile unavailable right now.</p>
      </PanelSection>

      <PanelSection title="📅 My last 7 days" data-testid="my-activity-panel">
        <template v-if="activity">
          <p class="stat-value">{{ formatXp(activity.totalXp) }}</p>
          <p class="stat-caption">
            missions done on {{ activity.numDaysDone }} of {{ activity.days.length }} days
          </p>
          <ul class="day-strip" data-testid="day-strip">
            <li
              v-for="day in activity.days"
              :key="day.date"
              class="day-cell"
              :class="{ done: day.missionCompleted }"
              :title="day.date"
            >
              <span class="day-label">{{ weekdayInitial(day.date) }}</span>
              <!-- Icon + color together: completion is never conveyed by color alone. -->
              <span class="day-mark">{{ day.missionCompleted ? '✓' : '·' }}</span>
            </li>
          </ul>
        </template>
        <p v-else class="empty-state">No activity data yet.</p>
      </PanelSection>

      <ErrorBanner v-if="errorMessage" class="error-banner-wide" data-testid="error-banner">
        {{ errorMessage }}
      </ErrorBanner>
    </template>
  </main>
</template>

<style scoped>
.day-strip {
  list-style: none;
  margin: 14px 0 0;
  padding: 0;
  display: flex;
  gap: 6px;
  flex-wrap: wrap;
}

.day-cell {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 2px;
  min-width: 34px;
  padding: 6px 4px;
  background: var(--bg-row);
  border: 1px solid transparent;
  border-radius: 10px;
}

.day-cell.done {
  background: var(--viewer);
  border-color: var(--viewer-border);
}

.day-label {
  color: var(--text-muted);
  font-size: 0.7rem;
  font-weight: 600;
}

.day-mark {
  font-weight: 700;
}

.day-cell.done .day-mark {
  color: var(--viewer-border);
}
</style>
