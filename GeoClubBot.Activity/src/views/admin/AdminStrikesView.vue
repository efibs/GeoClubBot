<script setup lang="ts">
import { ref } from 'vue';
import PanelSection from '../../components/PanelSection.vue';
import FormField from '../../components/FormField.vue';
import ActionButton from '../../components/ActionButton.vue';
import ErrorBanner from '../../components/ErrorBanner.vue';
import { useAdminStrikes } from '../../queries/admin';
import { formatDate, todayAsDateInputValue } from '../../format';
import { confirm } from '../../composables/useConfirm';

const strikes = useAdminStrikes();

const newStrikeNickname = ref('');
const newStrikeDate = ref(todayAsDateInputValue());

async function addStrike(): Promise<void> {
  const nickname = newStrikeNickname.value.trim();
  if (!nickname || !newStrikeDate.value) {
    return;
  }
  if (!(await confirm({ message: `Add a strike to ${nickname}?`, danger: true }))) {
    return;
  }
  try {
    await strikes.add(nickname, newStrikeDate.value);
    newStrikeNickname.value = '';
  } catch {
    // Surfaced via strikes.error.
  }
}

async function revoke(strikeId: string, nickname: string): Promise<void> {
  if (await confirm({ message: `Revoke this strike of ${nickname}?`, danger: true })) {
    await strikes.revoke(strikeId).catch(() => {});
  }
}

async function unrevoke(strikeId: string, nickname: string): Promise<void> {
  if (await confirm(`Restore this strike of ${nickname}?`)) {
    await strikes.unrevoke(strikeId).catch(() => {});
  }
}
</script>

<template>
  <main class="panels" data-testid="admin-strikes-view">
    <ErrorBanner v-if="strikes.error" data-testid="error-banner">{{ strikes.error }}</ErrorBanner>

    <PanelSection title="🚨 Members with active strikes" data-testid="relevant-strikes-panel">
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
      <p v-else-if="strikes.isPending" class="empty-state">Loading…</p>
    </PanelSection>

    <PanelSection title="➕ Add strike" data-testid="add-strike-panel">
      <form class="form-stack" @submit.prevent="addStrike">
        <FormField
          id="strike-nickname"
          v-model="newStrikeNickname"
          label="GeoGuessr nickname"
          required
          data-testid="add-strike-nickname"
        />
        <FormField
          id="strike-date"
          v-model="newStrikeDate"
          label="Strike date"
          type="date"
          required
          data-testid="add-strike-date"
        />
        <div class="form-actions">
          <ActionButton
            type="submit"
            :busy="strikes.addPending"
            busy-label="Adding…"
            :disabled="strikes.busy || !newStrikeNickname"
            data-testid="add-strike-submit"
          >
            Add strike
          </ActionButton>
        </div>
      </form>
    </PanelSection>

    <PanelSection title="📜 All strikes" wide data-testid="all-strikes-panel">
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
            <ActionButton
              v-if="!strike.revoked"
              danger
              small
              :busy="strikes.pendingStrikeId === strike.strikeId"
              busy-label="Revoking…"
              :disabled="strikes.busy"
              data-testid="strike-revoke"
              @click="revoke(strike.strikeId, strike.nickname)"
            >
              Revoke
            </ActionButton>
            <ActionButton
              v-else
              small
              :busy="strikes.pendingStrikeId === strike.strikeId"
              busy-label="Restoring…"
              :disabled="strikes.busy"
              data-testid="strike-unrevoke"
              @click="unrevoke(strike.strikeId, strike.nickname)"
            >
              Restore
            </ActionButton>
          </span>
        </li>
      </ul>
      <p v-else-if="strikes.data" class="empty-state" data-testid="all-strikes-empty">
        No strikes recorded.
      </p>
      <p v-else-if="strikes.isPending" class="empty-state">Loading…</p>
    </PanelSection>
  </main>
</template>
