<script setup lang="ts">
import { computed, ref } from 'vue';
import PanelSection from './PanelSection.vue';
import FormField from './FormField.vue';
import ActionButton from './ActionButton.vue';
import ErrorBanner from './ErrorBanner.vue';
import {
  useRemindersQuery,
  useAddReminderMutation,
  useRemoveReminderMutation,
} from '../queries/reminder';
import { toErrorMessage } from '../api';

const { data: reminders } = useRemindersQuery();
const add = useAddReminderMutation();
const remove = useRemoveReminderMutation();
const adding = add.isPending;

const time = ref('');
const message = ref('');
// The browser knows the viewer's time zone; the backend validates it and converts to UTC.
const timeZoneId = Intl.DateTimeFormat().resolvedOptions().timeZone ?? null;

const list = computed(() => reminders.value ?? []);

// The last add persisted but its confirmation DM couldn't be delivered.
const dmWarning = computed(() => add.data.value?.dmDelivered === false);
const errorMessage = computed(() => {
  const err = add.error.value ?? remove.error.value;
  return err ? toErrorMessage(err, 'Failed to update your reminders.') : null;
});

// Which reminder is currently being removed, so only its button shows the busy state.
const removingId = computed(() => (remove.isPending.value ? (remove.variables.value ?? null) : null));

async function onAdd(): Promise<void> {
  if (!time.value) {
    return;
  }
  try {
    await add.mutateAsync({
      localTime: time.value,
      timeZoneId,
      customMessage: message.value.trim() || null,
    });
    // Keep the time so adjacent reminders are quick to add; clear the one-off message.
    message.value = '';
  } catch {
    // Surfaced via errorMessage.
  }
}

async function onRemove(id: string): Promise<void> {
  try {
    await remove.mutateAsync(id);
  } catch {
    // Surfaced via errorMessage.
  }
}
</script>

<template>
  <PanelSection title="⏰ Daily reminders" data-testid="reminder-panel">
    <p class="stat-caption" data-testid="reminder-status">
      <template v-if="list.length">
        The bot DMs you at each time below — unless your mission is already done.
      </template>
      <template v-else>
        No reminders yet. Pick a time and the bot DMs you each day until your mission is done.
      </template>
    </p>

    <ul v-if="list.length" class="reminder-list" data-testid="reminder-list">
      <li v-for="reminder in list" :key="reminder.id" class="reminder-item" data-testid="reminder-item">
        <div class="reminder-item-info">
          <span class="reminder-item-time">
            {{ reminder.localTime }}
            <template v-if="reminder.timeZoneId">({{ reminder.timeZoneId }})</template>
            <template v-else>UTC</template>
          </span>
          <span v-if="reminder.customMessage" class="reminder-item-message">
            {{ reminder.customMessage }}
          </span>
        </div>
        <ActionButton
          danger
          :busy="removingId === reminder.id"
          busy-label="Removing…"
          :disabled="adding"
          data-testid="reminder-remove"
          @click="onRemove(reminder.id)"
        >
          Remove
        </ActionButton>
      </li>
    </ul>

    <form class="form-stack" @submit.prevent="onAdd">
      <FormField
        id="reminder-time"
        v-model="time"
        :label="`Add a time (${timeZoneId ?? 'UTC'})`"
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
          :busy="adding"
          busy-label="Adding…"
          :disabled="!time"
          data-testid="reminder-save"
        >
          Add reminder
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

<style scoped>
.reminder-list {
  list-style: none;
  margin: 0 0 0.75rem;
  padding: 0;
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
}

.reminder-item {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 0.75rem;
  padding: 0.5rem 0.75rem;
  border: 1px solid var(--border, rgba(127, 127, 127, 0.3));
  border-radius: 0.5rem;
}

.reminder-item-info {
  display: flex;
  flex-direction: column;
  gap: 0.15rem;
  min-width: 0;
}

.reminder-item-time {
  font-weight: 600;
}

.reminder-item-message {
  font-size: 0.85em;
  opacity: 0.75;
  overflow-wrap: anywhere;
}
</style>
