<script setup lang="ts">
import LoadingSpinner from './LoadingSpinner.vue';

// A button that folds in the busy/spinner pattern repeated across every form and action row.
// `busy` shows an inline spinner (+ optional `busyLabel`) and disables the button; `variant`
// switches between the filled primary look and the ghost (outline) look used for refresh/cancel.
// Attributes (data-testid, @click, aria-*) fall through to the underlying <button>.
withDefaults(
  defineProps<{
    type?: 'button' | 'submit';
    variant?: 'primary' | 'ghost';
    danger?: boolean;
    small?: boolean;
    busy?: boolean;
    busyLabel?: string;
    disabled?: boolean;
  }>(),
  { type: 'button', variant: 'primary' },
);
</script>

<template>
  <button
    :type="type"
    class="action-button"
    :class="{ ghost: variant === 'ghost', danger, small }"
    :disabled="disabled || busy"
  >
    <LoadingSpinner v-if="busy" />
    <template v-if="busy && busyLabel">{{ busyLabel }}</template>
    <slot v-else />
  </button>
</template>

<style scoped>
.action-button {
  background: linear-gradient(135deg, var(--accent), var(--accent-strong));
  color: #fff;
  border: none;
  border-radius: 10px;
  padding: 8px 14px;
  font-weight: 600;
  cursor: pointer;
}

.action-button:disabled {
  opacity: 0.6;
  cursor: default;
}

.action-button.danger {
  background: #5a2733;
  border: 1px solid #7a3b48;
  color: #ffd7dd;
}

.action-button.small {
  padding: 4px 10px;
  font-size: 0.8rem;
}

.action-button.ghost {
  background: var(--bg-elevated);
  color: var(--text);
  border: 1px solid var(--border);
  transition:
    border-color 0.15s ease,
    transform 0.1s ease;
}

.action-button.ghost:hover:not(:disabled) {
  border-color: var(--accent);
}
</style>
