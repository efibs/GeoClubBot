<script setup lang="ts">
import type { MissionStreakDto } from '../types';
import { formatStreak, streakFlames } from '../format';

defineProps<{
  streaks: MissionStreakDto[];
  viewerNickname: string | null;
}>();
</script>

<template>
  <section class="panel" data-testid="streaks-panel">
    <h2 class="panel-title">🔥 Mission Streaks</h2>
    <p v-if="streaks.length === 0" class="empty-state" data-testid="streaks-empty">
      No streaks tracked yet.
    </p>
    <ol v-else class="rows">
      <li
        v-for="streak in streaks"
        :key="streak.nickname"
        class="row"
        :class="{ 'is-viewer': streak.nickname === viewerNickname }"
      >
        <span class="name">{{ streak.nickname }}</span>
        <span class="value">
          <span class="flames">{{ streakFlames(streak.currentStreak) }}</span>
          {{ formatStreak(streak.currentStreak) }}
        </span>
        <span class="sub">best {{ formatStreak(streak.longestStreak) }}</span>
      </li>
    </ol>
  </section>
</template>
