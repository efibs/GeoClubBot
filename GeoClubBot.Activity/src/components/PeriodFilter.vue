<script setup lang="ts">
import type { DepthOption } from '../format';

defineProps<{
  modelValue: number;
  options: DepthOption[];
}>();

defineEmits<{ 'update:modelValue': [value: number] }>();
</script>

<template>
  <div class="period-filter" role="group" aria-label="Leaderboard period">
    <button
      v-for="option in options"
      :key="option.value"
      type="button"
      class="period-button"
      :class="{ active: option.value === modelValue }"
      :data-testid="`period-${option.value}`"
      @click="$emit('update:modelValue', option.value)"
    >
      {{ option.label }}
    </button>
  </div>
</template>

<style scoped>
.period-filter {
  display: inline-flex;
  background: var(--bg-elevated);
  border: 1px solid var(--border);
  border-radius: 12px;
  padding: 4px;
  gap: 4px;
}

.period-button {
  background: transparent;
  color: var(--text-muted);
  border: none;
  border-radius: 9px;
  padding: 7px 16px;
  font-weight: 600;
  cursor: pointer;
  transition:
    background 0.15s ease,
    color 0.15s ease;
}

.period-button.active {
  background: linear-gradient(135deg, var(--accent), var(--accent-strong));
  color: #fff;
}
</style>
