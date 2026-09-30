/**
 * Toast auto-dismiss timing.
 *
 * Toast.jsx's dismiss timer effect originally listed only [duration] and
 * called a bare `handleClose` — eslint's react-hooks/exhaustive-deps flagged
 * it, and the obvious fix (wrap handleClose in useCallback with [onClose] in
 * the deps) is a TRAP here rather than a fix.
 *
 * ToastContainer passes `onClose={() => onRemove?.(id)}` — a fresh closure on
 * every parent render (Toast.jsx:97). So onClose changes identity constantly,
 * a useCallback keyed on onClose would produce a new handleClose on every
 * render, and putting handleClose in the timer effect's deps would clear and
 * re-arm the timer on every render. The toast would then never reach its own
 * duration: any parent re-render silently reset the countdown to zero, and a
 * container that re-renders on every notification would pin the timer forever.
 *
 * This test drives a re-rendering parent and asserts the timer is NOT reset,
 * which is the property the ref-based fix (onCloseRef) provides.
 */

import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { render, screen, act } from '@testing-library/react'
import { useState } from 'react'

import { ToastContainer } from './Toast'

const DURATION = 1000

const Harness = ({ onRemove }) => {
  const [tick, setTick] = useState(0)
  const toasts = [{ id: 'a', message: 'saved', duration: DURATION, type: 'success' }]

  return (
    <>
      <button onClick={() => setTick(t => t + 1)}>rerender</button>
      <span data-testid="tick">{tick}</span>
      <ToastContainer toasts={toasts} onRemove={onRemove} />
    </>
  )
}

beforeEach(() => {
  vi.useFakeTimers()
})

afterEach(() => {
  vi.useRealTimers()
})

describe('Toast — auto-dismiss timer is not re-armed by parent re-renders', () => {
  it('still dismisses on time after the parent re-renders mid-countdown', () => {
    const onRemove = vi.fn()
    render(<Harness onRemove={onRemove} />)

    // Burn 40% of the countdown.
    act(() => {
      vi.advanceTimersByTime(400)
    })
    expect(onRemove).not.toHaveBeenCalled()

    // Parent re-renders — ToastContainer builds a NEW onClose closure here.
    act(() => {
      screen.getByRole('button', { name: 'rerender' }).click()
    })
    expect(screen.getByTestId('tick')).toHaveTextContent('1')

    // Two more re-renders, still inside the original 1000ms window.
    act(() => {
      screen.getByRole('button', { name: 'rerender' }).click()
      screen.getByRole('button', { name: 'rerender' }).click()
    })
    expect(screen.getByTestId('tick')).toHaveTextContent('3')

    // Total elapsed: 400 + 600 = 1000ms — the dismiss timer fires HERE.
    // handleClose then spends another 300ms on the leave animation before
    // onRemove is invoked, so the callback lands at T+1300. Measured with a
    // throwaway timeline probe, not assumed; the 300ms is Toast.jsx's own
    // `setTimeout(..., 300)` inside handleClose.
    act(() => {
      vi.advanceTimersByTime(600)
    })
    expect(onRemove).not.toHaveBeenCalled()

    // If the timer had been re-armed on each of the 3 parent renders, it
    // would need a further 1000ms from the last click plus the 300ms
    // animation. Advancing only the animation must NOT dismiss.
    act(() => {
      vi.advanceTimersByTime(300)
    })
    expect(onRemove).toHaveBeenCalledWith('a')
  })

  it('dismisses on time with NO parent re-render at all (baseline)', () => {
    const onRemove = vi.fn()
    render(<Harness onRemove={onRemove} />)

    act(() => {
      vi.advanceTimersByTime(DURATION + 300) // + the 300ms leave animation
    })
    expect(onRemove).toHaveBeenCalledWith('a')
  })
})
