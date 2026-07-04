<script setup lang="ts">
import { onMounted, ref, watch } from 'vue';
import { storeToRefs } from 'pinia';
import { useReminderStore } from '../stores/reminder';
import Spinner from './Spinner.vue';

const store = useReminderStore();
const { reminder, saving, error, dmWarning } = storeToRefs(store);

const time = ref('');
const message = ref('');
// Which action is running, so only the clicked button shows a spinner (both share `saving`).
const pending = ref<'save' | 'stop' | null>(null);
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

onMounted(() => {
  if (!store.loaded) {
    void store.load();
  }
});

async function save(): Promise<void> {
  if (!time.value) {
    return;
  }
  pending.value = 'save';
  await store.save(time.value, timeZoneId, message.value.trim() || null);
  pending.value = null;
}

async function stop(): Promise<void> {
  pending.value = 'stop';
  await store.stop();
  pending.value = null;
}
</script>

<template>
  <section class="panel" data-testid="reminder-panel">
    <h2 class="panel-title">⏰ Daily reminder</h2>

    <p v-if="reminder" class="stat-caption" data-testid="reminder-status">
      Reminding you daily at {{ reminder.localTime }}
      <template v-if="reminder.timeZoneId">({{ reminder.timeZoneId }})</template>
      <template v-else>UTC</template>
      — unless your mission is already done.
    </p>
    <p v-else class="stat-caption" data-testid="reminder-status">
      No reminder yet. Pick a time and the bot DMs you each day until your mission is done.
    </p>

    <form class="reminder-form" @submit.prevent="save">
      <label class="field-label" for="reminder-time">Time ({{ timeZoneId ?? 'UTC' }})</label>
      <input
        id="reminder-time"
        v-model="time"
        type="time"
        required
        class="field-input"
        data-testid="reminder-time"
      />

      <label class="field-label" for="reminder-message">Custom message (optional)</label>
      <input
        id="reminder-message"
        v-model="message"
        type="text"
        maxlength="500"
        placeholder="Time to play GeoGuessr!"
        class="field-input"
        data-testid="reminder-message"
      />

      <div class="form-actions">
        <button type="submit" class="action-button" :disabled="saving || !time" data-testid="reminder-save">
          <Spinner v-if="pending === 'save'" />
          {{ pending === 'save' ? 'Saving…' : reminder ? 'Update reminder' : 'Set reminder' }}
        </button>
        <button
          v-if="reminder"
          type="button"
          class="action-button danger"
          :disabled="saving"
          data-testid="reminder-stop"
          @click="stop"
        >
          <Spinner v-if="pending === 'stop'" />
          {{ pending === 'stop' ? 'Stopping…' : 'Stop reminder' }}
        </button>
      </div>
    </form>

    <p v-if="dmWarning" class="error-banner" data-testid="reminder-dm-warning">
      ⚠️ Saved — but the confirmation DM couldn't be delivered. Allow direct messages from server
      members so the reminders can reach you.
    </p>
    <p v-if="error" class="error-banner" data-testid="reminder-error">⚠️ {{ error }}</p>
  </section>
</template>
