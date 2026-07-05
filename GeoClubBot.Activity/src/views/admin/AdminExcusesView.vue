<script setup lang="ts">
import { ref } from 'vue';
import PanelSection from '../../components/PanelSection.vue';
import FormField from '../../components/FormField.vue';
import ActionButton from '../../components/ActionButton.vue';
import ErrorBanner from '../../components/ErrorBanner.vue';
import { useAdminExcuses } from '../../queries/admin';
import { formatDate, toDateInputValue } from '../../format';
import { confirm } from '../../composables/useConfirm';
import type { AdminExcuseDto } from '../../types';

const excuses = useAdminExcuses();

// One form serves both adding and editing; editingId decides which.
const editingId = ref<string | null>(null);
const nickname = ref('');
const from = ref('');
const to = ref('');

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
  try {
    if (editingId.value) {
      await excuses.update(editingId.value, from.value, to.value);
    } else {
      await excuses.add(nickname.value.trim(), from.value, to.value);
    }
    resetForm();
  } catch {
    // Surfaced via excuses.error.
  }
}

async function remove(excuse: AdminExcuseDto): Promise<void> {
  const message = `Remove the excuse of ${excuse.nickname} (${formatDate(excuse.from)} → ${formatDate(excuse.to)})?`;
  if (await confirm({ message, danger: true })) {
    await excuses.remove(excuse.excuseId).catch(() => {});
  }
}
</script>

<template>
  <main class="panels" data-testid="admin-excuses-view">
    <ErrorBanner v-if="excuses.error" data-testid="error-banner">{{ excuses.error }}</ErrorBanner>

    <PanelSection
      :title="editingId ? '✏️ Edit excuse' : '➕ Add excuse'"
      data-testid="excuse-form-panel"
    >
      <form class="form-stack" @submit.prevent="submit">
        <FormField
          id="excuse-nickname"
          v-model="nickname"
          label="GeoGuessr nickname"
          :required="!editingId"
          :disabled="editingId !== null"
          data-testid="excuse-nickname"
        />
        <FormField
          id="excuse-from"
          v-model="from"
          label="From"
          type="date"
          required
          data-testid="excuse-from"
        />
        <FormField
          id="excuse-to"
          v-model="to"
          label="To"
          type="date"
          required
          data-testid="excuse-to"
        />
        <div class="form-actions">
          <ActionButton
            type="submit"
            :busy="excuses.submitting"
            :busy-label="editingId ? 'Saving…' : 'Adding…'"
            :disabled="excuses.busy"
            data-testid="excuse-submit"
          >
            {{ editingId ? 'Save changes' : 'Add excuse' }}
          </ActionButton>
          <ActionButton v-if="editingId" danger data-testid="excuse-cancel-edit" @click="resetForm">
            Cancel
          </ActionButton>
        </div>
      </form>
    </PanelSection>

    <PanelSection title="🏖️ Excuses" wide data-testid="excuses-panel">
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
            <ActionButton
              small
              :disabled="excuses.busy"
              data-testid="excuse-edit"
              @click="startEdit(excuse)"
            >
              Edit
            </ActionButton>
            <ActionButton
              danger
              small
              :busy="excuses.removingId === excuse.excuseId"
              busy-label="Removing…"
              :disabled="excuses.busy"
              data-testid="excuse-remove"
              @click="remove(excuse)"
            >
              Remove
            </ActionButton>
          </span>
        </li>
      </ul>
      <p v-else-if="excuses.data" class="empty-state" data-testid="excuses-empty">
        No excuses recorded.
      </p>
      <p v-else-if="excuses.isPending" class="empty-state">Loading…</p>
    </PanelSection>
  </main>
</template>
