import { reactive, readonly } from 'vue';

export interface ConfirmOptions {
  message: string;
  confirmLabel?: string;
  cancelLabel?: string;
  /** Styles the confirm button as destructive (red) — for revoke/remove/unlink. */
  danger?: boolean;
}

interface ConfirmState {
  open: boolean;
  message: string;
  confirmLabel: string;
  cancelLabel: string;
  danger: boolean;
}

const state = reactive<ConfirmState>({
  open: false,
  message: '',
  confirmLabel: 'Confirm',
  cancelLabel: 'Cancel',
  danger: false,
});

let resolver: ((value: boolean) => void) | null = null;

/**
 * Promise-based confirmation prompt that replaces `window.confirm()`.
 *
 * The dashboard runs inside Discord's activity iframe, which is cross-origin and sandboxed.
 * Chromium silently ignores `window.confirm()` there (it just returns `false`), so every admin
 * write guarded by it appeared to do nothing. This renders an in-DOM modal instead — see
 * {@link ConfirmDialog}, mounted once at the app root.
 */
export function confirm(options: string | ConfirmOptions): Promise<boolean> {
  // Abandon any prompt still on screen as declined before showing the new one.
  resolver?.(false);

  const opts = typeof options === 'string' ? { message: options } : options;
  state.message = opts.message;
  state.confirmLabel = opts.confirmLabel ?? 'Confirm';
  state.cancelLabel = opts.cancelLabel ?? 'Cancel';
  state.danger = opts.danger ?? false;
  state.open = true;

  return new Promise<boolean>((resolve) => {
    resolver = resolve;
  });
}

function settle(result: boolean): void {
  if (!state.open) {
    return;
  }
  state.open = false;
  const resolve = resolver;
  resolver = null;
  resolve?.(result);
}

/** Wiring for the single {@link ConfirmDialog} host component. */
export function useConfirmDialog() {
  return {
    state: readonly(state),
    accept: () => settle(true),
    cancel: () => settle(false),
  };
}
