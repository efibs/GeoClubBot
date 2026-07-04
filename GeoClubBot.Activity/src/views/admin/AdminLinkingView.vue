<script setup lang="ts">
import { onMounted, ref } from 'vue';
import { storeToRefs } from 'pinia';
import { useAdminStore } from '../../stores/admin';
import { confirm } from '../../composables/useConfirm';
import Spinner from '../../components/Spinner.vue';
import type { AdminLinkRequestDto } from '../../types';

const admin = useAdminStore();
const { linking } = storeToRefs(admin);

// The request whose "Complete" form is open, plus the OTP the member sent via GeoGuessr DM.
const completingKey = ref<string | null>(null);
const otp = ref('');
// The request whose complete/cancel action is currently running, so only that row shows a spinner.
const pendingKey = ref<string | null>(null);

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
  pendingKey.value = keyOf(request);
  const ok = await admin.completeLinkRequest(request.discordUserId, request.geoGuessrUserId, otp.value.trim());
  pendingKey.value = null;
  if (ok) {
    completingKey.value = null;
    otp.value = '';
  }
}

async function cancel(request: AdminLinkRequestDto): Promise<void> {
  if (await confirm({ message: `Cancel the linking request of Discord user ${request.discordUserId}?`, danger: true })) {
    pendingKey.value = keyOf(request);
    await admin.cancelLinkRequest(request.discordUserId, request.geoGuessrUserId);
    pendingKey.value = null;
  }
}

async function unlink(): Promise<void> {
  const discordId = unlinkDiscordId.value.trim();
  const geoGuessrId = unlinkGeoGuessrId.value.trim();
  if (!discordId || !geoGuessrId) {
    return;
  }
  if (!(await confirm({ message: `Unlink Discord user ${discordId} from GeoGuessr account ${geoGuessrId}?`, danger: true }))) {
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
              <button
                type="button"
                class="action-button small"
                :disabled="linking.busy"
                data-testid="complete-confirm"
                @click="complete(request)"
              >
                <Spinner v-if="pendingKey === keyOf(request)" />
                {{ pendingKey === keyOf(request) ? 'Linking…' : 'Confirm' }}
              </button>
            </template>
            <template v-else>
              <button
                type="button"
                class="action-button small"
                :disabled="linking.busy"
                data-testid="link-complete"
                @click="openComplete(request)"
              >
                Complete
              </button>
              <button
                type="button"
                class="action-button danger small"
                :disabled="linking.busy"
                data-testid="link-request-cancel"
                @click="cancel(request)"
              >
                <Spinner v-if="pendingKey === keyOf(request)" />
                {{ pendingKey === keyOf(request) ? 'Cancelling…' : 'Cancel' }}
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
            :disabled="linking.busy || !unlinkDiscordId || !unlinkGeoGuessrId"
            data-testid="unlink-submit"
          >
            <Spinner v-if="linking.busy" />
            {{ linking.busy ? 'Unlinking…' : 'Unlink' }}
          </button>
        </div>
      </form>
    </section>
  </main>
</template>
