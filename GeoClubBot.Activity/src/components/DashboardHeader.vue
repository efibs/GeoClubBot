<script setup lang="ts">
defineProps<{
  clubName: string;
  clubLevel: number | null;
  lastUpdated: Date | null;
  loading: boolean;
}>();

defineEmits<{ refresh: [] }>();

function formatTime(date: Date | null): string {
  if (!date) {
    return '—';
  }
  return date.toLocaleTimeString('en-US', { hour: '2-digit', minute: '2-digit' });
}
</script>

<template>
  <header class="dashboard-header">
    <div class="title-group">
      <h1 class="club-name" data-testid="club-name">{{ clubName }}</h1>
      <span v-if="clubLevel !== null" class="level-badge" data-testid="club-level">Lv {{ clubLevel }}</span>
    </div>
    <div class="header-actions">
      <span class="updated" data-testid="last-updated">Updated {{ formatTime(lastUpdated) }}</span>
      <button
        type="button"
        class="refresh-button"
        :disabled="loading"
        data-testid="refresh-button"
        @click="$emit('refresh')"
      >
        <span class="refresh-icon" :class="{ spinning: loading }">⟳</span> Refresh
      </button>
    </div>
  </header>
</template>
