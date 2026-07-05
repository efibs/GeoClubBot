<script setup lang="ts">
// A labelled text input for the vertical `.form-stack` forms. Renders the label + input pair the
// forms repeated by hand. Uses the global `.field-*` primitives (shared with the few raw inputs
// that can't be a FormField, like the inline OTP box). Extra attrs (data-testid, @keyup) go to the
// input.
defineOptions({ inheritAttrs: false });

const model = defineModel<string>({ required: true });

withDefaults(
  defineProps<{
    id: string;
    label: string;
    type?: string;
    placeholder?: string;
    required?: boolean;
    maxlength?: number;
    disabled?: boolean;
  }>(),
  { type: 'text', required: false },
);
</script>

<template>
  <label class="field-label" :for="id">{{ label }}</label>
  <input
    :id="id"
    v-model="model"
    v-bind="$attrs"
    class="field-input"
    :type="type"
    :placeholder="placeholder"
    :required="required"
    :maxlength="maxlength"
    :disabled="disabled"
  />
</template>
