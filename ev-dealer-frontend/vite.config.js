import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
  test: {
    environment: 'jsdom',
    setupFiles: './src/test/setup.js',
    // NOTE: import.meta.env.DEV stays true under Vitest and `test.define`
    // cannot flip it — vite-node serves import.meta.env at runtime instead
    // of statically replacing it. src/test/setup.js mutates DEV to false so
    // ProtectedRoute's development bypass cannot make role-gate tests pass
    // vacuously. (Measured, not assumed.)
  },
});
