<script setup lang="ts">
import { onMounted, ref } from 'vue';
import TabNav from './components/TabNav.vue';
import { useSessionStore } from './stores/session';
import { initializeDiscord } from './discord';

const session = useSessionStore();

const initializing = ref(true);
const initError = ref<string | null>(null);
const initStatus = ref('Connecting to the club…');

onMounted(async () => {
  try {
    await initializeDiscord((step) => {
      initStatus.value = step;
    });
    initStatus.value = 'Loading your profile…';
    // Non-fatal: a /me failure still renders the shell with the non-admin, unlinked defaults.
    await session.loadMe();
  } catch (err) {
    initError.value = err instanceof Error ? err.message : 'Failed to start the dashboard.';
  } finally {
    initializing.value = false;
  }
});
</script>

<template>
  <div class="app">
    <div v-if="initializing" class="screen-message" data-testid="initializing">
      <div class="spinner" />
      <p>{{ initStatus }}</p>
    </div>

    <div v-else-if="initError" class="screen-message error" data-testid="init-error">
      <p>⚠️ {{ initError }}</p>
    </div>

    <template v-else>
      <TabNav />
      <router-view />
    </template>
  </div>
</template>
