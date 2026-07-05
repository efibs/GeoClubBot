<script setup lang="ts">
import { useLastCheckTimeQuery } from '../../queries/admin';

const { data: lastCheckTime } = useLastCheckTimeQuery();
</script>

<template>
  <div class="admin-area" data-testid="admin-view">
    <nav class="sub-nav" data-testid="admin-sub-nav">
      <RouterLink to="/admin/strikes" class="tab-link" data-testid="admin-tab-strikes"
        >Strikes</RouterLink
      >
      <RouterLink to="/admin/excuses" class="tab-link" data-testid="admin-tab-excuses"
        >Excuses</RouterLink
      >
      <RouterLink to="/admin/members" class="tab-link" data-testid="admin-tab-members"
        >Members</RouterLink
      >
      <RouterLink to="/admin/linking" class="tab-link" data-testid="admin-tab-linking"
        >Linking</RouterLink
      >
      <span v-if="lastCheckTime" class="updated" data-testid="admin-last-check">
        Last activity check: {{ new Date(lastCheckTime).toLocaleString() }}
      </span>
    </nav>

    <router-view />
  </div>
</template>
