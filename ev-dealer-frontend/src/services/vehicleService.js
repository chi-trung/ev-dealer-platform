import api from './api'
import { mockVehicles, mockDealers } from '../data/mockVehicles'

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
      console.warn('API call failed, using mock data:', error.message)
      await new Promise(resolve => setTimeout(resolve, 500))
      return [...mockVehicles]
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
      // Fallback to mock data if API fails
      console.warn('API call failed, using mock data:', error.message)

      // Simulate API delay
      await new Promise(resolve => setTimeout(resolve, 500))

      let vehicles = [...mockVehicles]

      // Apply filters
      if (params.search) {
        const searchTerm = params.search.toLowerCase()
        vehicles = vehicles.filter(vehicle =>
          vehicle.model.toLowerCase().includes(searchTerm) ||
          vehicle.type.toLowerCase().includes(searchTerm) ||
          vehicle.dealerName.toLowerCase().includes(searchTerm)
        )
      }

      if (params.type && params.type !== 'all') {
        vehicles = vehicles.filter(vehicle => vehicle.type === params.type)
      }

      if (params.dealerId) {
        vehicles = vehicles.filter(vehicle => vehicle.dealerId === params.dealerId)
      }

      if (params.minPrice) {
        vehicles = vehicles.filter(vehicle => vehicle.price >= parseInt(params.minPrice))
      }

      if (params.maxPrice) {
        vehicles = vehicles.filter(vehicle => vehicle.price <= parseInt(params.maxPrice))
      }

      // Apply pagination
      const page = parseInt(params.page) || 1
      const limit = parseInt(params.limit) || 10
      const offset = (page - 1) * limit
      const total = vehicles.length
      const paginatedVehicles = vehicles.slice(offset, offset + limit)

      return {
        vehicles: paginatedVehicles,
        pagination: {
          page,
          limit,
          total,
          totalPages: Math.ceil(total / limit)
        }
      }
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
      // Fallback to mock data if API fails
      console.warn('API call failed, using mock data:', error.message)

      // Simulate API delay
      await new Promise(resolve => setTimeout(resolve, 300))

      const vehicle = mockVehicles.find(v => v.id === parseInt(id))
      if (!vehicle) {
        throw new Error('Vehicle not found')
      }

      return vehicle
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
      // If error has response, it means backend responded with an error (not network issue)
      if (error.response) {
        throw error
      }
      
      // Only fallback to mock implementation if it's a network error
      console.warn('API call failed (network error), using mock implementation:', error.message)

      // Simulate API delay
      await new Promise(resolve => setTimeout(resolve, 800))

      // Generate new ID
      const newId = Math.max(...mockVehicles.map(v => v.id)) + 1

      const newVehicle = {
        id: newId,
        ...vehicleData,
        createdAt: new Date().toISOString(),
        updatedAt: new Date().toISOString()
      }

      // In real app, this would be handled by the backend
      console.log('Vehicle created:', newVehicle)

      return newVehicle
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
      // If error has response, it means backend responded with an error (not network issue)
      if (error.response) {
        throw error
      }
      
      // Only fallback to mock implementation if it's a network error
      console.warn('API call failed (network error), using mock implementation:', error.message)

      // Simulate API delay
      await new Promise(resolve => setTimeout(resolve, 800))

      const existingVehicle = mockVehicles.find(v => v.id === parseInt(id))
      if (!existingVehicle) {
        throw new Error('Vehicle not found')
      }

      const updatedVehicle = {
        ...existingVehicle,
        ...vehicleData,
        updatedAt: new Date().toISOString()
      }

      // In real app, this would be handled by the backend
      console.log('Vehicle updated:', updatedVehicle)

      return updatedVehicle
    }
  },

  // Delete vehicle
  deleteVehicle: async (id) => {
    try {
      const response = await api.delete(`/vehicles/${id}`)
      return response
    } catch (error) {
      // If error has response, it means backend responded with an error (not network issue)
      // In this case, throw the error instead of falling back to mock
      if (error.response) {
        throw error
      }

      // Only fallback to mock implementation if it's a network error
      console.warn('API call failed (network error), using mock implementation:', error.message)

      // Simulate API delay
      await new Promise(resolve => setTimeout(resolve, 1200))

      const vehicle = mockVehicles.find(v => v.id === parseInt(id))
      if (!vehicle) {
        throw new Error('Vehicle not found')
      }

      // In real app, this would be handled by the backend
      console.log('Vehicle deleted:', id)

      return { success: true, message: 'Vehicle deleted successfully' }
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
      // Fallback to mock types if API fails
      console.warn('API call failed, using mock data:', error.message)

      return [
        { value: 'sedan', label: 'Sedan' },
        { value: 'suv', label: 'SUV' },
        { value: 'hatchback', label: 'Hatchback' },
        { value: 'coupe', label: 'Coupe' },
        { value: 'convertible', label: 'Convertible' },
        { value: 'truck', label: 'Truck' }
      ]
    }
  },

  // Get dealers
  getDealers: async () => {
    try {
      const response = await api.get('/dealers')
      return response
    } catch (error) {
      // Fallback to mock dealers if API fails
      console.warn('API call failed, using mock data:', error.message)

      return mockDealers
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
      // Fallback to mock implementation if API fails
      console.warn('API call failed, using mock implementation:', error.message)

      // Simulate API delay
      await new Promise(resolve => setTimeout(resolve, 1000))

      // Mock successful reservation
      const mockReservation = {
        id: Math.floor(Math.random() * 1000) + 1,
        vehicleId: parseInt(vehicleId),
        vehicleName: `Mock Vehicle ${vehicleId}`,
        colorVariantId: reservationData.colorVariantId,
        colorVariantName: reservationData.colorVariantId ? 'Selected Color' : null,
        customerName: reservationData.customerName,
        customerEmail: reservationData.customerEmail,
        customerPhone: reservationData.customerPhone,
        notes: reservationData.notes,
        quantity: reservationData.quantity || 1,
        totalPrice: 50000, // Mock price
        status: 'Pending',
        createdAt: new Date().toISOString(),
        updatedAt: new Date().toISOString(),
        expiresAt: new Date(Date.now() + 48 * 60 * 60 * 1000).toISOString() // 48 hours from now
      }

      console.log('Mock reservation created:', mockReservation)
      return mockReservation
    }
  },

  // Get vehicle statistics - total stock count
  getVehicleStatistics: async () => {
    try {
      const response = await api.get('/vehicles', { params: { PageSize: 1000 } })
      
      // Handle different response formats - same as getVehicles
      let vehicles = []
      if (response.items && Array.isArray(response.items)) {
        vehicles = response.items
      } else if (response.Items && Array.isArray(response.Items)) {
        vehicles = response.Items
      } else if (Array.isArray(response)) {
        vehicles = response
      }
      
      // Calculate total stock - check both camelCase and PascalCase
      const totalStock = vehicles.reduce((sum, v) => {
        const stock = v.stockQuantity || v.StockQuantity || 0
        return sum + (typeof stock === 'number' ? stock : 0)
      }, 0)
      
      console.log('Vehicle statistics:', { totalStock, vehicleCount: vehicles.length })
      return { totalStock }
    } catch (error) {
      console.warn('Error fetching vehicle statistics:', error)
      return { totalStock: 0 }
    }
  }
}

export default vehicleService
