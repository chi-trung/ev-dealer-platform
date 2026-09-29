import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import { readFileSync, writeFileSync } from 'node:fs'
import { resolve } from 'node:path'
import {
  SERVICE_WORKER_FILENAME,
  resolveSdkVersion,
  buildFirebaseConfig,
  renderServiceWorker,
} from './src/firebase/serviceWorkerTemplate.js'

/**
 * Generate the FCM service worker into publicDir so Vite's normal public
 * copy carries it into dist/.
 *
 * The worker is a static asset and cannot read import.meta.env, so its
 * config and SDK version have to be baked in at build time. Writing it into
 * publicDir at configResolved (rather than emitting an extra file from
 * generateBundle) keeps a single copy on disk: the file you edit is the file
 * that ships. Measured: both routes survive the publicDir copy, this one
 * leaves no build-only artifact behind.
 *
 * public/firebase-messaging-sw.js is gitignored and generated — see
 * .gitignore. Editing it directly is pointless; edit
 * src/firebase/serviceWorkerTemplate.js instead.
 */
function generateServiceWorker() {
  let sdkVersion
  try {
    // Read the INSTALLED version, not the "^12.6.0" range in package.json:
    // the CDN URL needs a concrete version or the worker drifts a major
    // version whenever the lockfile is refreshed.
    const lockfile = JSON.parse(readFileSync(resolve('package-lock.json'), 'utf8'))
    sdkVersion = resolveSdkVersion(lockfile)
  } catch (error) {
    // Fail the build rather than emit a worker that loads a guessed SDK.
    throw new Error(
      `Could not generate ${SERVICE_WORKER_FILENAME}: ${error.message}`
    )
  }

  const { config, missing } = buildFirebaseConfig({
    VITE_FIREBASE_API_KEY: process.env.VITE_FIREBASE_API_KEY,
    VITE_FIREBASE_AUTH_DOMAIN: process.env.VITE_FIREBASE_AUTH_DOMAIN,
    VITE_FIREBASE_PROJECT_ID: process.env.VITE_FIREBASE_PROJECT_ID,
    VITE_FIREBASE_STORAGE_BUCKET: process.env.VITE_FIREBASE_STORAGE_BUCKET,
    VITE_FIREBASE_MESSAGING_SENDER_ID: process.env.VITE_FIREBASE_MESSAGING_SENDER_ID,
    VITE_FIREBASE_APP_ID: process.env.VITE_FIREBASE_APP_ID,
    VITE_FIREBASE_MEASUREMENT_ID: process.env.VITE_FIREBASE_MEASUREMENT_ID,
  })

  if (missing.length > 0) {
    // A warning, not an error: the app itself already boots without these
    // (firebaseConfig.js reads the same vars and is equally undefined), so
    // failing here would break `npm run build` for anyone who only wants a
    // bundle. The generated worker logs the same list at runtime.
    console.warn(
      `\n[firebase] ${SERVICE_WORKER_FILENAME} built with ${missing.length} unset config ` +
      `field(s): ${missing.join(', ')}\n` +
      `         Push notifications will not work. See .env.example.\n`
    )
  }

  return { sdkVersion, config, missing }
}

// https://vite.dev/config/
export default defineConfig({
  plugins: [
    // Before react() so the worker exists on disk before anything else
    // inspects publicDir.
    {
      name: 'generate-fcm-service-worker',
      configResolved(config) {
        const { sdkVersion, config: firebaseConfig, missing } = generateServiceWorker()
        const target = resolve(config.publicDir, SERVICE_WORKER_FILENAME)
        const source = renderServiceWorker({ sdkVersion, config: firebaseConfig, missing })
        // publicDir is copied verbatim during build and served directly in
        // dev, so writing the file there is enough for both paths.
        writeFileSync(target, source, 'utf8')
      },
    },
    react(),
  ],
  test: {
    environment: 'jsdom',
    setupFiles: './src/test/setup.js',
    // NOTE: import.meta.env.DEV stays true under Vitest and `test.define`
    // cannot flip it — vite-node serves import.meta.env at runtime instead
    // of statically replacing it. src/test/setup.js mutates DEV to false so
    // ProtectedRoute's development bypass cannot make role-gate tests pass
    // vacuously. (Measured, not assumed.)
  },
})
