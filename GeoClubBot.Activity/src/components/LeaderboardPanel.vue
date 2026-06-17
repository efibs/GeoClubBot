<script setup lang="ts">
import type { LeaderboardEntryDto } from '../types';
import { formatXp, rankBadge } from '../format';

defineProps<{
  entries: LeaderboardEntryDto[];
  viewerNickname: string | null;
}>();
</script>

<template>
  <section class="panel" data-testid="leaderboard-panel">
    <h2 class="panel-title">🏆 Leaderboard</h2>
    <p v-if="entries.length === 0" class="empty-state" data-testid="leaderboard-empty">
      No ranking data yet.
    </p>
    <ol v-else class="rows">
      <li
        v-for="entry in entries"
        :key="entry.nickname"
        class="row"
        :class="{ 'is-viewer': entry.nickname === viewerNickname }"
        :data-testid="entry.nickname === viewerNickname ? 'viewer-row' : undefined"
      >
        <span class="rank" :class="{ medal: entry.rank <= 3 }">{{ rankBadge(entry.rank) }}</span>
        <span class="name">{{ entry.nickname }}</span>
        <span class="value">{{ formatXp(entry.averageXp) }}</span>
      </li>
    </ol>
  </section>
</template>
