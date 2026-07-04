<script setup lang="ts">
import { computed, ref } from 'vue';
import { storeToRefs } from 'pinia';
import { useSessionStore } from '../stores/session';
import { cancelLinkRequest, startLinkRequest } from '../api';

const session = useSessionStore();
const { openLinkRequest } = storeToRefs(session);

const profileLink = ref('');
const busy = ref(false);
const error = ref<string | null>(null);
const copied = ref(false);

// Accepts either the raw 24-hex GeoGuessr user id or a profile share link ending in /user/<id>.
const parsedUserId = computed(() => {
  const match = profileLink.value.trim().match(/([a-f0-9]{24})\s*$/i);
  return match ? match[1].toLowerCase() : null;
});

async function start(): Promise<void> {
  if (!parsedUserId.value) {
    error.value = 'That doesn’t look like a GeoGuessr profile link. It should end in /user/<24 characters>.';
    return;
  }
  busy.value = true;
  error.value = null;
  try {
    await startLinkRequest(parsedUserId.value);
    // Reload the session so the open request (and its OTP) comes from the server.
    await session.loadMe();
    profileLink.value = '';
  } catch (err) {
    error.value = err instanceof Error ? err.message : 'Failed to start the linking process.';
  } finally {
    busy.value = false;
  }
}

async function cancel(): Promise<void> {
  busy.value = true;
  error.value = null;
  try {
    await cancelLinkRequest();
    await session.loadMe();
  } catch (err) {
    error.value = err instanceof Error ? err.message : 'Failed to cancel the linking request.';
  } finally {
    busy.value = false;
  }
}

async function copyOtp(): Promise<void> {
  if (!openLinkRequest.value) {
    return;
  }
  await navigator.clipboard.writeText(openLinkRequest.value.oneTimePassword);
  copied.value = true;
  setTimeout(() => {
    copied.value = false;
  }, 2000);
}
</script>

<template>
  <section class="panel" data-testid="link-account-panel">
    <h2 class="panel-title">🔗 Link your GeoGuessr account</h2>

    <template v-if="openLinkRequest">
      <p data-testid="link-otp-intro">
        Your linking request for GeoGuessr account
        <strong>{{ openLinkRequest.geoGuessrUserId }}</strong> is waiting for an admin. Send this
        one-time password to a club admin as a <strong>direct message inside GeoGuessr</strong> —
        never in Discord — and they'll complete the link:
      </p>
      <p class="otp-display" data-testid="link-otp">
        <code>{{ openLinkRequest.oneTimePassword }}</code>
        <button type="button" class="action-button" data-testid="link-otp-copy" @click="copyOtp">
          {{ copied ? 'Copied!' : 'Copy' }}
        </button>
      </p>
      <div class="form-actions">
        <button
          type="button"
          class="action-button danger"
          :disabled="busy"
          data-testid="link-cancel"
          @click="cancel"
        >
          Cancel request
        </button>
      </div>
    </template>

    <template v-else>
      <p class="stat-caption">
        Open your GeoGuessr profile, hit the share button and paste the link here (it looks like
        https://www.geoguessr.com/user/62c353a29d0d57e7b9a3383f).
      </p>
      <form class="reminder-form" @submit.prevent="start">
        <label class="field-label" for="profile-link">Profile link</label>
        <input
          id="profile-link"
          v-model="profileLink"
          type="text"
          required
          placeholder="https://www.geoguessr.com/user/…"
          class="field-input"
          data-testid="link-profile-input"
        />
        <div class="form-actions">
          <button type="submit" class="action-button" :disabled="busy || !profileLink" data-testid="link-start">
            Start linking
          </button>
        </div>
      </form>
    </template>

    <p v-if="error" class="error-banner" data-testid="link-error">⚠️ {{ error }}</p>
  </section>
</template>
