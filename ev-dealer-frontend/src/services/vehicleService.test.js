/**
 * No-mock-fallback tests.
 *
 * vehicleService used to catch every failure and answer from
 * src/data/mockVehicles.js. The read paths shipped invented cars; the
 * mutation paths were the real hazard — createVehicle returned
 * {id, ...}, deleteVehicle returned {success: true} and reserveVehicle
 * returned a Pending reservation, so the UI reported success for writes
 * that never left the browser.
 *
 * These pin that a failed request REJECTS on every path, mutation
 * included. They were mutation-tested by restoring each fallback and
 * watching the matching test turn red.
 */

import { describe, it, expect, vi, beforeEach } from 'vitest'
import vehicleService from './vehicleService'
import api from './api'

vi.mock('./api', () => ({
  default: { get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() },
}))

const boom = new Error('gateway down')

beforeEach(() => {
  vi.clearAllMocks()
  api.get.mockRejectedValue(boom)
  api.post.mockRejectedValue(boom)
  api.put.mockRejectedValue(boom)
  api.delete.mockRejectedValue(boom)
})

describe('vehicleService — read paths reject instead of faking data', () => {
  it('getAllVehicles rejects', async () => {
    await expect(vehicleService.getAllVehicles()).rejects.toThrow('gateway down')
  })

  it('getVehicles rejects', async () => {
    await expect(vehicleService.getVehicles()).rejects.toThrow('gateway down')
  })

  it('getVehicleById rejects', async () => {
    await expect(vehicleService.getVehicleById(1)).rejects.toThrow('gateway down')
  })

  it('getVehicleTypes rejects', async () => {
    await expect(vehicleService.getVehicleTypes()).rejects.toThrow('gateway down')
  })

  it('getDealers rejects', async () => {
    await expect(vehicleService.getDealers()).rejects.toThrow('gateway down')
  })
})

describe('vehicleService — mutation paths must not report fake success', () => {
  // These three are the regressions worth failing CI for: the old code
  // returned a well-formed success object, so the caller's UI showed
  // "created" / "deleted" / "reserved" while the database was untouched.
  it('createVehicle rejects', async () => {
    await expect(vehicleService.createVehicle({ model: 'X' })).rejects.toThrow('gateway down')
  })

  it('updateVehicle rejects', async () => {
    await expect(vehicleService.updateVehicle(1, { model: 'X' })).rejects.toThrow('gateway down')
  })

  it('deleteVehicle rejects', async () => {
    await expect(vehicleService.deleteVehicle(1)).rejects.toThrow('gateway down')
  })

  it('reserveVehicle rejects', async () => {
    await expect(
      vehicleService.reserveVehicle(1, { customerName: 'A', customerEmail: 'a@b.c' })
    ).rejects.toThrow('gateway down')
  })
})
