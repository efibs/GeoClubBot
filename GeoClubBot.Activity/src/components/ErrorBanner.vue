<script setup lang="ts">
// A red inline error banner. Centralizes the ⚠️ prefix and styling repeated across the views; the
// message goes in the default slot. Attributes (data-testid) fall through to the paragraph.
</script>

<template>
  <p class="error-banner">⚠️ <slot /></p>
</template>

<style scoped>
.error-banner {
  background: #3a1d24;
  border: 1px solid #7a3b48;
  color: #ffd7dd;
  border-radius: 10px;
  padding: 10px 14px;
  margin: 0;
}
</style>
