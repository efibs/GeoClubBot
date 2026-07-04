import { onMounted, onUnmounted } from 'vue';
import { refreshIntervalMs } from '../config';

/**
 * Runs `callback` on an interval while the owning component is mounted. Views use this for their
 * background refresh, so only the active tab polls — switching tabs stops the old view's timer.
 */
export function usePolling(callback: () => void, intervalMs: number = refreshIntervalMs): void {
  let timer: ReturnType<typeof setInterval> | undefined;

  onMounted(() => {
    timer = setInterval(callback, intervalMs);
  });

  onUnmounted(() => {
    if (timer) {
      clearInterval(timer);
    }
  });
}
