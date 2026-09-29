<script setup lang="ts">
import { computed } from 'vue';
import type { BoardDto, BoardTileDto, ClaimState, MissionBoardDto } from '../types';
import { formatRelative } from '../format';

const props = defineProps<{ board: MissionBoardDto }>();

// Only the current board can be claimed on; cleared boards are summarised and later ones locked.
const currentBoard = computed<BoardDto | undefined>(() =>
  props.board.allBoardsCleared
    ? undefined
    : props.board.boards.find((b) => b.number === props.board.currentBoardNumber),
);

const openClaims = computed(() =>
  props.board.boards
    .flatMap((b) => b.tiles)
    .filter((t) => t.state === 'claimed')
    .sort((a, b) => (a.claimedAt ?? '').localeCompare(b.claimedAt ?? '')),
);

const totals = computed(() => {
  const tiles = props.board.boards.flatMap((b) => b.tiles);
  return { done: tiles.filter((t) => t.state === 'completed').length, all: tiles.length };
});

const claimStateText: Record<ClaimState, string> = {
  ClaimAvailable: 'You can claim a mission now',
  HoldingOpenMission: 'Finish your open mission (or request help)',
  ClaimedThisCycle: 'Claimed today — next claim after the reset',
  NoneFree: 'No mission is free right now',
};

function boardStatus(board: BoardDto): string {
  if (board.clearedAt) return 'cleared';
  return board.number === props.board.currentBoardNumber && !props.board.allBoardsCleared
    ? 'current'
    : 'locked';
}

function tileTitle(tile: BoardTileDto): string {
  const who = tile.claimerNickname ? ` — ${tile.claimerNickname}` : '';
  const help = tile.helpRequested ? ' · help requested' : '';
  return `${tile.title} (${tile.currentProgress}/${tile.targetProgress})${who}${help}`;
}
</script>

<template>
  <section class="panel board-panel" data-testid="board-panel">
    <h2 class="panel-title">🗺️ Mission board</h2>

    <p class="stat-caption">
      {{ totals.done }} / {{ totals.all }} missions done · week ends
      {{ formatRelative(board.periodEnd) }} · daily claims reset
      {{ formatRelative(board.claimCycleEnd) }}
    </p>

    <ul class="board-list" data-testid="board-list">
      <li
        v-for="b in board.boards"
        :key="b.number"
        class="board-chip"
        :class="boardStatus(b)"
        :data-testid="`board-${b.number}`"
      >
        <span>{{
          boardStatus(b) === 'cleared' ? '✅' : boardStatus(b) === 'current' ? '▶️' : '🔒'
        }}</span>
        <span>Board {{ b.number }}</span>
        <span class="chip-count">{{ b.completedCount }}/{{ b.tiles.length }}</span>
      </li>
    </ul>

    <p v-if="board.allBoardsCleared" class="empty-state" data-testid="board-all-cleared">
      🎉 Every board is cleared this week.
    </p>

    <div
      v-else-if="currentBoard"
      class="tile-grid"
      :style="{ gridTemplateColumns: `repeat(${currentBoard.size}, 1fr)` }"
      data-testid="tile-grid"
    >
      <!-- State is shown by the mark as well as the colour, never by colour alone. -->
      <div
        v-for="tile in currentBoard.tiles"
        :key="tile.missionId"
        class="tile"
        :class="[tile.state, { mine: tile.claimedByViewer, helped: tile.helpedByViewer }]"
        :title="tileTitle(tile)"
        :data-testid="`tile-${tile.state}`"
      >
        <span class="tile-mark">
          {{
            tile.state === 'completed'
              ? '✓'
              : tile.state === 'claimed'
                ? tile.helpRequested
                  ? '🆘'
                  : '●'
                : ''
          }}
        </span>
      </div>
    </div>

    <template v-if="board.viewer">
      <h3 class="subheading">You</h3>
      <p class="viewer-state" data-testid="board-viewer-state">
        {{ claimStateText[board.viewer.claimState] }}
      </p>
      <p class="stat-caption">
        {{ board.viewer.completedThisWeek }} of your {{ board.viewer.claimsThisWeek }} claim(s)
        finished this week · helped out on {{ board.viewer.helpedThisWeek }} (unverified)
      </p>
    </template>

    <template v-if="openClaims.length > 0">
      <h3 class="subheading">Open missions</h3>
      <ul class="rows" data-testid="open-claims">
        <li
          v-for="tile in openClaims"
          :key="tile.missionId"
          class="row"
          :class="{ 'is-viewer': tile.claimedByViewer }"
        >
          <span class="rank">{{ tile.helpRequested ? '🆘' : '⏳' }}</span>
          <span class="name">{{ tile.claimerNickname ?? '?' }} — {{ tile.title }}</span>
          <span class="value">{{ tile.currentProgress }}/{{ tile.targetProgress }}</span>
          <span v-if="tile.claimedAt" class="sub"
            >claimed {{ formatRelative(tile.claimedAt) }}</span
          >
        </li>
      </ul>
    </template>
  </section>
</template>

<style scoped>
.board-panel {
  grid-column: 1 / -1;
}

.board-list {
  list-style: none;
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
  margin: 12px 0;
  padding: 0;
}

.board-chip {
  display: flex;
  gap: 6px;
  align-items: center;
  padding: 5px 10px;
  border-radius: 999px;
  background: var(--bg-row);
  border: 1px solid transparent;
  font-size: 0.85rem;
}

.board-chip.current {
  border-color: var(--viewer-border);
}

.board-chip.locked {
  opacity: 0.55;
}

.chip-count {
  color: var(--text-muted);
}

.tile-grid {
  display: grid;
  gap: 6px;
  max-width: 360px;
  margin: 8px 0 4px;
}

.tile {
  aspect-ratio: 1;
  border-radius: 8px;
  background: var(--bg-row);
  border: 1px solid var(--border);
  display: flex;
  align-items: center;
  justify-content: center;
}

.tile.completed {
  background: var(--viewer);
  border-color: var(--viewer-border);
}

.tile.claimed {
  border-color: var(--viewer-border);
  border-style: dashed;
}

.tile.mine {
  outline: 2px solid var(--viewer-border);
}

.tile-mark {
  font-weight: 700;
}

.subheading {
  margin: 16px 0 6px;
  font-size: 0.95rem;
}

.viewer-state {
  margin: 0;
  font-weight: 600;
}
</style>
