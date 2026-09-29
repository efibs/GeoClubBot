<script setup lang="ts">
import type { DailyStreakDto } from '../types';
import { formatStreak, streakFlames } from '../format';

defineProps<{
  streaks: DailyStreakDto[];
  viewerNickname: string | null;
}>();
</script>

<template>
  <section class="panel" data-testid="streaks-panel">
    <!-- A streak day is a day on which the member played the daily challenge (or a duel). -->
    <h2 class="panel-title">🔥 Daily Streaks</h2>
    <p v-if="streaks.length === 0" class="empty-state" data-testid="streaks-empty">
      No streaks tracked yet.
    </p>
    <ol v-else class="rows">
      <li
        v-for="streak in streaks"
        :key="streak.nickname"
        class="row row-no-rank"
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
