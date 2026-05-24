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
        app: './src/frontend/app.entry.tsx'
      },
      output: {
        entryFileNames: 'js/[name].bundle.js',
        chunkFileNames: 'js/[name].chunk.js',
        assetFileNames: 'assets/[name][extname]',
        banner: bannerComment
      }
    }
  },
  // @ts-ignore
  test: {
    exclude: [
      '**/node_modules/**',
      '**/dist/**',
      '**/tests/e2e/**',
      '**/.{idea,git,cache,output,temp}/**'
    ]
  }
});
