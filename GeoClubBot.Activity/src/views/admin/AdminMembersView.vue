<script setup lang="ts">
import { ref } from 'vue';
import PanelSection from '../../components/PanelSection.vue';
import FactRow from '../../components/FactRow.vue';
import FormField from '../../components/FormField.vue';
import ActionButton from '../../components/ActionButton.vue';
import ErrorBanner from '../../components/ErrorBanner.vue';
import LoadingSpinner from '../../components/LoadingSpinner.vue';
import { useAdminMemberLookup, useClubStatsQuery } from '../../queries/admin';
import { formatDate, formatXp } from '../../format';

const lookup = useAdminMemberLookup();
const { data: clubStats } = useClubStatsQuery();

const nicknameInput = ref(lookup.nickname);

function search(): void {
  const value = nicknameInput.value.trim();
  if (value) {
    lookup.search(value);
  }
}
</script>

<template>
  <main class="panels" data-testid="admin-members-view">
    <PanelSection title="📊 Club statistics" data-testid="club-stats-panel">
      <template v-if="clubStats">
        <FactRow label="Average of member averages">
          {{ formatXp(clubStats.averageAveragePoints) }}
        </FactRow>
        <FactRow label="Median">{{ formatXp(clubStats.medianAveragePoints) }}</FactRow>
        <FactRow label="Quartiles (25% / 75%)">
          {{ formatXp(clubStats.firstQuartileAveragePoints) }} /
          {{ formatXp(clubStats.thirdQuartileAveragePoints) }}
        </FactRow>
        <FactRow label="Range">
          {{ formatXp(clubStats.minAveragePoints) }} – {{ formatXp(clubStats.maxAveragePoints) }}
        </FactRow>
      </template>
      <p v-else class="empty-state" data-testid="club-stats-empty">No club history recorded yet.</p>
    </PanelSection>

    <PanelSection title="🔍 Member lookup" data-testid="member-lookup-panel">
      <form class="form-stack" @submit.prevent="search">
        <FormField
          id="lookup-nickname"
          v-model="nicknameInput"
          label="GeoGuessr nickname"
          placeholder="Exact nickname"
          required
          data-testid="lookup-input"
        />
        <div class="form-actions">
          <ActionButton
            type="submit"
            :busy="lookup.loading"
            busy-label="Looking up…"
            :disabled="lookup.loading || !nicknameInput"
            data-testid="lookup-submit"
          >
            Look up
          </ActionButton>
        </div>
      </form>
      <p v-if="lookup.loading" class="loading-inline" data-testid="lookup-loading">
        <LoadingSpinner /> Fetching live stats from GeoGuessr — this can take a moment…
      </p>
      <ErrorBanner v-if="lookup.error" data-testid="lookup-error">{{ lookup.error }}</ErrorBanner>
    </PanelSection>

    <template v-if="lookup.data">
      <PanelSection
        :title="`⚡ ${lookup.data.nickname} — strikes`"
        data-testid="lookup-strikes-panel"
      >
        <template v-if="lookup.data.strikes">
          <p class="stat-value">{{ lookup.data.strikes.numActiveStrikes }}</p>
          <p class="stat-caption">active strikes</p>
          <ul v-if="lookup.data.strikes.strikes.length > 0" class="rows">
            <li v-for="strike in lookup.data.strikes.strikes" :key="strike.strikeId" class="row">
              <span class="rank">{{ strike.revoked ? '↩️' : '⚡' }}</span>
              <span class="name">{{ formatDate(strike.timestamp) }}</span>
              <span class="sub">
                {{ strike.revoked ? 'revoked' : `expires ${formatDate(strike.expiresAt)}` }}
              </span>
            </li>
          </ul>
        </template>
        <p v-else class="empty-state">No strike data for this member.</p>
      </PanelSection>

      <PanelSection
        :title="`📅 ${lookup.data.nickname} — last 7 days`"
        data-testid="lookup-activity-panel"
      >
        <template v-if="lookup.data.activity">
          <p class="stat-value">{{ formatXp(lookup.data.activity.totalXp) }}</p>
          <!-- Same daily-only figure as the member's own view; weekly missions are excluded. -->
          <p class="stat-caption">
            excluding weekly missions · fully done on {{ lookup.data.activity.numDaysDone }} of
            {{ lookup.data.activity.days.length }} days
          </p>
        </template>
        <p v-else class="empty-state">No activity data for this member.</p>
      </PanelSection>

      <PanelSection
        :title="`📈 ${lookup.data.nickname} — history`"
        data-testid="lookup-stats-panel"
      >
        <template v-if="lookup.data.statistics">
          <FactRow label="Average">{{ formatXp(lookup.data.statistics.averagePoints) }}</FactRow>
          <FactRow label="Median">{{ formatXp(lookup.data.statistics.medianPoints) }}</FactRow>
          <FactRow label="Range">
            {{ formatXp(lookup.data.statistics.minPoints) }} –
            {{ formatXp(lookup.data.statistics.maxPoints) }}
          </FactRow>
          <FactRow label="Entries since">
            {{ lookup.data.statistics.numHistoryEntries }} ·
            {{ formatDate(lookup.data.statistics.historySince) }}
          </FactRow>
        </template>
        <p v-else class="empty-state">No recorded history for this member.</p>
      </PanelSection>
    </template>
  </main>
</template>

<style scoped>
.loading-inline {
  display: flex;
  align-items: center;
  gap: 8px;
  color: var(--text-muted);
  margin: 8px 0 0;
}
</style>
