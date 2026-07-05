<script setup lang="ts">
// A full-height centered loading state (a large spinner plus an optional message). Used for the
// app boot screen and each tab's first load. Attributes (data-testid) fall through.
defineProps<{ message?: string }>();
</script>

<template>
  <div class="screen-message">
    <div class="spinner" />
    <p v-if="message">{{ message }}</p>
  </div>
</template>
