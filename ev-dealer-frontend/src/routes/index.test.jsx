/**
 * Role-gate tests for /admin/users.
 *
 * The bug these protect against: routes/index.jsx used to pass
 * `roles={['Admin']}` (an array prop) while ProtectedRoute reads
 * `requiredRole` (a string). The mismatch was silently ignored, so EVERY
 * logged-in user reached UserManagement — the server refused the data
 * ([Authorize(Roles = "Admin")]), but the page itself rendered instead of
 * redirecting.
 *
 * These tests render the REAL AppRoutes (not a hand-copied route), so
 * they fail if the call-site prop name drifts again, not just if the
 * component's role check breaks.
 *
 * Vitest runs with import.meta.env.DEV forced false (see vite.config.js)
 * so ProtectedRoute's development bypass cannot make these vacuously pass.
 */

import { describe, it, expect, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import { MemoryRouter, useLocation } from 'react-router-dom'

import AppRoutes from './index'

// The three components the assertions actually observe, stubbed so the test
// measures ROUTING (who gets in) rather than each page's API calls.
// MainLayout MUST render <Outlet/>: it is the parent route element, so a
// stub without an Outlet silently never mounts the /admin/users child route —
// and every child-level assertion then "passes"/"fails" against nothing.
vi.mock('../layouts/MainLayout', async () => {
  const { Outlet } = await import('react-router-dom')
  return { default: () => <div data-testid="layout"><Outlet /></div> }
})
vi.mock('../pages/Dashboard/Dashboard', () => ({
  default: () => <div data-testid="dashboard" />,
}))
vi.mock('../pages/Admin/UserManagement', () => ({
  default: () => <div data-testid="user-management" />,
}))

const PathnameProbe = () => {
  const { pathname } = useLocation()
  return <div data-testid="pathname">{pathname}</div>
}

const loginAs = (role) => {
  localStorage.setItem('token', 'test-jwt')
  localStorage.setItem(
    'user',
    JSON.stringify({ id: '5', name: 'Test User', email: 't@example.com', role })
  )
}

const renderAt = (path) =>
  render(
    <MemoryRouter initialEntries={[path]}>
      <PathnameProbe />
      <AppRoutes />
    </MemoryRouter>
  )

describe('/admin/users role gate', () => {
  it('redirects a logged-in NON-admin to /dashboard', () => {
    loginAs('Customer')
    renderAt('/admin/users')

    expect(screen.getByTestId('pathname')).toHaveTextContent('/dashboard')
    expect(screen.queryByTestId('user-management')).toBeNull()
  })

  it('renders UserManagement for an Admin', () => {
    loginAs('Admin')
    renderAt('/admin/users')

    expect(screen.getByTestId('pathname')).toHaveTextContent('/admin/users')
    expect(screen.getByTestId('user-management')).toBeInTheDocument()
  })

  it('redirects an unauthenticated visitor to /login', () => {
    // no token — localStorage is cleared after every test
    renderAt('/admin/users')

    expect(screen.getByTestId('pathname')).toHaveTextContent('/login')
    expect(screen.queryByTestId('user-management')).toBeNull()
  })
})
