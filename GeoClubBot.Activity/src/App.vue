<script setup lang="ts">
import { onMounted, ref } from 'vue';
import TabNav from './components/TabNav.vue';
import ConfirmDialog from './components/ConfirmDialog.vue';
import LoadingScreen from './components/LoadingScreen.vue';
import ErrorScreen from './components/ErrorScreen.vue';
import { fetchMe, toErrorMessage } from './api';
import { queryClient } from './queryClient';
import { queryKeys } from './queries/keys';
import { initializeDiscord } from './discord';

const initializing = ref(true);
const initError = ref<string | null>(null);
const initStatus = ref('Connecting to the club…');

onMounted(async () => {
  try {
    await initializeDiscord((step) => {
      initStatus.value = step;
    });
    initStatus.value = 'Loading your profile…';
    // Non-fatal: prefetchQuery swallows errors, so a /me failure still renders the shell with the
    // non-admin, unlinked defaults. Priming the cache here also lets the router's admin guard read
    // the session synchronously before the first navigation.
    await queryClient.prefetchQuery({ queryKey: queryKeys.session, queryFn: fetchMe });
  } catch (err) {
    initError.value = toErrorMessage(err, 'Failed to start the dashboard.');
  } finally {
    initializing.value = false;
  }
});
</script>

<template>
  <div class="app">
    <LoadingScreen v-if="initializing" :message="initStatus" data-testid="initializing" />

    <ErrorScreen v-else-if="initError" :message="initError" data-testid="init-error" />

    <template v-else>
      <TabNav />
      <router-view />
    </template>

    <ConfirmDialog />
  </div>
</template>
