<script setup lang="ts">
// A dashboard panel: an elevated card with a heading. Replaces the repeated
// `<section class="panel"><h2 class="panel-title">…</h2>` markup across every view. `wide` makes
// the panel span the full grid width. Extra attributes (data-testid) fall through to the section.
withDefaults(defineProps<{ title: string; wide?: boolean }>(), { wide: false });
</script>

<template>
  <section class="panel" :class="{ 'panel-wide': wide }">
    <h2 class="panel-title">{{ title }}</h2>
    <slot />
  </section>
</template>

<style scoped>
.panel {
  background: var(--bg-elevated);
  border: 1px solid var(--border);
  border-radius: 16px;
  padding: 18px;
  box-shadow: 0 10px 30px rgba(0, 0, 0, 0.25);
}

.panel-title {
  margin: 0 0 14px;
  font-size: 1.1rem;
  font-weight: 700;
}
</style>
