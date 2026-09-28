import api from './api'

// NO MOCK FALLBACK — this file used to catch every failure and answer with
// src/data/mockVehicles. The read paths shipped invented cars to the UI, and
// the mutation paths were worse: createVehicle returned {id, ...}, deleteVehicle
// returned {success: true} and reserveVehicle returned a Pending reservation —
// so the UI reported "created" / "deleted" / "reserved" for writes that never
// left the browser. A dealer would stock a car that does not exist. Failures
// now propagate to the callers, which already render a real error state
// (VehicleList/VehicleDetail setError; VehicleForm and ReservationDialog
// surface the thrown error).
const vehicleService = {
  // Get all vehicles without pagination (for dropdowns/compare)
  getAllVehicles: async () => {
    try {
      const response = await api.get('/vehicles', { params: { PageSize: 1000 } })
      const vehicles = (response.items || response.Items || []).map(vehicle => {
        const transformedVehicle = { ...vehicle }
        if (vehicle.Images && Array.isArray(vehicle.Images)) {
          transformedVehicle.images = vehicle.Images.map(img => img.url || img.Url || img)
        } else if (vehicle.images && Array.isArray(vehicle.images)) {
          transformedVehicle.images = vehicle.images.map(img => typeof img === 'string' ? img : (img.url || img.Url || img))
        }
        return transformedVehicle
      })
      return vehicles
    } catch (error) {
      console.error('Failed to load vehicles:', error)
      throw error
    }
  },

  // Get all vehicles with optional filters and pagination
  getVehicles: async (params = {}) => {
    try {
      // Map frontend params to backend API params
      const apiParams = {
        Page: params.page || 1,
        PageSize: params.limit || 10,
        Search: params.search || undefined,
        Type: params.type && params.type !== 'all' ? params.type : undefined,
        DealerId: params.dealerId ? parseInt(params.dealerId) : undefined,
        MinPrice: params.minPrice ? parseFloat(params.minPrice) : undefined,
        MaxPrice: params.maxPrice ? parseFloat(params.maxPrice) : undefined,
        SortBy: params.sortBy || 'CreatedAt',
        SortOrder: params.sortOrder || 'desc'
      }

      // Remove undefined values
      Object.keys(apiParams).forEach(key => {
        if (apiParams[key] === undefined) {
          delete apiParams[key]
        }
      })

      const response = await api.get('/vehicles', { params: apiParams })
      
      // Transform backend response to frontend format
      // Backend returns: { Items, TotalCount, Page, PageSize, TotalPages }
      // Frontend expects: { vehicles, pagination: { page, limit, total, totalPages } }
      const vehicles = (response.items || response.Items || []).map(vehicle => {
        // Transform Images array to images array (backend uses Images with Url, frontend expects images with string URLs)
        const transformedVehicle = { ...vehicle }
        if (vehicle.Images && Array.isArray(vehicle.Images)) {
          transformedVehicle.images = vehicle.Images.map(img => img.url || img.Url || img)
        } else if (vehicle.images && Array.isArray(vehicle.images)) {
          // Already in correct format
          transformedVehicle.images = vehicle.images.map(img => typeof img === 'string' ? img : (img.url || img.Url || img))
        }
        return transformedVehicle
      })
      
      return {
        vehicles: vehicles,
        pagination: {
          page: response.page || response.Page || 1,
          limit: response.pageSize || response.PageSize || 10,
          total: response.totalCount || response.TotalCount || 0,
          totalPages: response.totalPages || response.TotalPages || 0
        }
      }
    } catch (error) {
      console.error('Failed to load vehicles:', error)
      throw error
    }
  },

  // Get single vehicle by ID
  getVehicleById: async (id) => {
    try {
      const response = await api.get(`/vehicles/${id}`)
      // Transform images array if needed (backend uses Images with Url, frontend expects images with string URLs)
      const transformedVehicle = { ...response }
      if (response.Images && Array.isArray(response.Images)) {
        transformedVehicle.images = response.Images.map(img => img.url || img.Url || img)
      } else if (response.images && Array.isArray(response.images)) {
        // Already in correct format, but ensure it's strings
        transformedVehicle.images = response.images.map(img => typeof img === 'string' ? img : (img.url || img.Url || img))
      }
      return transformedVehicle
    } catch (error) {
      console.error(`Failed to load vehicle ${id}:`, error)
      throw error
    }
  },

  // Create new vehicle
  createVehicle: async (vehicleData) => {
    try {
      // Backend expects form-data ([FromForm]) so send multipart/form-data
      const form = new FormData()
      // Append primitive fields
      if (vehicleData.model !== undefined) form.append('Model', vehicleData.model)
      if (vehicleData.type !== undefined) form.append('Type', vehicleData.type)
      if (vehicleData.price !== undefined) form.append('Price', vehicleData.price)
      if (vehicleData.batteryCapacity !== undefined) form.append('BatteryCapacity', vehicleData.batteryCapacity)
      if (vehicleData.range !== undefined) form.append('Range', vehicleData.range)
      if (vehicleData.stockQuantity !== undefined) form.append('StockQuantity', vehicleData.stockQuantity)
      if (vehicleData.description !== undefined) form.append('Description', vehicleData.description)
      if (vehicleData.dealerId !== undefined) form.append('DealerId', vehicleData.dealerId)

      // Append images if provided as files or URLs
      if (vehicleData.images && Array.isArray(vehicleData.images)) {
        vehicleData.images.forEach((img, idx) => {
          // if File or Blob
          if (img instanceof File || img instanceof Blob) {
            form.append('imageFiles', img)
          } else if (typeof img === 'string') {
            // Append as Images[0].Url style if backend expects structured fields
            form.append('Images[' + idx + '].Url', img)
          }
        })
      }

      const response = await api.post('/vehicles', form, { headers: { 'Content-Type': 'multipart/form-data' } })
      console.log('Vehicle created response:', response)
      if (response.images && response.images.length > 0) {
        console.log('First image URL:', response.images[0].url || response.images[0].Url)
      }
      return response
    } catch (error) {
      console.error('Failed to create vehicle:', error)
      throw error
    }
  },

  // Update existing vehicle
  updateVehicle: async (id, vehicleData) => {
    try {
      // Send multipart/form-data to match backend [FromForm]
      const form = new FormData()
      if (vehicleData.model !== undefined) form.append('Model', vehicleData.model)
      if (vehicleData.type !== undefined) form.append('Type', vehicleData.type)
      if (vehicleData.price !== undefined) form.append('Price', vehicleData.price)
      if (vehicleData.batteryCapacity !== undefined) form.append('BatteryCapacity', vehicleData.batteryCapacity)
      if (vehicleData.range !== undefined) form.append('Range', vehicleData.range)
      if (vehicleData.stockQuantity !== undefined) form.append('StockQuantity', vehicleData.stockQuantity)
      if (vehicleData.description !== undefined) form.append('Description', vehicleData.description)
      if (vehicleData.dealerId !== undefined) form.append('DealerId', vehicleData.dealerId)

      if (vehicleData.images && Array.isArray(vehicleData.images)) {
        vehicleData.images.forEach((img, idx) => {
          if (img instanceof File || img instanceof Blob) {
            form.append('imageFiles', img)
          } else if (typeof img === 'string') {
            form.append('Images[' + idx + '].Url', img)
          }
        })
      }

      const response = await api.put(`/vehicles/${id}`, form, { headers: { 'Content-Type': 'multipart/form-data' } })
      return response
    } catch (error) {
      console.error(`Failed to update vehicle ${id}:`, error)
      throw error
    }
  },

  // Delete vehicle
  deleteVehicle: async (id) => {
    try {
      const response = await api.delete(`/vehicles/${id}`)
      return response
    } catch (error) {
      console.error(`Failed to delete vehicle ${id}:`, error)
      throw error
    }
  },

  // Get vehicle types
  getVehicleTypes: async () => {
    try {
      const response = await api.get('/vehicletypes')
      // Transform backend response to frontend format
      // Backend returns array of VehicleType objects, frontend expects { value, label }
      if (Array.isArray(response)) {
        return response.map(type => ({
          value: type.name || type.Name || type.value || type.Value,
          label: type.name || type.Name || type.label || type.Label
        }))
      }
      return response
    } catch (error) {
      console.error('Failed to load vehicle types:', error)
      throw error
    }
  },

  // Get dealers
  getDealers: async () => {
    try {
      const response = await api.get('/dealers')
      return response
    } catch (error) {
      console.error('Failed to load dealers:', error)
      throw error
    }
  },

  // Reserve vehicle
  reserveVehicle: async (vehicleId, reservationData) => {
    try {
      const response = await api.post(`/vehicles/${vehicleId}/reserve`, {
        customerName: reservationData.customerName,
        customerEmail: reservationData.customerEmail,
        customerPhone: reservationData.customerPhone,
        colorVariantId: reservationData.colorVariantId,
        notes: reservationData.notes,
        quantity: reservationData.quantity || 1,
        deviceToken: reservationData.deviceToken || null
      })
      return response
    } catch (error) {
      console.error(`Failed to reserve vehicle ${vehicleId}:`, error)
      throw error
    }
  }
}

export default vehicleService
