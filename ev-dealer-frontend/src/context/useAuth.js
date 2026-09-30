/**
 * Auth context object + the useAuth consumer hook.
 *
 * Split out of AuthContext.jsx so that file exports ONLY the AuthProvider
 * component: eslint-plugin-react-refresh (rule only-export-components)
 * requires component files to be pure refresh boundaries, and a hook export
 * alongside AuthProvider made every Fast Refresh a full reload. A file with
 * no component exports is not a refresh boundary, so this one is fine.
 */
import { createContext, useContext } from 'react'

export const AuthContext = createContext(null)

export const useAuth = () => {
  const context = useContext(AuthContext)
  if (!context) {
    throw new Error('useAuth must be used within an AuthProvider')
  }
  return context
}
