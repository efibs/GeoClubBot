<script setup lang="ts">
import { onMounted, ref } from 'vue';
import { storeToRefs } from 'pinia';
import { useAdminStore } from '../../stores/admin';
import { formatDate, formatXp } from '../../format';

const admin = useAdminStore();
const { lookup, clubStats } = storeToRefs(admin);

const nickname = ref(lookup.value.data?.nickname ?? '');

onMounted(() => {
  if (!clubStats.value.data) {
    void admin.loadClubStats();
  }
});

function search(): void {
  const value = nickname.value.trim();
  if (value) {
    void admin.lookupMember(value);
  }
}
</script>

<template>
  <main class="panels" data-testid="admin-members-view">
    <section class="panel" data-testid="club-stats-panel">
      <h2 class="panel-title">📊 Club statistics</h2>
      <template v-if="clubStats.data">
        <div class="fact-row">
          <span>Average of member averages</span>
          <strong>{{ formatXp(clubStats.data.averageAveragePoints) }}</strong>
        </div>
        <div class="fact-row">
          <span>Median</span>
          <strong>{{ formatXp(clubStats.data.medianAveragePoints) }}</strong>
        </div>
        <div class="fact-row">
          <span>Quartiles (25% / 75%)</span>
          <strong>
            {{ formatXp(clubStats.data.firstQuartileAveragePoints) }} /
            {{ formatXp(clubStats.data.thirdQuartileAveragePoints) }}
          </strong>
        </div>
        <div class="fact-row">
          <span>Range</span>
          <strong>
            {{ formatXp(clubStats.data.minAveragePoints) }} – {{ formatXp(clubStats.data.maxAveragePoints) }}
          </strong>
        </div>
      </template>
      <p v-else class="empty-state" data-testid="club-stats-empty">No club history recorded yet.</p>
    </section>

    <section class="panel" data-testid="member-lookup-panel">
      <h2 class="panel-title">🔍 Member lookup</h2>
      <form class="reminder-form" @submit.prevent="search">
        <label class="field-label" for="lookup-nickname">GeoGuessr nickname</label>
        <input
          id="lookup-nickname"
          v-model="nickname"
          type="text"
          required
          placeholder="Exact nickname"
          class="field-input"
          data-testid="lookup-input"
        />
        <div class="form-actions">
          <button type="submit" class="action-button" :disabled="lookup.loading || !nickname" data-testid="lookup-submit">
            Look up
          </button>
        </div>
      </form>
      <p v-if="lookup.error" class="error-banner" data-testid="lookup-error">⚠️ {{ lookup.error }}</p>
    </section>

    <template v-if="lookup.data">
      <section class="panel" data-testid="lookup-strikes-panel">
        <h2 class="panel-title">⚡ {{ lookup.data.nickname }} — strikes</h2>
        <template v-if="lookup.data.strikes">
          <p class="stat-value">{{ lookup.data.strikes.numActiveStrikes }}</p>
          <p class="stat-caption">active strikes</p>
          <ul v-if="lookup.data.strikes.strikes.length > 0" class="rows">
            <li v-for="strike in lookup.data.strikes.strikes" :key="strike.strikeId" class="row">
              <span class="rank">{{ strike.revoked ? '↩️' : '⚡' }}</span>
              <span class="name">{{ formatDate(strike.timestamp) }}</span>
              <span class="sub">{{ strike.revoked ? 'revoked' : `expires ${formatDate(strike.expiresAt)}` }}</span>
            </li>
          </ul>
        </template>
        <p v-else class="empty-state">No strike data for this member.</p>
      </section>

      <section class="panel" data-testid="lookup-activity-panel">
        <h2 class="panel-title">📅 {{ lookup.data.nickname }} — last 7 days</h2>
        <template v-if="lookup.data.activity">
          <p class="stat-value">{{ formatXp(lookup.data.activity.totalXp) }}</p>
          <p class="stat-caption">
            missions done on {{ lookup.data.activity.numDaysDone }} of
            {{ lookup.data.activity.days.length }} days
          </p>
        </template>
        <p v-else class="empty-state">No activity data for this member.</p>
      </section>

      <section class="panel" data-testid="lookup-stats-panel">
        <h2 class="panel-title">📈 {{ lookup.data.nickname }} — history</h2>
        <template v-if="lookup.data.statistics">
          <div class="fact-row">
            <span>Average</span>
            <strong>{{ formatXp(lookup.data.statistics.averagePoints) }}</strong>
          </div>
          <div class="fact-row">
            <span>Median</span>
            <strong>{{ formatXp(lookup.data.statistics.medianPoints) }}</strong>
          </div>
          <div class="fact-row">
            <span>Range</span>
            <strong>
              {{ formatXp(lookup.data.statistics.minPoints) }} –
              {{ formatXp(lookup.data.statistics.maxPoints) }}
            </strong>
          </div>
          <div class="fact-row">
            <span>Entries since</span>
            <strong>
              {{ lookup.data.statistics.numHistoryEntries }} ·
              {{ formatDate(lookup.data.statistics.historySince) }}
            </strong>
          </div>
        </template>
        <p v-else class="empty-state">No recorded history for this member.</p>
      </section>
    </template>
  </main>
</template>
