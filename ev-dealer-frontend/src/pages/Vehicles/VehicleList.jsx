/**
 * Vehicle List Page - Modern Card Layout
 * Features: Search, Filter, Pagination, CRUD actions
 */

import { useState, useEffect } from 'react'
import { useNavigate } from 'react-router-dom'
import {
  Box,
  Typography,
  Button,
  Chip,
  Stack,
  Alert,
  CircularProgress,
  Dialog,
  DialogTitle,
  DialogContent,
  DialogActions,
  DialogContentText,
  Container,
  TextField,
  InputAdornment,
  FormControl,
  InputLabel,
  Select,
  MenuItem,
  Card,
  CardContent,
  CardMedia,
  CardActions,
  IconButton,
  Paper
} from '@mui/material'
import {
  Add as AddIcon,
  Search as SearchIcon,
  Edit as EditIcon,
  Delete as DeleteIcon,
  Visibility as ViewIcon,
  Refresh as RefreshIcon,
  FavoriteBorder as FavoriteBorderIcon,
  LocalGasStation as BatteryIcon,
  Speed as SpeedIcon,
  LocationOn as LocationIcon
} from '@mui/icons-material'

import vehicleService from '../../services/vehicleService'
import resolveImagePath, { placeholderVehicleImage } from '../../utils/imageUtils'

