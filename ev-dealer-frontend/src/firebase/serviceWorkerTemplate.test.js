/**
 * Service worker generation tests.
 *
 * These exist because the worker used to be a hand-edited static file with
 * two copies of the truth (a pinned compat SDK version and a hard-coded
 * Firebase project config) that nothing checked against the app. Each test
 * below was mutation-tested: reintroducing the specific drift it guards
 * against turned the matching assertion red.
 */

import { describe, it, expect } from 'vitest'
import { readFileSync } from 'node:fs'
import { resolveSdkVersion, buildFirebaseConfig, renderServiceWorker, SERVICE_WORKER_FILENAME, SERVICE_WORKER_URL } from './serviceWorkerTemplate'

const FULL_ENV = {
  VITE_FIREBASE_API_KEY: 'AIzaTestKey',
  VITE_FIREBASE_AUTH_DOMAIN: 'test-project.firebaseapp.com',
  VITE_FIREBASE_PROJECT_ID: 'test-project',
  VITE_FIREBASE_STORAGE_BUCKET: 'test-project.firebasestorage.app',
  VITE_FIREBASE_MESSAGING_SENDER_ID: '520880625592',
  VITE_FIREBASE_APP_ID: '1:520880625592:web:abc123',
  VITE_FIREBASE_MEASUREMENT_ID: 'G-TEST123',
}

describe('resolveSdkVersion', () => {
  it('reads the exact installed version from the lockfile, not the caret range', () => {
    const lockfile = {
      packages: { 'node_modules/firebase': { version: '12.6.0' } },
    }
    // The range in package.json is "^12.6.0"; a caret here would let the
    // worker's CDN URL drift to whatever is newest at build time.
    expect(resolveSdkVersion(lockfile)).toBe('12.6.0')
    expect(resolveSdkVersion(lockfile)).not.toContain('^')
  })

  it('throws rather than guessing when the lockfile has no firebase entry', () => {
    // Silently falling back to a hard-coded version is exactly the drift
    // this replaces, so an unresolvable lockfile has to stop the build.
    expect(() => resolveSdkVersion({ packages: {} })).toThrow(/package-lock\.json/)
    expect(() => resolveSdkVersion(undefined)).toThrow(/package-lock\.json/)
  })
})

describe('buildFirebaseConfig', () => {
  it('maps every VITE_FIREBASE_* var into the Firebase web config', () => {
    const { config, missing } = buildFirebaseConfig(FULL_ENV)
    expect(config).toEqual({
      apiKey: 'AIzaTestKey',
      authDomain: 'test-project.firebaseapp.com',
      projectId: 'test-project',
      storageBucket: 'test-project.firebasestorage.app',
      messagingSenderId: '520880625592',
      appId: '1:520880625592:web:abc123',
      measurementId: 'G-TEST123',
    })
    expect(missing).toEqual([])
  })

  it('reports unset fields instead of emitting them as undefined', () => {
    // The old worker shipped a config object whose fields could be
    // undefined; initializeApp() then bound the worker to a project that
    // did not exist and every background message went nowhere.
    const { config, missing } = buildFirebaseConfig({ ...FULL_ENV, VITE_FIREBASE_PROJECT_ID: '' })
    expect(config.projectId).toBeUndefined()
    expect(Object.values(config)).not.toContain(undefined)
    expect(missing).toEqual(['VITE_FIREBASE_PROJECT_ID'])
  })
})

describe('renderServiceWorker', () => {
  const source = renderServiceWorker({
    sdkVersion: '12.6.0',
    config: buildFirebaseConfig(FULL_ENV).config,
  })

  it('loads the same SDK version the app installs', () => {
    expect(source).toContain('https://www.gstatic.com/firebasejs/12.6.0/firebase-app-compat.js')
    expect(source).toContain('https://www.gstatic.com/firebasejs/12.6.0/firebase-messaging-compat.js')
    expect(source).not.toContain('10.7.1')
  })

  it('embeds the env-provided project, not a hard-coded one', () => {
    expect(source).toContain('"projectId": "test-project"')
    // The project the old worker was pinned to.
    expect(source).not.toContain('ev-dealer-management-6c620')
  })

  it('does not request assets that do not exist in public/', () => {
    // /logo.png, /badge.png, /icons/view.png and /icons/close.png were all
    // absent from public/ — four 404s per background notification. The
    // check is on executable lines only: the generated header explains in a
    // comment why those references were dropped, and naming them there is
    // the whole point.
    const code = source
      .split('\n')
      .filter((line) => !line.trim().startsWith('//'))
      .join('\n')

    for (const missing of ['/logo.png', '/badge.png', '/icons/view.png', '/icons/close.png']) {
      expect(code).not.toContain(missing)
    }
    // Sanity: the code filter must not be vacuous.
    expect(code).toContain('onBackgroundMessage')
  })

  it('warns loudly when config is missing but still installs', () => {
    const { config, missing } = buildFirebaseConfig({})
    const warned = renderServiceWorker({ sdkVersion: '12.6.0', config, missing })
    expect(warned).toContain('WARNING')
    expect(warned).toContain('VITE_FIREBASE_PROJECT_ID')
    // The worker must still define its handler, or a misconfigured env
    // would take push down entirely instead of just disabling it.
    expect(warned).toContain('onBackgroundMessage')
  })

  it('is emitted at the path Firebase registers by default', () => {
    // @firebase/messaging registerDefaultSw hard-codes this path; a worker
    // emitted anywhere else is never installed, with no error.
    expect(SERVICE_WORKER_FILENAME).toBe('firebase-messaging-sw.js')
    expect(SERVICE_WORKER_URL).toBe(`/${SERVICE_WORKER_FILENAME}`)
  })
})

describe('the repo stays consistent with itself', () => {
  it('the worker SDK version equals the installed firebase version', () => {
    // The drift this whole change exists to prevent: worker on one version,
    // app on another. Asserted against the real files, not fixtures.
    const lockfile = JSON.parse(readFileSync('./package-lock.json', 'utf8'))
    const sdkVersion = resolveSdkVersion(lockfile)
    const { config } = buildFirebaseConfig(FULL_ENV)
    const source = renderServiceWorker({ sdkVersion, config })
    expect(source).toContain(`firebasejs/${sdkVersion}/firebase-app-compat.js`)
  })
})
