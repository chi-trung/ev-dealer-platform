/**
 * Vitest setup.
 */
import { afterEach, vi } from 'vitest'
import { cleanup } from '@testing-library/react'
import '@testing-library/jest-dom/vitest'

// Vitest runs in dev mode, so import.meta.env.DEV is true and
// ProtectedRoute's development bypass would return children before any auth
// or role check — making these tests vacuously pass. The env object is shared
// across the worker, so flipping it here (test files load after setup) forces
// every component to take the PRODUCTION branch, the one that enforces
// requiredRole. `test.define` in vite.config.js does NOT do this: vite-node
// serves import.meta.env at runtime instead of statically replacing it.
import.meta.env.DEV = false
import.meta.env.PROD = true

// 16 files barrel-import `@mui/icons-material` (`import { X } from ...`), and
// vite-node does not tree-shake — loading that barrel pulls ~16k ESM files
// into the graph and blows the process file-handle limit (EMFILE: too many
// open files) before a single test runs. Icons are irrelevant to these
// routing tests, so the barrel is replaced by a Proxy whose named exports
// are no-op components. Deep imports (`@mui/icons-material/Warning`) are
// untouched and still load the real file.
// `then` MUST stay undefined or the namespace becomes thenable and `await`
// of the module hangs; `default` exists for `import X from` style imports.
vi.mock('@mui/icons-material', () =>
  new Proxy(
    {},
    {
      // Vitest resolves a named export with `name in factoryResult` before
      // `factoryResult[name]`; an empty target would report "No X export is
      // defined on the mock", so every key must be claimed here too.
      has: () => true,
      get: (_target, name) => {
        if (typeof name !== 'string') return undefined
        if (name === 'then' || name === '__esModule') return undefined
        if (name === 'default') return () => null
        return () => null
      },
    }
  )
)

afterEach(() => {
  cleanup()
  localStorage.clear()
})
