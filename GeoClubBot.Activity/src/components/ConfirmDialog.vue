<script setup lang="ts">
import { useConfirmDialog } from '../composables/useConfirm';
import ActionButton from './ActionButton.vue';

const { state, accept, cancel } = useConfirmDialog();
</script>

<template>
  <Transition name="confirm-fade">
    <div
      v-if="state.open"
      class="confirm-overlay"
      data-testid="confirm-dialog"
      @click.self="cancel"
    >
      <div class="confirm-card" role="alertdialog" aria-modal="true">
        <p class="confirm-message">{{ state.message }}</p>
        <div class="confirm-actions">
          <ActionButton variant="ghost" data-testid="confirm-cancel" @click="cancel">
            {{ state.cancelLabel }}
          </ActionButton>
          <ActionButton :danger="state.danger" data-testid="confirm-accept" @click="accept">
            {{ state.confirmLabel }}
          </ActionButton>
        </div>
      </div>
    </div>
  </Transition>
</template>

<style scoped>
.confirm-overlay {
  position: fixed;
  inset: 0;
  z-index: 100;
  display: flex;
  align-items: center;
  justify-content: center;
  padding: 20px;
  background: rgba(5, 8, 18, 0.7);
}

.confirm-card {
  background: var(--bg-elevated);
  border: 1px solid var(--border);
  border-radius: 16px;
  padding: 22px;
  max-width: 420px;
  width: 100%;
  box-shadow: 0 20px 60px rgba(0, 0, 0, 0.45);
}

.confirm-message {
  margin: 0 0 18px;
  font-size: 1rem;
  line-height: 1.4;
}

.confirm-actions {
  display: flex;
  justify-content: flex-end;
  gap: 8px;
}

.confirm-fade-enter-active,
.confirm-fade-leave-active {
  transition: opacity 0.15s ease;
}

.confirm-fade-enter-from,
.confirm-fade-leave-to {
  opacity: 0;
}
</style>
