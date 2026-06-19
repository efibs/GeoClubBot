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
