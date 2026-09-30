/**
 * AuthContext boot behaviour.
 *
 * Bug these protect against: AuthProvider mounted on EVERY page (main.jsx)
 * and used to call GET /users/me unconditionally. On the public landing page
 * — before anyone pressed Login — that request had no token, the server
 * answered 401, and api.js's response interceptor wiped storage and did
 * window.location.href = "/login": a visitor was kicked off "/" on first
 * load. No token means the answer is already known (signed out).
 */

import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen } from '@testing-library/react'
import { AuthProvider } from './AuthContext'
import { useAuth } from './useAuth'
import api from '../services/api'

vi.mock('../services/api', () => ({ default: { get: vi.fn() } }))

const Probe = () => {
  const { user, loading } = useAuth()
  return (
    <div data-testid="probe">{`${loading ? 'loading' : 'ready'}:${user?.role ?? 'none'}`}</div>
  )
}

const renderAuth = () =>
  render(
    <AuthProvider>
      <Probe />
    </AuthProvider>
  )

beforeEach(() => {
  api.get.mockReset()
})

describe('AuthProvider boot', () => {
  it('does NOT call /users/me when there is no token', async () => {
    // no token in localStorage — the public landing-page case
    renderAuth()

    // children stay hidden while loading; it must settle without a request
    expect(await screen.findByTestId('probe')).toHaveTextContent('ready:none')
    expect(api.get).not.toHaveBeenCalled()
  })

  it('calls /users/me once when a token exists', async () => {
    localStorage.setItem('token', 'test-jwt')
    api.get.mockResolvedValue({ id: 5, role: 'Admin' })

    renderAuth()

    expect(await screen.findByTestId('probe')).toHaveTextContent('ready:Admin')
    expect(api.get).toHaveBeenCalledTimes(1)
    expect(api.get).toHaveBeenCalledWith('/users/me')
  })
})
