import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query';
import { deleteReminder, fetchReminder, putReminder } from '../api';
import { queryKeys } from './keys';
import type { ReminderDto } from '../types';

/** The viewer's current daily-mission reminder (null when none is set). */
export function useReminderQuery() {
  return useQuery({
    queryKey: queryKeys.reminder,
    queryFn: async () => (await fetchReminder()).reminder,
  });
}

interface SaveReminderVars {
  localTime: string;
  timeZoneId: string | null;
  customMessage: string | null;
}

/**
 * Saves the reminder and writes the server's echo straight into the cache (no extra refetch). The
 * mutation's `data.dmDelivered` lets the panel show the "DM couldn't be delivered" warning.
 */
export function useSaveReminderMutation() {
  const client = useQueryClient();
  return useMutation({
    mutationFn: (vars: SaveReminderVars) => putReminder(vars),
    onSuccess: (result) => {
      client.setQueryData<ReminderDto | null>(queryKeys.reminder, result.reminder);
    },
  });
}

/** Stops the reminder and clears it from the cache. */
export function useStopReminderMutation() {
  const client = useQueryClient();
  return useMutation({
    mutationFn: () => deleteReminder(),
    onSuccess: () => {
      client.setQueryData<ReminderDto | null>(queryKeys.reminder, null);
    },
  });
}
