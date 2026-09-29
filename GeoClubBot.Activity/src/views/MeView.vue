<script setup lang="ts">
import { computed } from 'vue';
import PanelSection from '../components/PanelSection.vue';
import FactRow from '../components/FactRow.vue';
import LinkAccountPanel from '../components/LinkAccountPanel.vue';
import ErrorBanner from '../components/ErrorBanner.vue';
import { useSession } from '../queries/session';
import { useMyActivityQuery, useMyCurrentActivityQuery, useProfileQuery } from '../queries/profile';
import { countryFlag, formatXp, weekdayInitial } from '../format';
import { toErrorMessage } from '../api';
import type { DayActivityDto } from '../types';

// The streak is the tick; board missions finished that day are counted beneath it.
function dayTitle(day: DayActivityDto): string {
  const streak = `streak: ${day.challengeDone ? 'kept' : 'not kept'}`;
  return `${day.date} — ${streak}, club missions: ${day.boardMissions}`;
}

const { isLinked, nickname } = useSession();

// The profile/activity queries are enabled only while linked; if the account gets linked while this
// tab is open (an admin completes the request), `isLinked` flips and they fetch on their own.
const profileQuery = useProfileQuery(isLinked);
const activityQuery = useMyActivityQuery(isLinked);
const currentQuery = useMyCurrentActivityQuery(isLinked);
const { data: profile } = profileQuery;
const { data: activity } = activityQuery;
const { data: current } = currentQuery;
const loadingProfile = profileQuery.isPending;

const errorMessage = computed(() => {
  const err = profileQuery.error.value ?? activityQuery.error.value ?? currentQuery.error.value;
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

      <PanelSection title="📋 This week" data-testid="my-week-panel">
        <template v-if="current">
          <ul class="requirements" data-testid="requirements">
            <!-- Met/missed is shown by the mark as well as the colour. -->
            <li
              v-for="requirement in current.requirements"
              :key="requirement.label"
              class="requirement"
              :class="{ met: requirement.met }"
            >
              <span>{{ requirement.met ? '✅' : '⬜' }} {{ requirement.label }}</span>
              <strong>{{ requirement.actual }} / {{ requirement.target }}</strong>
            </li>
          </ul>
          <FactRow v-if="current.ruleXp != null" label="Rule XP">{{
            formatXp(current.ruleXp)
          }}</FactRow>
          <FactRow v-if="current.helpedThisWeek != null" label="Helped out (unverified)">
            {{ current.helpedThisWeek }}
          </FactRow>
          <p class="stat-caption">since the last weekly check</p>
        </template>
        <p v-else class="empty-state">No activity data yet.</p>
      </PanelSection>

      <PanelSection title="📅 My last 7 days" data-testid="my-activity-panel">
        <template v-if="activity">
          <p class="stat-value">{{ formatXp(activity.totalXp) }}</p>
          <p class="stat-caption">
            streak kept on {{ activity.numChallengeDaysDone }} of {{ activity.days.length }} days ·
            {{ activity.boardMissions }} club mission(s)
            <template v-if="activity.boardClearBonusXp > 0">
              · {{ formatXp(activity.boardClearBonusXp) }} board bonus
            </template>
          </p>
          <ul class="day-strip" data-testid="day-strip">
            <li
              v-for="day in activity.days"
              :key="day.date"
              class="day-cell"
              :class="{ done: day.challengeDone }"
              :title="dayTitle(day)"
            >
              <span class="day-label">{{ weekdayInitial(day.date) }}</span>
              <!-- Icon + color together: the streak is never conveyed by color alone. -->
              <span class="day-mark">{{ day.challengeDone ? '✓' : '·' }}</span>
              <span class="day-missions">{{
                day.boardMissions > 0 ? `🎯${day.boardMissions}` : ''
              }}</span>
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

.day-missions {
  min-height: 1em;
  font-size: 0.7rem;
}

.requirements {
  list-style: none;
  margin: 0 0 8px;
  padding: 0;
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.requirement {
  display: flex;
  justify-content: space-between;
  padding: 8px 12px;
  border-radius: 10px;
  background: var(--bg-row);
  border: 1px solid transparent;
}

.requirement.met {
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
