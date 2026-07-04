<script setup lang="ts">
import { onMounted, watch } from 'vue';
import { storeToRefs } from 'pinia';
import LinkAccountPanel from '../components/LinkAccountPanel.vue';
import { useSessionStore } from '../stores/session';
import { useProfileStore } from '../stores/profile';
import { countryFlag, formatXp, weekdayInitial } from '../format';
import { usePolling } from '../composables/usePolling';

const session = useSessionStore();
const profileStore = useProfileStore();
const { profile, activity, loading, error } = storeToRefs(profileStore);
const { isLinked } = storeToRefs(session);

function loadIfLinked(showSpinner: boolean): void {
  if (session.isLinked) {
    void profileStore.load(showSpinner);
  }
}

onMounted(() => {
  loadIfLinked(profile.value === null);
});

usePolling(() => {
  loadIfLinked(false);
});

// If the account gets linked while this tab is open (e.g. an admin completes the request), the
// profile appears without a manual refresh.
watch(isLinked, (linked) => {
  if (linked) {
    void profileStore.load(true);
  }
});
</script>

<template>
  <main class="panels" data-testid="me-view">
    <LinkAccountPanel v-if="!isLinked" />

    <template v-else>
      <section class="panel" data-testid="profile-panel">
        <h2 class="panel-title">👤 {{ profile?.nickname ?? session.nickname }}</h2>
        <template v-if="profile">
          <div class="fact-row">
            <span>Country</span>
            <strong>{{ countryFlag(profile.countryCode) }} {{ profile.countryCode.toUpperCase() }}</strong>
          </div>
          <div v-if="profile.level != null" class="fact-row">
            <span>Level</span>
            <strong>{{ profile.level }}</strong>
          </div>
          <div class="fact-row">
            <span>Playing since</span>
            <strong>{{ new Date(profile.createdAt).getFullYear() }}</strong>
          </div>
          <div v-if="profile.isProUser" class="fact-row">
            <span>Subscription</span>
            <strong>Pro</strong>
          </div>
          <template v-if="profile.ranked">
            <div v-if="profile.ranked.rating != null" class="fact-row">
              <span>Ranked rating</span>
              <strong>{{ profile.ranked.rating }}</strong>
            </div>
            <div v-if="profile.ranked.divisionName" class="fact-row">
              <span>Division</span>
              <strong>{{ profile.ranked.divisionName }}</strong>
            </div>
            <div v-if="profile.ranked.peakRating != null" class="fact-row">
              <span>Peak rating</span>
              <strong>{{ profile.ranked.peakRating }}</strong>
            </div>
          </template>
        </template>
        <p v-else-if="loading" class="empty-state">Loading your profile…</p>
        <p v-else class="empty-state">Profile unavailable right now.</p>
      </section>

      <section class="panel" data-testid="my-activity-panel">
        <h2 class="panel-title">📅 My last 7 days</h2>
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
      </section>

      <p v-if="error" class="error-banner error-banner-wide" data-testid="error-banner">⚠️ {{ error }}</p>
    </template>
  </main>
</template>
