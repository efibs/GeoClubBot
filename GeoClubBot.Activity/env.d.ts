/// <reference types="vite/client" />

interface ImportMetaEnv {
  /** When 'true', the Discord SDK handshake is skipped (local dev / E2E outside Discord). */
  readonly VITE_DEV_BYPASS?: string;
  /** Overrides the API base path (defaults derived from VITE_DEV_BYPASS). */
  readonly VITE_API_BASE?: string;
}

interface ImportMeta {
  readonly env: ImportMetaEnv;
}
