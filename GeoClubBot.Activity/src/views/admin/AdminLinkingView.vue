<script setup lang="ts">
import { onMounted, ref } from 'vue';
import { storeToRefs } from 'pinia';
import { useAdminStore } from '../../stores/admin';
import type { AdminLinkRequestDto } from '../../types';

const admin = useAdminStore();
const { linking } = storeToRefs(admin);

// The request whose "Complete" form is open, plus the OTP the member sent via GeoGuessr DM.
const completingKey = ref<string | null>(null);
const otp = ref('');

const unlinkDiscordId = ref('');
const unlinkGeoGuessrId = ref('');

onMounted(() => {
  if (!linking.value.data) {
    void admin.loadLinking();
  }
});

function keyOf(request: AdminLinkRequestDto): string {
  return `${request.discordUserId}:${request.geoGuessrUserId}`;
}

function openComplete(request: AdminLinkRequestDto): void {
  completingKey.value = keyOf(request);
  otp.value = '';
}

async function complete(request: AdminLinkRequestDto): Promise<void> {
  if (!otp.value.trim()) {
    return;
  }
  if (await admin.completeLinkRequest(request.discordUserId, request.geoGuessrUserId, otp.value.trim())) {
    completingKey.value = null;
    otp.value = '';
  }
}

function cancel(request: AdminLinkRequestDto): void {
  if (window.confirm(`Cancel the linking request of Discord user ${request.discordUserId}?`)) {
    void admin.cancelLinkRequest(request.discordUserId, request.geoGuessrUserId);
  }
}

async function unlink(): Promise<void> {
  const discordId = unlinkDiscordId.value.trim();
  const geoGuessrId = unlinkGeoGuessrId.value.trim();
  if (!discordId || !geoGuessrId) {
    return;
  }
  if (!window.confirm(`Unlink Discord user ${discordId} from GeoGuessr account ${geoGuessrId}?`)) {
    return;
  }
  if (await admin.unlinkAccounts(discordId, geoGuessrId)) {
    unlinkDiscordId.value = '';
    unlinkGeoGuessrId.value = '';
  }
}
</script>

<template>
  <main class="panels" data-testid="admin-linking-view">
    <p v-if="linking.error" class="error-banner" data-testid="error-banner">⚠️ {{ linking.error }}</p>

    <section class="panel panel-wide" data-testid="link-requests-panel">
      <h2 class="panel-title">🔗 Open linking requests</h2>
      <p class="stat-caption">
        Complete a request only after the member sent you their one-time password as a direct
        message <strong>inside GeoGuessr</strong> — that's what proves they own the account.
      </p>
      <ul v-if="linking.data && linking.data.length > 0" class="rows">
        <li
          v-for="request in linking.data"
          :key="keyOf(request)"
          class="row row-with-actions"
          :data-testid="`link-request-${request.discordUserId}`"
        >
          <span class="rank">🔗</span>
          <span class="name">Discord {{ request.discordUserId }}</span>
          <span class="value">{{ request.geoGuessrUserId }}</span>
          <span class="row-actions">
            <template v-if="completingKey === keyOf(request)">
              <input
                v-model="otp"
                type="text"
                placeholder="One-time password"
                class="field-input small"
                data-testid="complete-otp-input"
                @keyup.enter="complete(request)"
              />
              <button type="button" class="action-button small" data-testid="complete-confirm" @click="complete(request)">
                Confirm
              </button>
            </template>
            <template v-else>
              <button type="button" class="action-button small" data-testid="link-complete" @click="openComplete(request)">
                Complete
              </button>
              <button type="button" class="action-button danger small" data-testid="link-request-cancel" @click="cancel(request)">
                Cancel
              </button>
            </template>
          </span>
        </li>
      </ul>
      <p v-else-if="linking.data" class="empty-state" data-testid="link-requests-empty">
        No open linking requests.
      </p>
      <p v-else-if="linking.loading" class="empty-state">Loading…</p>
    </section>

    <section class="panel" data-testid="unlink-panel">
      <h2 class="panel-title">✂️ Unlink accounts</h2>
      <form class="reminder-form" @submit.prevent="unlink">
        <label class="field-label" for="unlink-discord">Discord user id</label>
        <input
          id="unlink-discord"
          v-model="unlinkDiscordId"
          type="text"
          required
          class="field-input"
          data-testid="unlink-discord-input"
        />
        <label class="field-label" for="unlink-gg">GeoGuessr user id</label>
        <input
          id="unlink-gg"
          v-model="unlinkGeoGuessrId"
          type="text"
          required
          class="field-input"
          data-testid="unlink-gg-input"
        />
        <div class="form-actions">
          <button
            type="submit"
            class="action-button danger"
            :disabled="linking.loading || !unlinkDiscordId || !unlinkGeoGuessrId"
            data-testid="unlink-submit"
          >
            Unlink
          </button>
        </div>
      </form>
    </section>
  </main>
</template>
