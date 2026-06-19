import { defineConfig } from 'vitest/config';
import vue from '@vitejs/plugin-vue';

// The API base is `/api` in local dev (proxied to the running backend) and `/.proxy/api` inside
// Discord (routed through the activity proxy via the Developer Portal URL mappings).
export default defineConfig({
  plugins: [vue()],
  server: {
    proxy: {
      '/api': {
        target: 'http://localhost:5194',
        changeOrigin: true,
      },
    },
  },
  build: {
    outDir: 'dist',
    emptyOutDir: true,
  },
  test: {
    environment: 'jsdom',
    globals: true,
    include: ['src/**/*.spec.ts'],
    exclude: ['node_modules', 'dist', 'e2e'],
  },
});
