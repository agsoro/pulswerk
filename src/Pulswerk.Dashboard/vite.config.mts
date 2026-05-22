import { defineConfig } from 'vite';
import path from 'path';
import fs from 'fs';

let version = '1.0.0';
try {
  const versionPath = path.resolve(__dirname, '../../version.txt');
  const versionBuild = fs.readFileSync(versionPath, 'utf8').trim();
  version = `2.6.${versionBuild}`;
} catch (e) {
  console.warn('Could not read version.txt, using fallback version 1.0.0', e);
}

const bannerComment = `/*! Pulswerk v${version} */`;

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
        'logs.page': './src/frontend/pages/logs.page.ts',
        'config.page': './src/frontend/pages/config.page.tsx'
      },
      output: {
        entryFileNames: 'js/[name].bundle.js',
        chunkFileNames: 'js/[name].chunk.js',
        assetFileNames: 'assets/[name][extname]',
        banner: bannerComment
      }
    }
  }
});
