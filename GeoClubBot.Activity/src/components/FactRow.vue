<script setup lang="ts">
// A label/value line used inside stat panels (country, level, averages, …). The value goes in the
// default slot so it can hold rich content (flags, emphasis); the label is plain text.
defineProps<{ label: string }>();
</script>

<template>
  <div class="fact-row">
    <span>{{ label }}</span>
    <strong><slot /></strong>
  </div>
</template>

<style scoped>
.fact-row {
  display: flex;
  justify-content: space-between;
  align-items: baseline;
  gap: 10px;
  padding: 7px 0;
  border-bottom: 1px solid var(--border);
}

.fact-row:last-of-type {
  border-bottom: none;
}

.fact-row span {
  color: var(--text-muted);
  font-size: 0.9rem;
}
</style>
