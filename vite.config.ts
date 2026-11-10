import tailwindcss from '@tailwindcss/vite';
import react from '@vitejs/plugin-react';
import path from 'path';
import {defineConfig} from 'vite';

export default defineConfig(() => {
  const isAiStudioPreview =
    process.env.DISABLE_HMR === 'true' ||
    Boolean(process.env.K_SERVICE) ||
    Boolean(process.env.APP_URL?.includes('.run.app')) ||
    Boolean(process.env.GOOGLE_RUNTIME);

  return {
    plugins: [react(), tailwindcss()],
    resolve: {
      alias: {
        '@': path.resolve(__dirname, '.'),
      },
    },
    server: {
      // HMR is disabled in AI Studio preview (where the proxy does not forward Vite HMR WebSockets)
      // or when DISABLE_HMR=true, while remaining enabled in standard local development.
      hmr: !isAiStudioPreview,
      watch: isAiStudioPreview ? null : {},
    },
  };
});

