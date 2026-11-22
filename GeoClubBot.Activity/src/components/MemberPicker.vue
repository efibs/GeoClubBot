<script setup lang="ts">
// Picks a tracked member by nickname and models their GeoGuessr user id, so the admin endpoints are
// never asked to find a member by a nickname that may be shared or changed (#139). A native
// <datalist> offers the members as the admin types; only an exact label counts as a pick.
import { computed, ref, watch } from 'vue';
import { buildMemberOptions, resolveMemberOption } from '../memberOptions';
import type { AdminMemberDto } from '../types';

defineOptions({ inheritAttrs: false });

/** The picked member's user id; null while nothing (or nothing known) is typed. */
const userId = defineModel<string | null>({ required: true });

const props = withDefaults(
  defineProps<{
    id: string;
    label: string;
    members: readonly AdminMemberDto[] | undefined;
    required?: boolean;
    disabled?: boolean;
  }>(),
  { required: false, disabled: false },
);

const options = computed(() => buildMemberOptions(props.members ?? []));
const text = ref('');
const listId = computed(() => `${props.id}-members`);

const unresolved = computed(() => text.value.trim() !== '' && userId.value === null);

watch(text, (value) => {
  userId.value = resolveMemberOption(options.value, value)?.userId ?? null;
});

// The member list may arrive after the admin typed; resolve again once it does.
watch(options, () => {
  userId.value = resolveMemberOption(options.value, text.value)?.userId ?? null;
});

// The parent resets the form by clearing the id. A null that comes from the text naming nobody
// leaves the text alone; a null while the text still names a member came from the parent.
watch(userId, (value) => {
  if (value === null && resolveMemberOption(options.value, text.value) !== null) {
    text.value = '';
  }
});
</script>

<template>
  <label class="field-label" :for="id">{{ label }}</label>
  <input
    :id="id"
    v-model="text"
    v-bind="$attrs"
    class="field-input"
    type="text"
    autocomplete="off"
    :list="listId"
    :placeholder="members ? 'Start typing a nickname' : 'Loading members…'"
    :required="required"
    :disabled="disabled"
    :aria-invalid="unresolved"
  />
  <datalist :id="listId">
    <option v-for="option in options" :key="option.userId" :value="option.label" />
  </datalist>
  <p v-if="unresolved" class="field-hint" :data-testid="`${id}-unresolved`">
    No tracked member has that name — pick one of the suggestions.
  </p>
</template>

<style scoped>
.field-hint {
  margin: 4px 0 0;
  color: var(--text-muted);
  font-size: 0.85em;
}
</style>
