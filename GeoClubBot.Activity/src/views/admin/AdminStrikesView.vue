<script setup lang="ts">
import { onMounted, ref } from 'vue';
import { storeToRefs } from 'pinia';
import { useAdminStore } from '../../stores/admin';
import { formatDate } from '../../format';

const admin = useAdminStore();
const { strikes } = storeToRefs(admin);

const newStrikeNickname = ref('');
const newStrikeDate = ref(new Date().toISOString().slice(0, 10));

onMounted(() => {
  if (!strikes.value.data) {
    void admin.loadStrikes();
  }
});

async function addStrike(): Promise<void> {
  const nickname = newStrikeNickname.value.trim();
  if (!nickname || !newStrikeDate.value) {
    return;
  }
  if (!window.confirm(`Add a strike to ${nickname}?`)) {
    return;
  }
  if (await admin.addStrike(nickname, newStrikeDate.value)) {
    newStrikeNickname.value = '';
  }
}

function revoke(strikeId: string, nickname: string): void {
  if (window.confirm(`Revoke this strike of ${nickname}?`)) {
    void admin.revokeStrike(strikeId);
  }
}

function unrevoke(strikeId: string, nickname: string): void {
  if (window.confirm(`Restore this strike of ${nickname}?`)) {
    void admin.unrevokeStrike(strikeId);
  }
}
</script>

<template>
  <main class="panels" data-testid="admin-strikes-view">
    <p v-if="strikes.error" class="error-banner" data-testid="error-banner">⚠️ {{ strikes.error }}</p>

    <section class="panel" data-testid="relevant-strikes-panel">
      <h2 class="panel-title">🚨 Members with active strikes</h2>
      <ul v-if="strikes.data && strikes.data.relevant.length > 0" class="rows">
        <li v-for="entry in strikes.data.relevant" :key="entry.nickname" class="row">
          <span class="rank">⚠️</span>
          <span class="name">{{ entry.nickname }}</span>
          <span class="value">{{ entry.numActiveStrikes }}</span>
        </li>
      </ul>
      <p v-else-if="strikes.data" class="empty-state" data-testid="relevant-strikes-empty">
        Nobody has an active strike. 🎉
      </p>
      <p v-else-if="strikes.loading" class="empty-state">Loading…</p>
    </section>

    <section class="panel" data-testid="add-strike-panel">
      <h2 class="panel-title">➕ Add strike</h2>
      <form class="reminder-form" @submit.prevent="addStrike">
        <label class="field-label" for="strike-nickname">GeoGuessr nickname</label>
        <input
          id="strike-nickname"
          v-model="newStrikeNickname"
          type="text"
          required
          class="field-input"
          data-testid="add-strike-nickname"
        />
        <label class="field-label" for="strike-date">Strike date</label>
        <input
          id="strike-date"
          v-model="newStrikeDate"
          type="date"
          required
          class="field-input"
          data-testid="add-strike-date"
        />
        <div class="form-actions">
          <button
            type="submit"
            class="action-button"
            :disabled="strikes.loading || !newStrikeNickname"
            data-testid="add-strike-submit"
          >
            Add strike
          </button>
        </div>
      </form>
    </section>

    <section class="panel panel-wide" data-testid="all-strikes-panel">
      <h2 class="panel-title">📜 All strikes</h2>
      <ul v-if="strikes.data && strikes.data.all.length > 0" class="rows">
        <li
          v-for="strike in strikes.data.all"
          :key="strike.strikeId"
          class="row row-with-actions"
          :class="{ revoked: strike.revoked }"
          :data-testid="`strike-${strike.strikeId}`"
        >
          <span class="rank">{{ strike.revoked ? '↩️' : '⚡' }}</span>
          <span class="name">{{ strike.nickname }}</span>
          <span class="value">{{ formatDate(strike.timestamp) }}</span>
          <span class="sub">
            {{ strike.revoked ? 'revoked' : `expires ${formatDate(strike.expiresAt)}` }}
          </span>
          <span class="row-actions">
            <button
              v-if="!strike.revoked"
              type="button"
              class="action-button danger small"
              data-testid="strike-revoke"
              @click="revoke(strike.strikeId, strike.nickname)"
            >
              Revoke
            </button>
            <button
              v-else
              type="button"
              class="action-button small"
              data-testid="strike-unrevoke"
              @click="unrevoke(strike.strikeId, strike.nickname)"
            >
              Restore
            </button>
          </span>
        </li>
      </ul>
      <p v-else-if="strikes.data" class="empty-state" data-testid="all-strikes-empty">No strikes recorded.</p>
      <p v-else-if="strikes.loading" class="empty-state">Loading…</p>
    </section>
  </main>
</template>
