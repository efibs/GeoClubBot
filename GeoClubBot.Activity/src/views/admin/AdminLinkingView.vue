<script setup lang="ts">
import { ref } from 'vue';
import PanelSection from '../../components/PanelSection.vue';
import FormField from '../../components/FormField.vue';
import ActionButton from '../../components/ActionButton.vue';
import ErrorBanner from '../../components/ErrorBanner.vue';
import { useAdminLinking } from '../../queries/admin';
import { confirm } from '../../composables/useConfirm';
import type { AdminLinkRequestDto } from '../../types';

const linking = useAdminLinking();

// The request whose "Complete" form is open, plus the OTP the member sent via GeoGuessr DM.
const completingKey = ref<string | null>(null);
const otp = ref('');

const unlinkDiscordId = ref('');
const unlinkGeoGuessrId = ref('');

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
  try {
    await linking.complete(request.discordUserId, request.geoGuessrUserId, otp.value.trim());
    completingKey.value = null;
    otp.value = '';
  } catch {
    // Surfaced via linking.error.
  }
}

async function cancel(request: AdminLinkRequestDto): Promise<void> {
  if (
    await confirm({
      message: `Cancel the linking request of Discord user ${request.discordUserId}?`,
      danger: true,
    })
  ) {
    await linking.cancel(request.discordUserId, request.geoGuessrUserId).catch(() => {});
  }
}

async function unlink(): Promise<void> {
  const discordId = unlinkDiscordId.value.trim();
  const geoGuessrId = unlinkGeoGuessrId.value.trim();
  if (!discordId || !geoGuessrId) {
    return;
  }
  if (
    !(await confirm({
      message: `Unlink Discord user ${discordId} from GeoGuessr account ${geoGuessrId}?`,
      danger: true,
    }))
  ) {
    return;
  }
  try {
    await linking.unlink(discordId, geoGuessrId);
    unlinkDiscordId.value = '';
    unlinkGeoGuessrId.value = '';
  } catch {
    // Surfaced via linking.error.
  }
}
</script>

<template>
  <main class="panels" data-testid="admin-linking-view">
    <ErrorBanner v-if="linking.error" data-testid="error-banner">{{ linking.error }}</ErrorBanner>

    <PanelSection title="🔗 Open linking requests" wide data-testid="link-requests-panel">
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
              <ActionButton
                small
                :busy="linking.pendingKey === keyOf(request)"
                busy-label="Linking…"
                :disabled="linking.busy"
                data-testid="complete-confirm"
                @click="complete(request)"
              >
                Confirm
              </ActionButton>
            </template>
            <template v-else>
              <ActionButton
                small
                :disabled="linking.busy"
                data-testid="link-complete"
                @click="openComplete(request)"
              >
                Complete
              </ActionButton>
              <ActionButton
                danger
                small
                :busy="linking.pendingKey === keyOf(request)"
                busy-label="Cancelling…"
                :disabled="linking.busy"
                data-testid="link-request-cancel"
                @click="cancel(request)"
              >
                Cancel
              </ActionButton>
            </template>
          </span>
        </li>
      </ul>
      <p v-else-if="linking.data" class="empty-state" data-testid="link-requests-empty">
        No open linking requests.
      </p>
      <p v-else-if="linking.isPending" class="empty-state">Loading…</p>
    </PanelSection>

    <PanelSection title="✂️ Unlink accounts" data-testid="unlink-panel">
      <form class="form-stack" @submit.prevent="unlink">
        <FormField
          id="unlink-discord"
          v-model="unlinkDiscordId"
          label="Discord user id"
          required
          data-testid="unlink-discord-input"
        />
        <FormField
          id="unlink-gg"
          v-model="unlinkGeoGuessrId"
          label="GeoGuessr user id"
          required
          data-testid="unlink-gg-input"
        />
        <div class="form-actions">
          <ActionButton
            type="submit"
            danger
            :busy="linking.unlinking"
            busy-label="Unlinking…"
            :disabled="linking.busy || !unlinkDiscordId || !unlinkGeoGuessrId"
            data-testid="unlink-submit"
          >
            Unlink
          </ActionButton>
        </div>
      </form>
    </PanelSection>
  </main>
</template>
