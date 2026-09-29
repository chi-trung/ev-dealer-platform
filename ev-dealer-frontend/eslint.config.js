import js from '@eslint/js'
import globals from 'globals'
import reactHooks from 'eslint-plugin-react-hooks'
import reactRefresh from 'eslint-plugin-react-refresh'
import { defineConfig, globalIgnores } from 'eslint/config'

export default defineConfig([
  globalIgnores([
    'dist',
    // Generated at build time by the plugin in vite.config.js from
    // src/firebase/serviceWorkerTemplate.js. It runs in a service-worker
    // scope (importScripts, firebase, clients), not the browser one the
    // config below describes, so linting it produced 7 no-undef errors on
    // generated code nobody hand-writes. Lint the template instead.
    'public/firebase-messaging-sw.js',
  ]),
  {
    files: ['**/*.{js,jsx}'],
    extends: [
      js.configs.recommended,
      reactHooks.configs['recommended-latest'],
      reactRefresh.configs.vite,
    ],
    languageOptions: {
      ecmaVersion: 2020,
      globals: globals.browser,
      parserOptions: {
        ecmaVersion: 'latest',
        ecmaFeatures: { jsx: true },
        sourceType: 'module',
      },
    },
    rules: {
      'no-unused-vars': ['error', { varsIgnorePattern: '^[A-Z_]' }],
    },
  },
  {
    // Build-time Node files: the browser globals block above does not apply
    // to them, so `process` and friends were flagged as undefined.
    files: ['vite.config.js', 'vitest.config.js', 'eslint.config.js'],
    languageOptions: {
      globals: globals.node,
    },
  },
])
