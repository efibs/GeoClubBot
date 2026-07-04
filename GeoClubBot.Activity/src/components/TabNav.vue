<script setup lang="ts">
import { computed } from 'vue';
import { useRoute } from 'vue-router';
import { useSessionStore } from '../stores/session';

const session = useSessionStore();
const route = useRoute();

const tabs = computed(() => {
  const entries = [
    { to: '/', label: 'Overview', testId: 'tab-overview' },
    { to: '/missions', label: 'Missions', testId: 'tab-missions' },
    { to: '/me', label: 'My Profile', testId: 'tab-me' },
  ];
  if (session.isAdmin) {
    // Cosmetic only: hiding the tab is UX, not security — the admin endpoints re-check the
    // Administrator permission on every request.
    entries.push({ to: '/admin', label: 'Admin', testId: 'tab-admin' });
  }
  return entries;
});

function isActive(to: string): boolean {
  return to === '/' ? route.path === '/' : route.path.startsWith(to);
}
</script>

<template>
  <nav class="tab-nav" data-testid="tab-nav">
    <RouterLink
      v-for="tab in tabs"
      :key="tab.to"
      :to="tab.to"
      class="tab-link"
      :class="{ active: isActive(tab.to) }"
      :data-testid="tab.testId"
    >
      {{ tab.label }}
    </RouterLink>
  </nav>
</template>
