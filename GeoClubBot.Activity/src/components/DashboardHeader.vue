<script setup lang="ts">
import ActionButton from './ActionButton.vue';

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
      <span v-if="clubLevel !== null" class="level-badge" data-testid="club-level"
        >Lv {{ clubLevel }}</span
      >
    </div>
    <div class="header-actions">
      <span class="updated" data-testid="last-updated">Updated {{ formatTime(lastUpdated) }}</span>
      <ActionButton
        variant="ghost"
        :disabled="loading"
        data-testid="refresh-button"
        @click="$emit('refresh')"
      >
        <span class="refresh-icon" :class="{ spinning: loading }">⟳</span> Refresh
      </ActionButton>
    </div>
  </header>
</template>

<style scoped>
.dashboard-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  flex-wrap: wrap;
  gap: 12px;
}

.title-group {
  display: flex;
  align-items: baseline;
  gap: 12px;
}

.club-name {
  margin: 0;
  font-size: clamp(1.5rem, 3vw, 2.4rem);
  font-weight: 800;
  letter-spacing: 0.02em;
}

.level-badge {
  background: linear-gradient(135deg, var(--accent), var(--accent-strong));
  color: #fff;
  font-weight: 700;
  font-size: 0.85rem;
  padding: 4px 10px;
  border-radius: 999px;
}

.header-actions {
  display: flex;
  align-items: center;
  gap: 14px;
}

.refresh-icon {
  display: inline-block;
}

.refresh-icon.spinning {
  animation: spin 0.9s linear infinite;
}
</style>
