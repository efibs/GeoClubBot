<script setup lang="ts">
// Small inline spinner that inherits the current text color — use inside buttons and next to
// labels to signal an in-flight action. For a full-screen load use LoadingScreen instead.
</script>

<template>
  <span class="spinner-inline" role="status" aria-label="Loading" />
</template>

<style scoped>
.spinner-inline {
  display: inline-block;
  width: 1em;
  height: 1em;
  vertical-align: -0.15em;
  margin-right: 6px;
  border: 2px solid currentColor;
  border-top-color: transparent;
  border-radius: 50%;
  opacity: 0.9;
  animation: spin 0.7s linear infinite;
}
</style>
