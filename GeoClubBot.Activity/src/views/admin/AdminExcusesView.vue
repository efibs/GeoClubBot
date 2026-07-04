<script setup lang="ts">
import { onMounted, ref } from 'vue';
import { storeToRefs } from 'pinia';
import { useAdminStore } from '../../stores/admin';
import { formatDate, toDateInputValue } from '../../format';
import { confirm } from '../../composables/useConfirm';
import Spinner from '../../components/Spinner.vue';
import type { AdminExcuseDto } from '../../types';

const admin = useAdminStore();
const { excuses } = storeToRefs(admin);

// One form serves both adding and editing; editingId decides which.
const editingId = ref<string | null>(null);
const nickname = ref('');
const from = ref('');
const to = ref('');
// The excuse whose row action is currently running, so only that button shows a spinner.
const pendingId = ref<string | null>(null);

onMounted(() => {
  if (!excuses.value.data) {
    void admin.loadExcuses();
  }
});

function startEdit(excuse: AdminExcuseDto): void {
  editingId.value = excuse.excuseId;
  nickname.value = excuse.nickname;
  // Use the local calendar date (like the list's formatDate), not the raw UTC prefix — an excuse
  // stored at local midnight comes back as the previous day in UTC, so slice(0, 10) shifted it back.
  from.value = toDateInputValue(excuse.from);
  to.value = toDateInputValue(excuse.to);
}

function resetForm(): void {
  editingId.value = null;
  nickname.value = '';
  from.value = '';
  to.value = '';
}

async function submit(): Promise<void> {
  if (!from.value || !to.value) {
    return;
  }
  const succeeded = editingId.value
    ? await admin.updateExcuse(editingId.value, from.value, to.value)
    : await admin.addExcuse(nickname.value.trim(), from.value, to.value);
  if (succeeded) {
    resetForm();
  }
}

async function remove(excuse: AdminExcuseDto): Promise<void> {
  const message = `Remove the excuse of ${excuse.nickname} (${formatDate(excuse.from)} → ${formatDate(excuse.to)})?`;
  if (await confirm({ message, danger: true })) {
    pendingId.value = excuse.excuseId;
    await admin.removeExcuse(excuse.excuseId);
    pendingId.value = null;
  }
}
</script>

<template>
  <main class="panels" data-testid="admin-excuses-view">
    <p v-if="excuses.error" class="error-banner" data-testid="error-banner">⚠️ {{ excuses.error }}</p>

    <section class="panel" data-testid="excuse-form-panel">
      <h2 class="panel-title">{{ editingId ? '✏️ Edit excuse' : '➕ Add excuse' }}</h2>
      <form class="reminder-form" @submit.prevent="submit">
        <label class="field-label" for="excuse-nickname">GeoGuessr nickname</label>
        <input
          id="excuse-nickname"
          v-model="nickname"
          type="text"
          :required="!editingId"
          :disabled="editingId !== null"
          class="field-input"
          data-testid="excuse-nickname"
        />
        <label class="field-label" for="excuse-from">From</label>
        <input id="excuse-from" v-model="from" type="date" required class="field-input" data-testid="excuse-from" />
        <label class="field-label" for="excuse-to">To</label>
        <input id="excuse-to" v-model="to" type="date" required class="field-input" data-testid="excuse-to" />
        <div class="form-actions">
          <button
            type="submit"
            class="action-button"
            :disabled="excuses.busy"
            data-testid="excuse-submit"
          >
            <Spinner v-if="excuses.busy" />
            {{ editingId ? (excuses.busy ? 'Saving…' : 'Save changes') : (excuses.busy ? 'Adding…' : 'Add excuse') }}
          </button>
          <button
            v-if="editingId"
            type="button"
            class="action-button danger"
            data-testid="excuse-cancel-edit"
            @click="resetForm"
          >
            Cancel
          </button>
        </div>
      </form>
    </section>

    <section class="panel panel-wide" data-testid="excuses-panel">
      <h2 class="panel-title">🏖️ Excuses</h2>
      <ul v-if="excuses.data && excuses.data.length > 0" class="rows">
        <li
          v-for="excuse in excuses.data"
          :key="excuse.excuseId"
          class="row row-with-actions"
          :data-testid="`excuse-${excuse.excuseId}`"
        >
          <span class="rank">🏖️</span>
          <span class="name">{{ excuse.nickname }}</span>
          <span class="value">{{ formatDate(excuse.from) }} → {{ formatDate(excuse.to) }}</span>
          <span class="row-actions">
            <button
              type="button"
              class="action-button small"
              :disabled="excuses.busy"
              data-testid="excuse-edit"
              @click="startEdit(excuse)"
            >
              Edit
            </button>
            <button
              type="button"
              class="action-button danger small"
              :disabled="excuses.busy"
              data-testid="excuse-remove"
              @click="remove(excuse)"
            >
              <Spinner v-if="pendingId === excuse.excuseId" />
              {{ pendingId === excuse.excuseId ? 'Removing…' : 'Remove' }}
            </button>
          </span>
        </li>
      </ul>
      <p v-else-if="excuses.data" class="empty-state" data-testid="excuses-empty">No excuses recorded.</p>
      <p v-else-if="excuses.loading" class="empty-state">Loading…</p>
    </section>
  </main>
</template>
