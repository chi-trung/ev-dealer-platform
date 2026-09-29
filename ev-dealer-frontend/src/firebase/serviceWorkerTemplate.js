/**
 * Service worker template + build-time config injection.
 *
 * The FCM service worker used to be a hand-maintained file in public/ that
 * pinned compat SDK 10.7.1 while the app depended on firebase 12.6.0, and
 * carried its own hard-coded copy of the Firebase project config. Two
 * sources of truth for both numbers, and neither one checked anything:
 *
 *   - Point the app at a different Firebase project (.env) and push kept
 *     flowing through the OLD project. getToken() mints a token for the
 *     env-configured project, but the worker behind it was still wired to
 *     the hard-coded one — so background messages were delivered to a
 *     project nothing was listening on, with no error anywhere.
 *   - Nothing in the repo failed when those drifted apart. The build was
 *     green either way.
 *
 * The worker is a plain static asset (Firebase registers /firebase-messaging-sw.js
 * by default — see @firebase/messaging registerDefaultSw), so it cannot read
 * import.meta.env the way src/firebase/firebaseConfig.js does. It is
 * therefore GENERATED at build time by a Vite plugin in vite.config.js,
 * which calls the pure functions below. Keeping the logic here rather than
 * in the plugin means it is unit-testable without running a build.
 */

/** Where the generated worker is written, relative to publicDir. */
export const SERVICE_WORKER_FILENAME = 'firebase-messaging-sw.js';

/** Public URL the worker must be served from — Firebase's default. */
export const SERVICE_WORKER_URL = '/firebase-messaging-sw.js';

/**
 * The exact firebase version to load in the worker.
 *
 * Read from the lockfile rather than package.json: package.json carries a
 * caret range ("^12.6.0") and the CDN path needs a concrete version. A range
 * would resolve to whatever is latest at the time the URL is built, so the
 * worker could silently jump a major version without a commit.
 *
 * @param {object} lockfile parsed package-lock.json
 * @returns {string} e.g. "12.6.0"
 * @throws if the lockfile has no resolved firebase version
 */
export function resolveSdkVersion(lockfile) {
  const entry = lockfile?.packages?.['node_modules/firebase'];
  const version = entry?.version;
  if (!version) {
    throw new Error(
      'Cannot resolve the firebase version from package-lock.json ' +
        '(no packages["node_modules/firebase"].version). The service ' +
        'worker cannot be built without pinning its SDK to the version the ' +
        'app actually installs.'
    );
  }
  return version;
}

/** The env var backing each field of the Firebase web config. */
const CONFIG_FIELDS = {
  apiKey: 'VITE_FIREBASE_API_KEY',
  authDomain: 'VITE_FIREBASE_AUTH_DOMAIN',
  projectId: 'VITE_FIREBASE_PROJECT_ID',
  storageBucket: 'VITE_FIREBASE_STORAGE_BUCKET',
  messagingSenderId: 'VITE_FIREBASE_MESSAGING_SENDER_ID',
  appId: 'VITE_FIREBASE_APP_ID',
  measurementId: 'VITE_FIREBASE_MEASUREMENT_ID',
};

/**
 * Build the Firebase web config from the build's env.
 *
 * These values are public by design — Firebase ships them in every web app,
 * and FCM authorisation rests on the VAPID key plus the service account on
 * the backend, not on apiKey. Moving them to env is about keeping ONE copy,
 * not about secrecy.
 *
 * A missing field is dropped rather than emitted as undefined, and the
 * caller is told which ones are missing so a misconfigured build says so.
 * An undefined field is what produced a worker that initialised against a
 * project that did not exist.
 *
 * @param {Record<string, string|undefined>} env
 * @returns {{config: Record<string,string>, missing: string[]}}
 */
