/// <reference types="vite/client" />

interface ImportMetaEnv {
  /** Discord application (client) id used by the Embedded App SDK. */
  readonly VITE_DISCORD_CLIENT_ID?: string;
  /** When 'true', the Discord SDK handshake is skipped (local dev / E2E outside Discord). */
  readonly VITE_DEV_BYPASS?: string;
  /** Overrides the API base path (defaults derived from VITE_DEV_BYPASS). */
  readonly VITE_API_BASE?: string;
}

interface ImportMeta {
  readonly env: ImportMetaEnv;
}
