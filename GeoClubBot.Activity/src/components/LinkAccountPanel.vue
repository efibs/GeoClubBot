<script setup lang="ts">
import { computed, ref } from 'vue';
import PanelSection from './PanelSection.vue';
import FormField from './FormField.vue';
import ActionButton from './ActionButton.vue';
import ErrorBanner from './ErrorBanner.vue';
import { useSession } from '../queries/session';
import { useCancelLinkMutation, useStartLinkMutation } from '../queries/linking';
import { copyText } from '../clipboard';
import { parseGeoGuessrUserId } from '../parse';
import { toErrorMessage } from '../api';

const { openLinkRequest } = useSession();
const start = useStartLinkMutation();
const cancel = useCancelLinkMutation();
const starting = start.isPending;
const cancelling = cancel.isPending;

const profileLink = ref('');
const validationError = ref<string | null>(null);
const copied = ref(false);
const copyFailed = ref(false);

const errorMessage = computed(() => {
  if (validationError.value) {
    return validationError.value;
  }
  const err = start.error.value ?? cancel.error.value;
  return err ? toErrorMessage(err, 'Something went wrong. Try again.') : null;
});

async function onStart(): Promise<void> {
  const userId = parseGeoGuessrUserId(profileLink.value);
  if (!userId) {
    validationError.value =
      'That doesn’t look like a GeoGuessr profile link. It should end in /user/<24 characters>.';
    return;
  }
  validationError.value = null;
  try {
    await start.mutateAsync(userId);
    profileLink.value = '';
  } catch {
    // Surfaced via errorMessage.
  }
}

async function onCancel(): Promise<void> {
  validationError.value = null;
  try {
    await cancel.mutateAsync();
  } catch {
    // Surfaced via errorMessage.
  }
}

async function copyOtp(): Promise<void> {
  if (!openLinkRequest.value) {
    return;
  }
  copyFailed.value = false;
  if (await copyText(openLinkRequest.value.oneTimePassword)) {
    copied.value = true;
    setTimeout(() => {
      copied.value = false;
    }, 2000);
  } else {
    // Clipboard blocked even via the fallback — prompt the user to copy it by hand.
    copyFailed.value = true;
  }
}
</script>

<template>
  <PanelSection title="🔗 Link your GeoGuessr account" data-testid="link-account-panel">
    <template v-if="openLinkRequest">
      <p data-testid="link-otp-intro">
        Your linking request for GeoGuessr account
        <strong>{{ openLinkRequest.geoGuessrUserId }}</strong> is waiting for an admin. Send this
        one-time password to a club admin as a <strong>direct message inside GeoGuessr</strong> —
        never in Discord — and they'll complete the link:
      </p>
      <p class="otp-display" data-testid="link-otp">
        <code>{{ openLinkRequest.oneTimePassword }}</code>
        <ActionButton data-testid="link-otp-copy" @click="copyOtp">
          {{ copied ? 'Copied!' : 'Copy' }}
        </ActionButton>
      </p>
      <p v-if="copyFailed" class="stat-caption" data-testid="link-otp-copy-failed">
        Couldn't copy automatically — select the code above and copy it manually.
      </p>
      <div class="form-actions">
        <ActionButton
          danger
          :busy="cancelling"
          busy-label="Cancelling…"
          data-testid="link-cancel"
          @click="onCancel"
        >
          Cancel request
        </ActionButton>
      </div>
    </template>

    <template v-else>
      <p class="stat-caption">
        Open your GeoGuessr profile, hit the share button and paste the link here (it looks like
        https://www.geoguessr.com/user/62c353a29d0d57e7b9a3383f).
      </p>
      <form class="form-stack" @submit.prevent="onStart">
        <FormField
          id="profile-link"
          v-model="profileLink"
          label="Profile link"
          placeholder="https://www.geoguessr.com/user/…"
          required
          data-testid="link-profile-input"
        />
        <div class="form-actions">
          <ActionButton
            type="submit"
            :busy="starting"
            busy-label="Starting…"
            :disabled="!profileLink"
            data-testid="link-start"
          >
            Start linking
          </ActionButton>
        </div>
      </form>
    </template>

    <ErrorBanner v-if="errorMessage" data-testid="link-error">{{ errorMessage }}</ErrorBanner>
  </PanelSection>
</template>

<style scoped>
.otp-display {
  display: flex;
  align-items: center;
  gap: 10px;
  background: var(--bg-row);
  border: 1px dashed var(--viewer-border);
  border-radius: 10px;
  padding: 10px 14px;
}

.otp-display code {
  font-size: 1.1rem;
  font-weight: 700;
  letter-spacing: 0.06em;
}
</style>
