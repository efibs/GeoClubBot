<script setup lang="ts">
// A full-height centered error state. Used when a tab can't load anything at all (as opposed to the
// inline ErrorBanner shown when there's already data on screen). Attributes fall through.
defineProps<{ message: string }>();
</script>

<template>
  <div class="screen-message error">
    <p>⚠️ {{ message }}</p>
  </div>
</template>
