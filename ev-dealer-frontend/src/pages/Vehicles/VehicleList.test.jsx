/**
 * VehicleList — the duplicate mount fetch.
 *
 * VehicleList.jsx used to have TWO effects that both called loadVehicles():
 * a mount effect with `[]` (which also called loadFilterOptions) and a second
 * effect keyed on [pagination.page, pagination.limit, searchTerm, filters].
 * Both fire on mount, so every visit to /vehicles issued two identical
 * `GET /vehicles` requests.
 *
 * That is not just wasted bandwidth. Each response writes `pagination.total`
 * and `pagination.totalPages` from its own `loadVehicles` closure, so the
 * page could end up displaying a total computed from a stale page size while
 * the visible rows came from the other response — a self-inconsistent list
 * with no error anywhere to explain it.
 *
 * The single-effect fix keys the fetch on the four values that actually shape
 * the request. This test pins the request COUNT, which is the property that
 * regressed, and also pins that a filter change still refetches (so the fix
 * cannot be "just delete the second effect").
 */

import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, act, fireEvent } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'

import VehicleList from './VehicleList'
import vehicleService from '../../services/vehicleService'

vi.mock('../../services/vehicleService', () => ({
  default: {
    getVehicles: vi.fn(),
    getVehicleTypes: vi.fn(),
    getDealers: vi.fn(),
    getVehicleById: vi.fn(),
    createVehicle: vi.fn(),
    updateVehicle: vi.fn(),
    deleteVehicle: vi.fn(),
  },
}))

const emptyPage = {
  vehicles: [],
  pagination: { total: 0, totalPages: 0, page: 1, limit: 12 },
}

beforeEach(() => {
  vi.clearAllMocks()
  vehicleService.getVehicles.mockResolvedValue(emptyPage)
  // Shape matters: VehicleList.jsx:303-307 maps `type.value` / `type.label`,
  // so an array of plain strings renders empty options and no test that opens
  // the listbox can find anything. (Caught by running the test, not by
  // reading it.)
  vehicleService.getVehicleTypes.mockResolvedValue([
    { value: 'sedan', label: 'Sedan' },
    { value: 'suv', label: 'SUV' },
  ])
  vehicleService.getDealers.mockResolvedValue([])
})

const renderList = () =>
  render(
    <MemoryRouter>
      <VehicleList />
    </MemoryRouter>
  )

/**
 * MUI's Select renders a div[role=combobox] with no <input id>, so
 * getByLabelText cannot reach it (verified: "Found a label with the text of:
 * ... however no form control was found associated to that label"). Drive it
 * through userEvent on the combobox itself and pick from the listbox MUI opens.
 */
const comboboxes = (container) => container.querySelectorAll('[role="combobox"]')

describe('VehicleList — vehicle fetch count', () => {
  it('issues exactly ONE getVehicles call on mount', async () => {
    await act(async () => {
      renderList()
    })

    // Filter options load separately and are not under test here.
    expect(vehicleService.getVehicles).toHaveBeenCalledTimes(1)
  })

  it('refetches exactly once more when the vehicle-type filter changes', async () => {
    const { container } = render(
      <MemoryRouter>
        <VehicleList />
      </MemoryRouter>
    )

    await act(async () => {})
    expect(vehicleService.getVehicles).toHaveBeenCalledTimes(1)

    // The first combobox is "Loại xe" (VehicleList.jsx:295-308). MUI opens the
    // listbox on mouseDown, not click — fireEvent.mouseDown is what the
    // component actually listens for.
    fireEvent.mouseDown(comboboxes(container)[0])

    const option = await screen.findByRole('option', { name: 'SUV' })
    await act(async () => {
      fireEvent.click(option)
    })

    // One NEW fetch, not two — this is the property the second effect used to
    // double up on, and it must survive the fix.
    expect(vehicleService.getVehicles).toHaveBeenCalledTimes(2)
    expect(vehicleService.getVehicles.mock.calls[1][0]).toMatchObject({ type: 'suv' })
  })

  it('sends page and limit, and omits empty filters from the request', async () => {
    await act(async () => {
      renderList()
    })

    const params = vehicleService.getVehicles.mock.calls[0][0]
    expect(params.page).toBe(1)
    expect(params.limit).toBe(12)
    // The '' filters are stripped before the call (VehicleList.jsx:107-111).
    expect(Object.values(params)).not.toContain('')
    // searchTerm is '' on mount and must not reach the gateway as `search: ''`.
    expect(params).not.toHaveProperty('search')
  })
})