const VehicleList = () => {
  const navigate = useNavigate()

  // State management
  const [vehicles, setVehicles] = useState([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState(null)
  const [pagination, setPagination] = useState({
    page: 1,
    limit: 12,
    total: 0,
    totalPages: 0
  })

  // Filters and search
  const [searchTerm, setSearchTerm] = useState('')
  const [filters, setFilters] = useState({
    type: '',
    dealerId: '',
    minPrice: '',
    maxPrice: ''
  })

  // Dialog states
  const [deleteDialogOpen, setDeleteDialogOpen] = useState(false)
  const [vehicleToDelete, setVehicleToDelete] = useState(null)
  const [deleting, setDeleting] = useState(false)

  // Options for filters
  const [vehicleTypes, setVehicleTypes] = useState([])
  const [dealers, setDealers] = useState([])

  // Load initial data
  useEffect(() => {
    loadVehicles()
    loadFilterOptions()
  }, [])

  // Reload when filters change
  useEffect(() => {
    loadVehicles()
  }, [pagination.page, pagination.limit, searchTerm, filters])

  const loadVehicles = async () => {
    try {
      setLoading(true)
      setError(null)

      const params = {
        page: pagination.page,
        limit: pagination.limit,
        search: searchTerm,
        ...filters
      }

      // Remove empty filters
      Object.keys(params).forEach(key => {
        if (params[key] === '' || params[key] === 'all') {
          delete params[key]
        }
      })

      const response = await vehicleService.getVehicles(params)
      setVehicles(response.vehicles)
      setPagination(prev => ({
        ...prev,
        total: response.pagination.total,
        totalPages: response.pagination.totalPages
      }))
    } catch (err) {
      setError(err.message || 'Failed to load vehicles')
      console.error('Error loading vehicles:', err)
    } finally {
      setLoading(false)
    }
  }

  const loadFilterOptions = async () => {
    try {
      const [typesResponse, dealersResponse] = await Promise.all([
        vehicleService.getVehicleTypes(),
        vehicleService.getDealers()
      ])
      setVehicleTypes(typesResponse)
      setDealers(dealersResponse)
    } catch (err) {
      console.error('Error loading filter options:', err)
    }
  }

  const handleSearch = (event) => {
    setSearchTerm(event.target.value)
    setPagination(prev => ({ ...prev, page: 1 })) // Reset to first page
  }

  const handleFilterChange = (field, value) => {
    setFilters(prev => ({ ...prev, [field]: value }))
    setPagination(prev => ({ ...prev, page: 1 })) // Reset to first page
  }

  const handleView = (vehicle) => {
    navigate(`/vehicles/${vehicle.id}`)
  }

  const handleEdit = (vehicle) => {
    navigate(`/vehicles/${vehicle.id}/edit`)
  }

  const handleDelete = (vehicle) => {
    setVehicleToDelete(vehicle)
    setDeleteDialogOpen(true)
  }

  const confirmDelete = async () => {
    if (!vehicleToDelete) return

    try {
      setDeleting(true)
      setError(null) // Clear any previous errors
      await vehicleService.deleteVehicle(vehicleToDelete.id)
      setDeleteDialogOpen(false)
      setVehicleToDelete(null)
      // Reload the list
      await loadVehicles()
    } catch (err) {
      console.error('Error deleting vehicle:', err)
      console.log('Error response:', err.response)
      console.log('Error response data:', err.response?.data)
      
      // Try multiple ways to extract error message
      const errorMessage = 
        err.response?.data?.message || 
        err.response?.data?.Message || 
        err.message || 
        'Không thể xóa xe'
      
      setError(errorMessage)
      setDeleteDialogOpen(false) // Close dialog even on error so user can see error message
    } finally {
      setDeleting(false)
    }
  }

  const handleAddNew = () => {
    navigate('/vehicles/new')
  }

  const handleRefresh = () => {
    loadVehicles()
  }

  const handleLoadMore = () => {
    if (pagination.page < pagination.totalPages) {
      setPagination(prev => ({ ...prev, page: prev.page + 1 }))
    }
  }

  if (loading && vehicles.length === 0) {
    return (
      <Container maxWidth="xl" sx={{ py: 4 }}>
        <Box sx={{ display: 'flex', justifyContent: 'center', alignItems: 'center', height: '50vh' }}>
          <CircularProgress size={60} />
          <Typography variant="h6" sx={{ ml: 2 }}>
            Đang tải xe...
          </Typography>
        </Box>
      </Container>
    )
  }

  return (
    <Container maxWidth="xl" sx={{ py: 4 }}>
      {/* Header */}
      <Box sx={{ mb: 4 }}>
        <Stack direction="row" justifyContent="space-between" alignItems="center" sx={{ mb: 3 }}>
          <Box>
            <Typography variant="h4" component="h1" sx={{ fontWeight: 'bold', mb: 1 }}>
              🚗 Xe Điện
            </Typography>
            <Typography variant="body1" color="text.secondary">
              Khám phá tương lai của giao thông bền vững
            </Typography>
          </Box>
          <Stack direction="row" spacing={2}>
            <Button
              variant="outlined"
              startIcon={<RefreshIcon />}
              onClick={handleRefresh}
              disabled={loading}
            >
              Làm mới
            </Button>
            <Button
              variant="contained"
              startIcon={<AddIcon />}
              onClick={handleAddNew}
              size="large"
            >
              Thêm Xe Mới
            </Button>
          </Stack>
        </Stack>

        {/* Error Alert */}
        {error && (
          <Alert severity="error" sx={{ mb: 3, borderRadius: 2 }}>
            {error}
          </Alert>
        )}

        {/* Filters and Search */}
        <Paper sx={{ borderRadius: 3, p: 3, mb: 4, boxShadow: '0 4px 20px rgba(0,0,0,0.08)' }}>
          <Stack direction={{ xs: 'column', md: 'row' }} spacing={3} alignItems="center">
            <TextField
              fullWidth
              placeholder="Tìm kiếm xe..."
              value={searchTerm}
              onChange={handleSearch}
              InputProps={{
                startAdornment: (
                  <InputAdornment position="start">
                    <SearchIcon />
                  </InputAdornment>
                ),
              }}
              size="small"
              sx={{ '& .MuiOutlinedInput-root': { borderRadius: 2 } }}
            />

            <FormControl size="small" sx={{ minWidth: 120 }}>
              <InputLabel>Loại xe</InputLabel>
              <Select
                value={filters.type}
                label="Loại xe"
                onChange={(e) => handleFilterChange('type', e.target.value)}
                sx={{ borderRadius: 2 }}
              >
                <MenuItem value="">Tất cả loại</MenuItem>
                {vehicleTypes.map((type) => (
                  <MenuItem key={type.value} value={type.value}>
                    {type.label}
                  </MenuItem>
                ))}
              </Select>
            </FormControl>

            <FormControl size="small" sx={{ minWidth: 120 }}>
              <InputLabel>Đại lý</InputLabel>
              <Select
                value={filters.dealerId}
                label="Đại lý"
                onChange={(e) => handleFilterChange('dealerId', e.target.value)}
                sx={{ borderRadius: 2 }}
              >
                <MenuItem value="">Tất cả đại lý</MenuItem>
                {dealers.map((dealer) => (
                  <MenuItem key={dealer.id} value={dealer.id}>
                    {dealer.name}
                  </MenuItem>
                ))}
              </Select>
            </FormControl>

            <TextField
              label="Giá tối thiểu"
              type="number"
              value={filters.minPrice}
              onChange={(e) => handleFilterChange('minPrice', e.target.value)}
              size="small"
              InputProps={{
                startAdornment: <InputAdornment position="start">$</InputAdornment>,
              }}
              sx={{ '& .MuiOutlinedInput-root': { borderRadius: 2 } }}
            />

            <TextField
              label="Giá tối đa"
              type="number"
              value={filters.maxPrice}
              onChange={(e) => handleFilterChange('maxPrice', e.target.value)}
              size="small"
              InputProps={{
                startAdornment: <InputAdornment position="start">$</InputAdornment>,
              }}
              sx={{ '& .MuiOutlinedInput-root': { borderRadius: 2 } }}
            />
          </Stack>
        </Paper>
      </Box>

      {/* Vehicles Grid */}
      <Box
        sx={{
          display: 'grid',
          gridTemplateColumns: {
            xs: '1fr',
            sm: 'repeat(2, 1fr)',
            md: 'repeat(3, 1fr)'
          },
          gap: 3,
          mb: 4
        }}
      >
        {vehicles.map((vehicle) => (
          <Card
            key={vehicle.id}
            sx={{
              height: '100%',
              display: 'flex',
              flexDirection: 'column',
              borderRadius: 4,
              overflow: 'hidden',
              boxShadow: '0 8px 32px rgba(0,0,0,0.12)',
              background: 'linear-gradient(135deg, #ffffff 0%, #f8fafc 100%)',
              border: '1px solid rgba(255,255,255,0.2)',
              transition: 'all 0.4s cubic-bezier(0.4, 0, 0.2, 1)',
              '&:hover': {
                transform: 'translateY(-8px) scale(1.02)',
                boxShadow: '0 20px 60px rgba(0,0,0,0.2)',
                border: '1px solid rgba(25, 118, 210, 0.3)'
              }
            }}
          >
            {/* Image */}
            <Box sx={{ position: 'relative', overflow: 'hidden' }}>
              <CardMedia
                component="img"
                height="240"
                image={vehicle.images?.[0] ? resolveImagePath(vehicle.images[0]) : placeholderVehicleImage(vehicle.model, 640, 480)}
                alt={vehicle.model}
                sx={{
                  objectFit: 'cover',
                  transition: 'transform 0.5s ease',
                  '&:hover': {
                    transform: 'scale(1.1)'
                  }
                }}
              />
              <Box
                sx={{
                  position: 'absolute',
                  top: 12,
                  right: 12,
                  display: 'flex',
                  gap: 1
                }}
              >
                <IconButton
                  size="small"
                  sx={{
                    bgcolor: 'rgba(255,255,255,0.95)',
                    backdropFilter: 'blur(10px)',
                    border: '1px solid rgba(255,255,255,0.2)',
                    '&:hover': {
                      bgcolor: 'white',
                      transform: 'scale(1.1)'
                    },
                    transition: 'all 0.2s ease'
                  }}
                >
                  <FavoriteBorderIcon fontSize="small" />
                </IconButton>
              </Box>
              <Chip
                label={vehicle.type === 'sedan' ? 'Sedan' : vehicle.type === 'suv' ? 'SUV' : vehicle.type === 'hatchback' ? 'Hatchback' : vehicle.type.charAt(0).toUpperCase() + vehicle.type.slice(1)}
                size="small"
                color="primary"
                sx={{
                  position: 'absolute',
                  top: 12,
                  left: 12,
                  bgcolor: 'rgba(18, 180, 221, 0.95)',
                  backdropFilter: 'blur(10px)',
                  border: '1px solid rgba(255,255,255,0.2)',
                  fontWeight: 'bold',
                  boxShadow: '0 2px 8px rgba(0,0,0,0.1)'
                }}
              />
            </Box>

            <CardContent sx={{ flexGrow: 1, p: 3 }}>
              <Typography
                variant="h6"
                component="h2"
                sx={{
                  fontWeight: 'bold',
                  mb: 1.5,
                  overflow: 'hidden',
                  textOverflow: 'ellipsis',
                  whiteSpace: 'nowrap',
                  fontSize: '1.1rem'
                }}
              >
                {vehicle.model}
              </Typography>

              <Typography
                variant="body2"
                color="text.secondary"
                sx={{
                  mb: 2.5,
                  height: 42,
                  overflow: 'hidden',
                  lineHeight: 1.4,
                  display: '-webkit-box',
                  WebkitLineClamp: 2,
                  WebkitBoxOrient: 'vertical'
                }}
              >
                {vehicle.description}
              </Typography>

              <Stack direction="row" spacing={1.5} sx={{ mb: 2.5 }}>
                <Chip
                  icon={<BatteryIcon sx={{ fontSize: '1rem !important' }} />}
                  label={`${vehicle.batteryCapacity} kWh`}
                  size="small"
                  variant="outlined"
                  sx={{
                    fontSize: '0.7rem',
                    height: 28,
                    borderRadius: 2,
                    '& .MuiChip-icon': { marginLeft: '6px' }
                  }}
                />
                <Chip
                  icon={<SpeedIcon sx={{ fontSize: '1rem !important' }} />}
                  label={`${vehicle.range} km`}
                  size="small"
                  variant="outlined"
                  sx={{
                    fontSize: '0.7rem',
                    height: 28,
                    borderRadius: 2,
                    '& .MuiChip-icon': { marginLeft: '6px' }
                  }}
                />
              </Stack>

              <Stack direction="row" alignItems="center" spacing={1} sx={{ mb: 2 }}>
                <LocationIcon fontSize="small" color="action" />
                <Typography variant="body2" color="text.secondary" sx={{ fontSize: '0.8rem' }}>
                  {vehicle.dealerName}
                </Typography>
              </Stack>

              <Stack direction="row" justifyContent="space-between" alignItems="center">
                <Typography
                  variant="h6"
                  sx={{
                    fontWeight: 'bold',
                    color: 'primary.main',
                    fontSize: '1.2rem'
                  }}
                >
                  {vehicle.price?.toLocaleString('vi-VN')} ₫
                </Typography>
                <Chip
                  label={`${vehicle.stockQuantity} xe có sẵn`}
                  size="small"
                  color={vehicle.stockQuantity > 5 ? 'success' : vehicle.stockQuantity > 0 ? 'warning' : 'error'}
                  variant="outlined"
                  sx={{
                    fontSize: '0.7rem',
                    height: 26,
                    borderRadius: 2
                  }}
                />
              </Stack>
            </CardContent>

            <CardActions sx={{ p: 3, pt: 0, gap: 1 }}>
              <Button
                size="small"
                variant="outlined"
                startIcon={<ViewIcon />}
                onClick={() => handleView(vehicle)}
                sx={{
                  flex: 1,
                  borderRadius: 2,
                  textTransform: 'none',
                  fontWeight: 600,
                  border: '1px solid rgba(25, 118, 210, 0.5)',
                  '&:hover': {
                    border: '1px solid #1976d2',
                    bgcolor: 'rgba(25, 118, 210, 0.04)'
                  }
                }}
              >
                Xem
              </Button>
              <Button
                size="small"
                variant="outlined"
                startIcon={<EditIcon />}
                onClick={() => handleEdit(vehicle)}
                sx={{
                  flex: 1,
                  borderRadius: 2,
                  textTransform: 'none',
                  fontWeight: 600,
                  border: '1px solid rgba(46, 125, 50, 0.5)',
                  '&:hover': {
                    border: '1px solid #2e7d32',
                    bgcolor: 'rgba(46, 125, 50, 0.04)'
                  }
                }}
              >
                Sửa
              </Button>
              <IconButton
                size="small"
                color="error"
                onClick={() => handleDelete(vehicle)}
                sx={{
                  borderRadius: 2,
                  border: '1px solid rgba(211, 47, 47, 0.5)',
                  '&:hover': {
                    bgcolor: 'rgba(211, 47, 47, 0.04)',
                    border: '1px solid #d32f2f'
                  }
                }}
              >
                <DeleteIcon fontSize="small" />
              </IconButton>
            </CardActions>
          </Card>
        ))}
      </Box>

      {/* Load More Button */}
      {pagination.page < pagination.totalPages && (
        <Box sx={{ display: 'flex', justifyContent: 'center', mt: 6 }}>
          <Button
            variant="outlined"
            size="large"
            onClick={handleLoadMore}
            disabled={loading}
            sx={{
              borderRadius: 3,
              px: 6,
              py: 2,
              textTransform: 'none',
              fontWeight: 600,
              fontSize: '1rem',
              border: '2px solid',
              '&:hover': {
                border: '2px solid',
                transform: 'translateY(-2px)',
                boxShadow: '0 8px 25px rgba(0,0,0,0.15)'
              },
              transition: 'all 0.3s ease'
            }}
          >
            {loading ? <CircularProgress size={24} /> : 'Tải thêm xe'}
          </Button>
        </Box>
      )}

      {/* No Results */}
      {!loading && vehicles.length === 0 && (
        <Box sx={{ textAlign: 'center', py: 10 }}>
          <Typography variant="h6" color="text.secondary" sx={{ mb: 2 }}>
            Không tìm thấy xe nào
          </Typography>
          <Typography variant="body2" color="text.secondary" sx={{ mb: 4 }}>
            Thử điều chỉnh bộ lọc hoặc thêm xe mới
          </Typography>
          <Button
            variant="contained"
            startIcon={<AddIcon />}
            onClick={handleAddNew}
            size="large"
            sx={{
              borderRadius: 3,
              px: 4,
              py: 1.5,
              textTransform: 'none',
              fontWeight: 600
            }}
          >
            Thêm Xe Mới
          </Button>
        </Box>
      )}

      {/* Delete Confirmation Dialog */}
      <Dialog
        open={deleteDialogOpen}
        onClose={() => setDeleteDialogOpen(false)}
        PaperProps={{
          sx: { borderRadius: 3 }
        }}
      >
        <DialogTitle sx={{
          fontWeight: 'bold',
          borderBottom: 1,
          borderColor: 'divider',
          pb: 2
        }}>
          🗑️ Xóa Xe
        </DialogTitle>
        <DialogContent sx={{ pt: 3 }}>
          <DialogContentText sx={{ fontSize: '1.1rem' }}>
            Bạn có chắc chắn muốn xóa <strong>"{vehicleToDelete?.model}"</strong>?
            Hành động này không thể hoàn tác và tất cả dữ liệu xe sẽ bị mất vĩnh viễn.
          </DialogContentText>
        </DialogContent>
        <DialogActions sx={{ p: 3, gap: 1 }}>
          <Button
            onClick={() => setDeleteDialogOpen(false)}
            variant="outlined"
            sx={{
              borderRadius: 2,
              textTransform: 'none',
              fontWeight: 600,
              px: 3
            }}
          >
            Hủy
          </Button>
          <Button
            onClick={confirmDelete}
            color="error"
            variant="contained"
            disabled={deleting}
            startIcon={deleting ? <CircularProgress size={20} /> : <DeleteIcon />}
            sx={{
              borderRadius: 2,
              textTransform: 'none',
              fontWeight: 600,
              px: 3
            }}
          >
            {deleting ? 'Đang xóa...' : 'Xóa Xe'}
          </Button>
        </DialogActions>
      </Dialog>
    </Container>
  )
}

export default VehicleList
