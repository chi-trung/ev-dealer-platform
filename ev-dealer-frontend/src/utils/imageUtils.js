import car1Img from '../assets/img/car1.webp'
import car2Img from '../assets/img/car2.webp'
import car3Img from '../assets/img/car3.webp'
import car4Img from '../assets/img/car4.webp'

/**
 * Small helper to normalize image paths used across the app.
 * - Absolute URLs (http(s), protocol-relative //) plus data: and blob:
 *   URLs are returned as-is.
 * - Legacy pre-#83 values ("src/assets/img/carN.png",
 *   "/src/assets/img/carN.png", stale dev-resolved
 *   "/src/assets/img/carN.webp") are mapped to the Vite-imported webps:
 *   the .png sources are deleted and production builds never serve /src/*,
 *   so returning those strings would serve index.html as the image on Vercel.
 * - Vite frontend files (/assets/* build output, /src/* dev source) stay
 *   same-origin — NEVER prefixed with the API origin. (Issue #83: the
 *   vercel.json SPA rewrite only bypasses /assets/*; anything else that is
 *   a frontend file must not be routed to the backend.)
 * - Every other leading-'/' path (e.g. /images/*, served by VehicleService
 *   wwwroot via UseStaticFiles + the gateway /images route) is prefixed
 *   with the API origin (VITE_API_BASE_URL minus trailing /api).
 * - Bare relative paths fall back to '/'-prefixed same-origin URLs.
 */
const LEGACY_CAR_IMAGE_MAP = {
  'car1.png': car1Img,
  'car2.png': car2Img,
  'car3.png': car3Img,
  'car4.png': car4Img,
  'car1.webp': car1Img,
  'car2.webp': car2Img,
  'car3.webp': car3Img,
  'car4.webp': car4Img,
}

function basenameOf(p) {
  const clean = String(p).split('?')[0].split('#')[0]
  const segments = clean.split('/')
  return segments[segments.length - 1].toLowerCase()
}

// Resolve a stale pre-#83 car-image reference to its bundled webp, or null.
// Strictly gated on frontend-source shapes so a same-named backend upload
// (e.g. /images/<guid>_car1.png) is never hijacked: only "src/assets/..."
// literals (bare or /-prefixed, incl. dev-resolved .webp) ever meant a
// frontend file; the deleted .png filenames alone are not enough signal.
function legacyCarImage(p) {
  const base = basenameOf(p)
  const mapped = LEGACY_CAR_IMAGE_MAP[base]
  if (!mapped) return null
  if (/(^|\/)src\/assets\//.test(p)) return mapped
  if (!p.includes('/')) return mapped // bare "car1.png" literal
  return null
}

export function resolveImagePath(path) {
  if (!path) return path
  const p = String(path)

  // Already absolute, protocol-relative, inline data, or an upload
  // preview — return as-is
  if (
    p.startsWith('http://') ||
    p.startsWith('https://') ||
    p.startsWith('//') ||
    p.startsWith('data:') ||
    p.startsWith('blob:')
  ) return p

  // Stale pre-#83 references (old DB rows, caches) → bundled webp
  const legacy = legacyCarImage(p)
  if (legacy) return legacy

  // Frontend-served files stay same-origin (Issue #83)
  if (p.startsWith('/assets/') || p.startsWith('/src/')) return p

  // Backend-served files (e.g. /images/*) go to the API origin
  if (p.startsWith('/')) {
    const apiBaseUrl = import.meta.env.VITE_API_BASE_URL || 'http://localhost:5036/api'
    // Remove /api suffix if present to get just the base domain
    const baseUrl = apiBaseUrl.replace(/\/api$/, '')
    return `${baseUrl}${p}`
  }

  // Otherwise treat as local asset
  return `/${p}`
}

/**
 * Shared inline-SVG placeholder (data URL) for vehicles with no image.
 * Same technique VehicleDetail uses for failed gallery images (Issue #83:
 * replaces the nonexistent /placeholder-car.jpg|.png fallbacks, which 404 —
 * and a leading-'/' path would have been routed to the API backend anyway).
 */
export function placeholderVehicleImage(text = 'Electric Vehicle', width = 1200, height = 700) {
  const fg = '#ffffff'
  const svg = `
      <svg xmlns='http://www.w3.org/2000/svg' width='${width}' height='${height}' viewBox='0 0 ${width} ${height}'>
        <defs>
          <linearGradient id="gradient" x1="0%" y1="0%" x2="100%" y2="100%">
            <stop offset="0%" style="stop-color:#667eea;stop-opacity:1" />
            <stop offset="100%" style="stop-color:#764ba2;stop-opacity:1" />
          </linearGradient>
        </defs>
        <rect width='100%' height='100%' fill='url(#gradient)' />
        <text x='50%' y='50%' dominant-baseline='middle' text-anchor='middle' fill='${fg}' font-family='Arial, Helvetica, sans-serif' font-size='36' font-weight='600'>${text}</text>
      </svg>`
  return `data:image/svg+xml;utf8,${encodeURIComponent(svg)}`
}

export default resolveImagePath
