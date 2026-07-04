import { beforeEach, describe, expect, it, vi } from 'vitest';
import { createPinia, setActivePinia } from 'pinia';
import { useReminderStore } from './reminder';
import { deleteReminder, fetchReminder, putReminder } from '../api';
import type { ReminderDto } from '../types';

vi.mock('../api', () => ({
  fetchReminder: vi.fn(),
  putReminder: vi.fn(),
  deleteReminder: vi.fn(),
}));

const mockedFetch = vi.mocked(fetchReminder);
const mockedPut = vi.mocked(putReminder);
const mockedDelete = vi.mocked(deleteReminder);

const reminder: ReminderDto = { timeUtc: '18:30', timeZoneId: 'Europe/Berlin', customMessage: null };

describe('reminder store', () => {
  beforeEach(() => {
    setActivePinia(createPinia());
    mockedFetch.mockReset();
    mockedPut.mockReset();
    mockedDelete.mockReset();
  });

  it('loads the current reminder', async () => {
    mockedFetch.mockResolvedValue({ reminder });
    const store = useReminderStore();

    await store.load();

    expect(store.reminder).toEqual(reminder);
    expect(store.loaded).toBe(true);
  });

  it('saves and flags a failed confirmation DM as a warning, not an error', async () => {
    mockedPut.mockResolvedValue({ reminder, dmDelivered: false, dmErrorCode: 'discord.dm.disabled' });
    const store = useReminderStore();

    await store.save('19:30', 'Europe/Berlin', null);

    expect(store.reminder).toEqual(reminder);
    expect(store.dmWarning).toBe(true);
    expect(store.error).toBeNull();
    expect(mockedPut).toHaveBeenCalledWith({
      localTime: '19:30',
      timeZoneId: 'Europe/Berlin',
      customMessage: null,
    });
  });

  it('stops the reminder', async () => {
    mockedFetch.mockResolvedValue({ reminder });
    mockedDelete.mockResolvedValue();
    const store = useReminderStore();
    await store.load();

    await store.stop();

    expect(store.reminder).toBeNull();
  });

  it('surfaces save failures', async () => {
    mockedPut.mockRejectedValue(new Error('bad time zone'));
    const store = useReminderStore();

    await store.save('19:30', 'Nowhere/Land', null);

    expect(store.error).toBe('bad time zone');
    expect(store.dmWarning).toBe(false);
  });
});
