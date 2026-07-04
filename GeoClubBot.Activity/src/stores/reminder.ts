import { defineStore } from 'pinia';
import { deleteReminder, fetchReminder, putReminder } from '../api';
import type { ReminderDto } from '../types';

interface ReminderState {
  reminder: ReminderDto | null;
  loaded: boolean;
  saving: boolean;
  error: string | null;
  // True when the last save persisted but the confirmation DM couldn't be delivered.
  dmWarning: boolean;
}

export const useReminderStore = defineStore('reminder', {
  state: (): ReminderState => ({
    reminder: null,
    loaded: false,
    saving: false,
    error: null,
    dmWarning: false,
  }),
  actions: {
    async load(): Promise<void> {
      this.error = null;
      try {
        this.reminder = (await fetchReminder()).reminder;
        this.loaded = true;
      } catch (err) {
        this.error = err instanceof Error ? err.message : 'Failed to load your reminder.';
      }
    },

    async save(localTime: string, timeZoneId: string | null, customMessage: string | null): Promise<void> {
      this.saving = true;
      this.error = null;
      this.dmWarning = false;
      try {
        const result = await putReminder({ localTime, timeZoneId, customMessage });
        this.reminder = result.reminder;
        this.dmWarning = !result.dmDelivered;
      } catch (err) {
        this.error = err instanceof Error ? err.message : 'Failed to save your reminder.';
      } finally {
        this.saving = false;
      }
    },

    async stop(): Promise<void> {
      this.saving = true;
      this.error = null;
      this.dmWarning = false;
      try {
        await deleteReminder();
        this.reminder = null;
      } catch (err) {
        this.error = err instanceof Error ? err.message : 'Failed to stop your reminder.';
      } finally {
        this.saving = false;
      }
    },
  },
});
