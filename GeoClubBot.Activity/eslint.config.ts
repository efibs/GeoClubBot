import pluginVue from 'eslint-plugin-vue';
import { defineConfigWithVueTs, vueTsConfigs } from '@vue/eslint-config-typescript';
import prettier from 'eslint-config-prettier';

// Flat config. `eslint-config-prettier` comes last so it switches off any stylistic rules that
// would fight Prettier — ESLint checks correctness and Vue idioms, Prettier owns formatting.
export default defineConfigWithVueTs(
  {
    name: 'app/files-to-lint',
    files: ['**/*.{ts,mts,vue}'],
  },
  {
    name: 'app/files-to-ignore',
    ignores: ['dist/**', 'playwright-report/**', 'test-results/**', 'coverage/**'],
  },
  pluginVue.configs['flat/recommended'],
  vueTsConfigs.recommended,
  {
    name: 'app/rules',
    rules: {
      // `App` is the conventional root component name; everything else is already multi-word.
      'vue/multi-word-component-names': ['error', { ignores: ['App'] }],
      // Optionality is already expressed by TypeScript `?` on the prop; a redundant runtime default
      // isn't meaningful for these presentational props.
      'vue/require-default-prop': 'off',
    },
  },
  prettier,
);
