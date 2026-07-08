import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query';
import { addReminder, deleteReminder, fetchReminders } from '../api';
import { queryKeys } from './keys';
import type { ReminderDto } from '../types';

/** The viewer's daily-mission reminders (empty array when none are set). */
export function useRemindersQuery() {
  return useQuery({
    queryKey: queryKeys.reminders,
    queryFn: () => fetchReminders(),
  });
}

interface AddReminderVars {
  localTime: string;
  timeZoneId: string | null;
  customMessage: string | null;
}

/**
 * Adds a reminder and merges the server's echo into the cached list (no extra refetch). The
 * mutation's `data.dmDelivered` lets the panel show the "DM couldn't be delivered" warning.
 */
export function useAddReminderMutation() {
  const client = useQueryClient();
  return useMutation({
    mutationFn: (vars: AddReminderVars) => addReminder(vars),
    onSuccess: (result) => {
      client.setQueryData<ReminderDto[]>(queryKeys.reminders, (current) => {
        const others = (current ?? []).filter((r) => r.id !== result.reminder.id);
        return [...others, result.reminder].sort((a, b) => a.timeUtc.localeCompare(b.timeUtc));
      });
    },
  });
}

/** Removes a reminder and drops it from the cached list. */
export function useRemoveReminderMutation() {
  const client = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => deleteReminder(id),
    onSuccess: (_result, id) => {
      client.setQueryData<ReminderDto[]>(queryKeys.reminders, (current) =>
        (current ?? []).filter((r) => r.id !== id),
      );
    },
  });
}
