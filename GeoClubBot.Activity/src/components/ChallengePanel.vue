<script setup lang="ts">
import type { ChallengeResultDto } from '../types';
import { rankBadge } from '../format';

defineProps<{
  challenges: ChallengeResultDto[];
  viewerNickname: string | null;
}>();
</script>

<template>
  <section class="panel" data-testid="challenge-panel">
    <h2 class="panel-title">🎯 Challenge Standings</h2>
    <p v-if="challenges.length === 0" class="empty-state" data-testid="challenge-empty">
      No active challenges right now.
    </p>
    <div v-else class="challenge-list">
      <div v-for="challenge in challenges" :key="challenge.difficulty" class="challenge-group">
        <h3 class="challenge-difficulty">{{ challenge.difficulty }}</h3>
        <p v-if="challenge.players.length === 0" class="empty-state small">No entries yet.</p>
        <ol v-else class="rows">
          <li
            v-for="player in challenge.players"
            :key="player.nickname"
            class="row"
            :class="{ 'is-viewer': player.nickname === viewerNickname }"
          >
            <span class="rank" :class="{ medal: player.rank <= 3 }">{{ rankBadge(player.rank) }}</span>
            <span class="name">{{ player.nickname }}</span>
            <span class="value">{{ player.totalScore }}</span>
            <span class="sub">{{ player.totalDistance }}</span>
          </li>
        </ol>
      </div>
    </div>
  </section>
</template>
