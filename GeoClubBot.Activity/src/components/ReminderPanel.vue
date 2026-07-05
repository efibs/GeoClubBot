<script setup lang="ts">
import { computed, ref, watch } from 'vue';
import PanelSection from './PanelSection.vue';
import FormField from './FormField.vue';
import ActionButton from './ActionButton.vue';
import ErrorBanner from './ErrorBanner.vue';
import {
  useReminderQuery,
  useSaveReminderMutation,
  useStopReminderMutation,
} from '../queries/reminder';
import { toErrorMessage } from '../api';

const { data: reminder } = useReminderQuery();
const save = useSaveReminderMutation();
const stop = useStopReminderMutation();
const saving = save.isPending;
const stopping = stop.isPending;

const time = ref('');
const message = ref('');
// The browser knows the viewer's time zone; the backend validates it and converts to UTC.
const timeZoneId = Intl.DateTimeFormat().resolvedOptions().timeZone ?? null;

// Prefill the form from the existing reminder (otherwise the time input just showed "--:--").
watch(
  reminder,
  (value) => {
    if (value) {
      time.value = value.localTime;
      message.value = value.customMessage ?? '';
    }
  },
  { immediate: true },
);

// The last save persisted but its confirmation DM couldn't be delivered.
const dmWarning = computed(() => reminder.value != null && save.data.value?.dmDelivered === false);
const errorMessage = computed(() => {
  const err = save.error.value ?? stop.error.value;
  return err ? toErrorMessage(err, 'Failed to save your reminder.') : null;
});

async function onSave(): Promise<void> {
  if (!time.value) {
    return;
  }
  try {
    await save.mutateAsync({
      localTime: time.value,
      timeZoneId,
      customMessage: message.value.trim() || null,
    });
  } catch {
    // Surfaced via errorMessage.
  }
}

async function onStop(): Promise<void> {
  try {
    await stop.mutateAsync();
  } catch {
    // Surfaced via errorMessage.
  }
}
</script>

<template>
  <PanelSection title="⏰ Daily reminder" data-testid="reminder-panel">
    <p v-if="reminder" class="stat-caption" data-testid="reminder-status">
      Reminding you daily at {{ reminder.localTime }}
      <template v-if="reminder.timeZoneId">({{ reminder.timeZoneId }})</template>
      <template v-else>UTC</template>
      — unless your mission is already done.
    </p>
    <p v-else class="stat-caption" data-testid="reminder-status">
      No reminder yet. Pick a time and the bot DMs you each day until your mission is done.
    </p>

    <form class="form-stack" @submit.prevent="onSave">
      <FormField
        id="reminder-time"
        v-model="time"
        :label="`Time (${timeZoneId ?? 'UTC'})`"
        type="time"
        required
        data-testid="reminder-time"
      />
      <FormField
        id="reminder-message"
        v-model="message"
        label="Custom message (optional)"
        :maxlength="500"
        placeholder="Time to play GeoGuessr!"
        data-testid="reminder-message"
      />

      <div class="form-actions">
        <ActionButton
          type="submit"
          :busy="saving"
          busy-label="Saving…"
          :disabled="!time || stopping"
          data-testid="reminder-save"
        >
          {{ reminder ? 'Update reminder' : 'Set reminder' }}
        </ActionButton>
        <ActionButton
          v-if="reminder"
          danger
          :busy="stopping"
          busy-label="Stopping…"
          :disabled="saving"
          data-testid="reminder-stop"
          @click="onStop"
        >
          Stop reminder
        </ActionButton>
      </div>
    </form>

    <ErrorBanner v-if="dmWarning" data-testid="reminder-dm-warning">
      Saved — but the confirmation DM couldn't be delivered. Allow direct messages from server
      members so the reminders can reach you.
    </ErrorBanner>
    <ErrorBanner v-if="errorMessage" data-testid="reminder-error">{{ errorMessage }}</ErrorBanner>
  </PanelSection>
</template>