export function buildFirebaseConfig(env) {
  const config = {};
  const missing = [];
  for (const [field, envKey] of Object.entries(CONFIG_FIELDS)) {
    const value = env?.[envKey];
    if (value === undefined || value === null || value === '') {
      missing.push(envKey);
    } else {
      config[field] = value;
    }
  }
  return { config, missing };
}

/**
 * Render the service worker source.
 *
 * Kept as pure string-building so the invariants that actually broke before
 * — SDK version matching the app, config coming from env, no dangling asset
 * paths — can be asserted in unit tests instead of discovered in production.
 *
 * @param {{sdkVersion: string, config: Record<string,string>, missing?: string[]}} options
 * @returns {string} the full service worker script
 */
export function renderServiceWorker({ sdkVersion, config, missing = [] }) {
  const configJson = JSON.stringify(config, null, 2);

  // A missing config is reported loudly in the worker console rather than
  // failing silently at initializeApp(). The worker still installs: the app
  // can run without push, and a worker that throws on load is harder to
  // diagnose than one that logs why it is inert.
  const missingWarning = missing.length
    ? `\n// WARNING: ${missing.length} Firebase config field(s) were not set at build\n` +
      `// time: ${missing.join(', ')}\n` +
      `// Push notifications cannot work until these are provided. See .env.example.\n`
    : '';

  return `// GENERATED FILE — do not edit.
//
// Written at build time by the plugin in vite.config.js (see
// src/firebase/serviceWorkerTemplate.js). Editing this file directly will
// be overwritten on the next build, and the change lost.
//
// Two things used to drift here silently and are now injected from the same
// sources the app itself uses:
//   * the compat SDK version comes from package-lock.json, so the worker
//     always loads the same firebase version the app bundles;
//   * the Firebase config comes from the VITE_FIREBASE_* env vars, so
//     pointing the app at a different project moves the worker with it.

// Import Firebase scripts from CDN
importScripts('https://www.gstatic.com/firebasejs/${sdkVersion}/firebase-app-compat.js');
importScripts('https://www.gstatic.com/firebasejs/${sdkVersion}/firebase-messaging-compat.js');

// Firebase configuration (public values, injected at build time)
${missingWarning}const firebaseConfig = ${configJson};

// Initialize Firebase in service worker
firebase.initializeApp(firebaseConfig);

// Initialize Firebase Messaging
const messaging = firebase.messaging();

// Handle background messages
messaging.onBackgroundMessage((payload) => {
  console.log('[Service Worker] Background message received:', payload);

  const notificationTitle = payload.notification?.title || 'EV Dealer Management';
  const notificationOptions = {
    body: payload.notification?.body || 'You have a new notification',
    tag: payload.data?.type || 'notification',
    data: payload.data || {},
    requireInteraction: false,
    vibrate: [200, 100, 200]
    // NOTE: icon / badge / action icons were removed. They pointed at
    // /logo.png, /badge.png, /icons/view.png and /icons/close.png, none of
    // which exist in public/ — every background notification was firing four
    // 404s and the browser silently dropped each asset. Notification action
    // buttons have poor support in Chrome for FCM messages anyway, so the
    // title and body are what the user actually sees.
  };

  // Show notification
  return self.registration.showNotification(notificationTitle, notificationOptions);
});

// Handle notification click
self.addEventListener('notificationclick', (event) => {
  console.log('[Service Worker] Notification clicked:', event);

  event.notification.close();

  // Focus an open tab if there is one, otherwise open the app.
  const urlToOpen = new URL('/', self.location.origin).href;

  event.waitUntil(
    clients.matchAll({ type: 'window', includeUncontrolled: true }).then((windowClients) => {
      for (let i = 0; i < windowClients.length; i++) {
        const client = windowClients[i];
        if (client.url === urlToOpen && 'focus' in client) {
          return client.focus();
        }
      }
      if (clients.openWindow) {
        return clients.openWindow(urlToOpen);
      }
    })
  );
});

console.log('[Service Worker] Firebase messaging service worker loaded');
`;
}
