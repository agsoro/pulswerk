import { defineConfig } from 'vite';
import path from 'path';
import fs from 'fs';

// ── Version ────────────────────────────────────────────────────────────────
// Read MAJOR and MINOR from Directory.Build.props so there is a single
// source of truth shared with the .NET assembly version.
function readBuildPropVersion(): { major: string; minor: string } {
  try {
    const propsPath = path.resolve(__dirname, '../../Directory.Build.props');
    const xml = fs.readFileSync(propsPath, 'utf8');
    const major = xml.match(/<VersionMajor>(\d+)<\/VersionMajor>/)?.[1] ?? '2';
    const minor = xml.match(/<VersionMinor>(\d+)<\/VersionMinor>/)?.[1] ?? '7';
    return { major, minor };
  } catch {
    return { major: '2', minor: '7' };
  }
}

let version = '0.0.0';
try {
  const { major, minor } = readBuildPropVersion();
  const patch = fs.readFileSync(path.resolve(__dirname, '../../version.txt'), 'utf8').trim();
  version = `${major}.${minor}.${patch}`;
} catch (e) {
  console.warn('Could not determine version, using fallback 0.0.0', e);
}

const bannerComment = `/*! Pulswerk v${version} */`;

export default defineConfig({
  define: {
    // Injected at build time — use as __APP_VERSION__ in TypeScript source
    __APP_VERSION__: JSON.stringify(`v${version}`)
  },
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
