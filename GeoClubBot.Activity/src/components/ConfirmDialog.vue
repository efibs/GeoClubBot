<script setup lang="ts">
import { useConfirmDialog } from '../composables/useConfirm';

const { state, accept, cancel } = useConfirmDialog();
</script>

<template>
  <Transition name="confirm-fade">
    <div v-if="state.open" class="confirm-overlay" data-testid="confirm-dialog" @click.self="cancel">
      <div class="confirm-card" role="alertdialog" aria-modal="true">
        <p class="confirm-message">{{ state.message }}</p>
        <div class="confirm-actions">
          <button type="button" class="refresh-button" data-testid="confirm-cancel" @click="cancel">
            {{ state.cancelLabel }}
          </button>
          <button
            type="button"
            class="action-button"
            :class="{ danger: state.danger }"
            data-testid="confirm-accept"
            @click="accept"
          >
            {{ state.confirmLabel }}
          </button>
        </div>
      </div>
    </div>
  </Transition>
</template>
