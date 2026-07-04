<script setup lang="ts">
import { onMounted, ref, watch } from 'vue';
import { storeToRefs } from 'pinia';
import { useReminderStore } from '../stores/reminder';

const store = useReminderStore();
const { reminder, saving, error, dmWarning } = storeToRefs(store);

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

onMounted(() => {
  if (!store.loaded) {
    void store.load();
  }
});

async function save(): Promise<void> {
  if (!time.value) {
    return;
  }
  await store.save(time.value, timeZoneId, message.value.trim() || null);
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
          {{ reminder ? 'Update reminder' : 'Set reminder' }}
        </button>
        <button
          v-if="reminder"
          type="button"
          class="action-button danger"
          :disabled="saving"
          data-testid="reminder-stop"
          @click="store.stop()"
        >
          Stop reminder
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
