import { defineConfig } from 'vite';
import path from 'path';

export default defineConfig({
  esbuild: {
    jsx: 'automatic',
    jsxImportSource: 'preact'
  },
  root: './',
  resolve: {
    alias: {
      '@': path.resolve(__dirname, './src/frontend')
    }
  },
  build: {
    outDir: './wwwroot/dist',
    emptyOutDir: true,
    manifest: true,
    rollupOptions: {
      input: {
        layout: './src/frontend/layout.entry.ts',
        dashboards: './src/frontend/dashboards.entry.ts',
        'index.page': './src/frontend/pages/index.page.ts',
        'favorites.page': './src/frontend/pages/favorites.page.ts',
        'assets.page': './src/frontend/pages/assets.page.ts',
        'alarms.page': './src/frontend/pages/alarms.page.ts',
        'heartbeat.page': './src/frontend/pages/heartbeat.page.ts',
        'logs.page': './src/frontend/pages/logs.page.ts'
      },
      output: {
        entryFileNames: 'js/[name].bundle.js',
        chunkFileNames: 'js/[name].chunk.js',
        assetFileNames: 'assets/[name][extname]'
      }
    }
  }
});
