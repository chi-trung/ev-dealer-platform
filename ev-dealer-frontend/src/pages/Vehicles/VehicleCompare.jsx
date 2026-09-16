/**
 * Vehicle Compare Page - So sánh xe điện
 * Features: Compare up to 3 vehicles side by side
 */

import { useState, useEffect } from 'react'
import { useNavigate, useSearchParams } from 'react-router-dom'
import {
  Box,
  Typography,
  Button,
  Container,
  Paper,
  Grid,
  Stack,
  IconButton,
  Chip,
  Divider,
  Alert,
  CircularProgress,
  Avatar,
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TableRow,
  Card,
  CardMedia,
  CardContent,
  Autocomplete,
  TextField
} from '@mui/material'
import {
  ArrowBack as ArrowBackIcon,
  Close as CloseIcon,
  Add as AddIcon,
  Check as CheckIcon
} from '@mui/icons-material'

import vehicleService from '../../services/vehicleService'
import resolveImagePath, { placeholderVehicleImage } from '../../utils/imageUtils'

const VehicleCompare = () => {
  const navigate = useNavigate()
  const [searchParams] = useSearchParams()
  
  const [vehicles, setVehicles] = useState([])
  const [compareVehicles, setCompareVehicles] = useState([])
  const [loading, setLoading] = useState(true)
  const [allVehicles, setAllVehicles] = useState([])

  useEffect(() => {
    loadVehicles()
    
    // Load vehicles from URL params
    const ids = searchParams.get('ids')
    if (ids) {
      const vehicleIds = ids.split(',').map(id => parseInt(id))
      loadCompareVehicles(vehicleIds)
    }
  }, [])

  const loadVehicles = async () => {
    try {
      const response = await vehicleService.getAllVehicles()
      setAllVehicles(Array.isArray(response) ? response : [])
    } catch (error) {
      console.error('Error loading vehicles:', error)
    }
  }

  const loadCompareVehicles = async (ids) => {
    try {
      setLoading(true)
      const promises = ids.map(id => vehicleService.getVehicleById(id))
      const results = await Promise.all(promises)
      setCompareVehicles(results)
    } catch (error) {
      console.error('Error loading compare vehicles:', error)
    } finally {
      setLoading(false)
    }
  }

  const handleAddVehicle = (vehicle) => {
    if (compareVehicles.length >= 3) {
      alert('Chỉ có thể so sánh tối đa 3 xe!')
      return
    }
    if (compareVehicles.find(v => v.id === vehicle.id)) {
      alert('Xe này đã được thêm vào danh sách so sánh!')
      return
    }
    setCompareVehicles([...compareVehicles, vehicle])
  }

  const handleRemoveVehicle = (vehicleId) => {
    setCompareVehicles(compareVehicles.filter(v => v.id !== vehicleId))
  }

  // Helper function để lấy mock specs theo loại xe với random dựa trên ID
  const getMockSpecsByType = (type, vehicleId = 1) => {
    const seed = vehicleId || 1
    
    const randomInRange = (min, max, offset = 0) => {
      const val = min + ((seed + offset) % (max - min + 1))
      return val
    }
    
    const ranges = {
      sedan: {
        acceleration: { min: 4.5, max: 6.5 },
        topSpeed: { min: 190, max: 230 },
        charging: { min: 25, max: 35 },
        seats: [5],
        cargo: { min: 420, max: 520 },
        warranty: ['3 năm / 60,000 km', '4 năm / 80,000 km', '5 năm / 100,000 km']
      },
      suv: {
        acceleration: { min: 5.5, max: 7.5 },
        topSpeed: { min: 170, max: 200 },
        charging: { min: 30, max: 45 },
        seats: [5, 7],
        cargo: { min: 580, max: 720 },
        warranty: ['4 năm / 100,000 km', '5 năm / 120,000 km', '6 năm / 150,000 km']
      },
      hatchback: {
        acceleration: { min: 6.5, max: 8.5 },
        topSpeed: { min: 150, max: 180 },
        charging: { min: 20, max: 30 },
        seats: [5],
        cargo: { min: 320, max: 420 },
        warranty: ['3 năm / 50,000 km', '3 năm / 60,000 km', '4 năm / 80,000 km']
      }
    }
    
    const range = ranges[type?.toLowerCase()] || ranges.sedan
    
    const accel = (randomInRange(range.acceleration.min * 10, range.acceleration.max * 10, 1) / 10).toFixed(1)
    const topSpeed = randomInRange(range.topSpeed.min, range.topSpeed.max, 2)
    const charging = randomInRange(range.charging.min, range.charging.max, 3)
    const seats = range.seats[seed % range.seats.length]
    const cargo = randomInRange(range.cargo.min, range.cargo.max, 4)
    const warranty = range.warranty[seed % range.warranty.length]
    
    return {
      acceleration: `${accel} giây`,
      topSpeed: `${topSpeed} km/h`,
      charging: `${charging} phút (80%)`,
      seats: `${seats} chỗ`,
      cargo: `${cargo} lít`,
      warranty: warranty
    }
  }

  // Helper để lấy giá trị spec với fallback
  const getSpecValue = (vehicle, field, mockField) => {
    // Kiểm tra xem có specifications không
    if (vehicle.specifications?.[field] && vehicle.specifications[field] !== 'N/A' && vehicle.specifications[field] !== '') {
      return vehicle.specifications[field]
    }
    // Fallback sang mock data với vehicle ID
    const mockSpecs = getMockSpecsByType(vehicle.type, vehicle.id)
    return mockSpecs[mockField] || 'Đang cập nhật'
  }

  const getComparisonData = () => {
    return [
      {
        category: 'Giá cả',
        specs: [
          {
            label: 'Giá bán',
            values: compareVehicles.map(v => `${(v.price / 1000000).toFixed(1)} triệu VND`),
            type: 'price',
            betterWhen: 'lower'
          }
        ]
      },
      {
        category: 'Pin & Động cơ',
        specs: [
          {
            label: 'Dung lượng pin',
            values: compareVehicles.map(v => `${v.batteryCapacity || 80} kWh`),
            type: 'number',
            betterWhen: 'higher'
          },
          {
            label: 'Quãng đường',
            values: compareVehicles.map(v => `${v.range || 456} km`),
            type: 'number',
            betterWhen: 'higher'
          },
          {
            label: 'Thời gian sạc',
            values: compareVehicles.map(v => v.chargingTime || getMockSpecsByType(v.type, v.id).charging),
            type: 'text'
          },
          {
            label: 'Công suất',
            values: compareVehicles.map(v => v.motorPower || '250 kW'),
            type: 'text'
          }
        ]
      },
      {
        category: 'Hiệu suất',
        specs: [
          {
            label: 'Tăng tốc 0-100 km/h',
            values: compareVehicles.map(v => getSpecValue(v, 'acceleration', 'acceleration')),
            type: 'text'
          },
          {
            label: 'Tốc độ tối đa',
            values: compareVehicles.map(v => getSpecValue(v, 'topSpeed', 'topSpeed')),
            type: 'text'
          }
        ]
      },
      {
        category: 'Kích thước',
        specs: [
          {
            label: 'Số chỗ ngồi',
            values: compareVehicles.map(v => getSpecValue(v, 'seats', 'seats')),
            type: 'text'
          },
          {
            label: 'Dung tích cốp',
            values: compareVehicles.map(v => getSpecValue(v, 'cargo', 'cargo')),
            type: 'text'
          },
          {
            label: 'Loại xe',
            values: compareVehicles.map(v => {
              const type = v.type
              return type === 'sedan' ? 'Sedan' : type === 'suv' ? 'SUV' : type === 'hatchback' ? 'Hatchback' : type?.toUpperCase()
            }),
            type: 'text'
          }
        ]
      },
      {
        category: 'Bảo hành',
        specs: [
          {
            label: 'Bảo hành xe',
            values: compareVehicles.map(v => getSpecValue(v, 'warranty', 'warranty')),
            type: 'text'
          }
        ]
      },
      {
        category: 'Tồn kho',
        specs: [
          {
            label: 'Số lượng có sẵn',
            values: compareVehicles.map(v => `${v.stockQuantity} xe`),
            type: 'number',
            betterWhen: 'higher'
          }
        ]
      }
    ]
  }

  const getBestValue = (values, type, betterWhen) => {
    if (type === 'price') {
      const prices = values.map(v => parseFloat(v.replace(/[^0-9.]/g, '')))
      return betterWhen === 'lower' ? Math.min(...prices) : Math.max(...prices)
    }
    if (type === 'number') {
      const numbers = values.map(v => parseFloat(v.replace(/[^0-9]/g, '')))
      return betterWhen === 'higher' ? Math.max(...numbers) : Math.min(...numbers)
    }
    return null
  }

  const isBestValue = (value, values, type, betterWhen) => {
    const best = getBestValue(values, type, betterWhen)
    if (best === null) return false
    const numValue = parseFloat(value.replace(/[^0-9.]/g, ''))
    return numValue === best
  }

  if (loading) {
    return (
      <Container maxWidth="xl" sx={{ py: 4 }}>
        <Box sx={{ display: 'flex', justifyContent: 'center', alignItems: 'center', height: '50vh' }}>
          <CircularProgress size={80} />
        </Box>
      </Container>
    )
  }

  return (
    <Box sx={{ 
      bgcolor: '#f8fbff', 
      minHeight: '100vh',
      py: 4
    }}>
      <Container maxWidth="xl">
        {/* Header */}
        <Box sx={{ mb: 5 }}>
          <Button
            startIcon={<ArrowBackIcon />}
            onClick={() => navigate('/vehicles')}
            sx={{ 
              mb: 3,
              fontSize: '18px',
              padding: '12px 24px',
              color: '#1976d2',
              '&:hover': {
                backgroundColor: 'rgba(25, 118, 210, 0.04)'
              }
            }}
            size="large"
          >
            Quay lại danh sách xe
          </Button>
          
          <Paper
            elevation={0}
            sx={{
              p: 4,
              borderRadius: 3,
              background: 'linear-gradient(135deg, #e3f2fd 0%, #bbdefb 100%)',
              border: '1px solid #90caf9'
            }}
          >
            <Typography variant="h3" sx={{ fontWeight: 700, mb: 2, color: '#1565c0', fontSize: { xs: '2rem', md: '2.5rem' } }}>
              SO SÁNH XE ĐIỆN
            </Typography>
            <Typography variant="h5" sx={{ color: '#1976d2', fontSize: '1.4rem', fontWeight: 500 }}>
              So sánh tối đa 3 mẫu xe để tìm lựa chọn phù hợp nhất
            </Typography>
          </Paper>
        </Box>

        {/* Add Vehicle Section */}
        {compareVehicles.length < 3 && (
          <Paper sx={{ 
            p: 4, 
            mb: 5, 
            borderRadius: 3, 
            background: 'white',
            border: '1px solid #e3f2fd'
          }}>
            <Typography variant="h4" sx={{ fontWeight: 600, mb: 3, color: '#1976d2' }}>
              Thêm xe để so sánh ({3 - compareVehicles.length} slot trống)
            </Typography>
            <Autocomplete
              sx={{
                '& .MuiAutocomplete-inputRoot': {
                  fontSize: '18px',
                  padding: '12px'
                }
              }}
              options={allVehicles.filter(v => !compareVehicles.find(cv => cv.id === v.id))}
              getOptionLabel={(option) => `${option.brand || ''} ${option.model} - ${(option.price / 1000000).toFixed(1)} triệu`}
              onChange={(event, value) => {
                if (value) handleAddVehicle(value)
              }}
              renderInput={(params) => (
                <TextField 
                  {...params} 
                  label="Tìm kiếm xe để thêm vào so sánh..." 
                  variant="outlined"
                  size="medium"
                  sx={{
                    '& .MuiInputLabel-root': {
                      fontSize: '18px',
                      color: '#1976d2'
                    },
                    '& .MuiOutlinedInput-root': {
                      '&:hover fieldset': {
                        borderColor: '#64b5f6',
                      },
                    }
                  }}
                />
              )}
            />
          </Paper>
        )}

        {/* Empty State */}
        {compareVehicles.length === 0 && (
          <Alert severity="info" sx={{ 
            borderRadius: 3, 
            mb: 5, 
            p: 3,
            backgroundColor: '#e3f2fd',
            color: '#1976d2',
            '& .MuiAlert-icon': {
              color: '#1976d2'
            }
          }}>
            <Typography variant="h5" sx={{ fontWeight: 600, mb: 1 }}>
              Chưa có xe nào để so sánh
            </Typography>
            <Typography variant="h6" sx={{ fontWeight: 400 }}>
              Vui lòng thêm ít nhất 2 xe để bắt đầu so sánh
            </Typography>
          </Alert>
        )}

        {/* Vehicle Cards */}
        {compareVehicles.length > 0 && (
          <Grid container spacing={3} sx={{ mb: 6 }}>
            {compareVehicles.map((vehicle, index) => (
              <Grid item xs={12} md={4} key={vehicle.id}>
                <Card 
                  sx={{ 
                    borderRadius: 3,
                    height: '100%',
                    position: 'relative',
                    boxShadow: '0 4px 12px rgba(25, 118, 210, 0.15)',
                    border: '2px solid #e3f2fd',
                    transition: 'all 0.3s ease',
                    '&:hover': {
                      boxShadow: '0 6px 20px rgba(25, 118, 210, 0.2)',
                      borderColor: '#64b5f6',
                      transform: 'translateY(-4px)'
                    }
                  }}
                >
                  <Box sx={{ position: 'relative' }}>
                    <CardMedia
                      component="img"
                      height="220"
                      image={vehicle.images?.[0] ? resolveImagePath(vehicle.images[0]) : placeholderVehicleImage(vehicle.model, 640, 480)}
                      alt={vehicle.model}
                      sx={{ 
                        objectFit: 'cover'
                      }}
                    />
                    
                    <Chip
                      label={`XE ${index + 1}`}
                      sx={{
                        position: 'absolute',
                        top: 16,
                        left: 16,
                        bgcolor: '#1976d2',
                        color: 'white',
                        fontWeight: '700',
                        fontSize: '16px',
                        padding: '8px 12px',
                        height: 'auto'
                      }}
                    />
                    
                    <IconButton
                      onClick={() => handleRemoveVehicle(vehicle.id)}
                      sx={{
                        position: 'absolute',
                        top: 12,
                        right: 12,
                        bgcolor: 'rgba(25, 118, 210, 0.8)',
                        color: 'white',
                        '&:hover': { bgcolor: 'rgba(25, 118, 210, 1)' },
                        width: 48,
                        height: 48
                      }}
                    >
                      <CloseIcon sx={{ fontSize: 28 }} />
                    </IconButton>
                  </Box>

                  <CardContent sx={{ p: 3 }}>
                    <Typography variant="h4" sx={{ fontWeight: 700, mb: 2, minHeight: '64px', color: '#1565c0' }}>
                      {vehicle.brand} {vehicle.model}
                    </Typography>
                    
                    <Typography variant="h3" sx={{ fontWeight: 800, mb: 3, color: '#1976d2' }}>
                      {(vehicle.price / 1000000).toFixed(1)} TRIỆU VND
                    </Typography>

                    {/* Thông số nổi bật */}
                    <Box sx={{ display: 'flex', gap: 2, mb: 3, flexWrap: 'wrap' }}>
                      <Chip 
                        label={`${vehicle.batteryCapacity || '--'} kWh`}
                        variant="outlined"
                        sx={{ 
                          fontSize: '16px', 
                          padding: '12px 16px', 
                          height: 'auto',
                          borderColor: '#64b5f6',
                          color: '#1976d2'
                        }}
                      />
                      <Chip 
                        label={`${vehicle.range || '--'} km`}
                        variant="outlined"
                        sx={{ 
                          fontSize: '16px', 
                          padding: '12px 16px', 
                          height: 'auto',
                          borderColor: '#64b5f6',
                          color: '#1976d2'
                        }}
                      />
                      <Chip 
                        label={vehicle.type === 'sedan' ? 'Sedan' : vehicle.type === 'suv' ? 'SUV' : vehicle.type || '--'}
                        variant="outlined"
                        sx={{ 
                          fontSize: '16px', 
                          padding: '12px 16px', 
                          height: 'auto',
                          borderColor: '#64b5f6',
                          color: '#1976d2'
                        }}
                      />
                    </Box>

                    <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                      <Chip 
                        label={vehicle.stockQuantity > 0 ? 'CÒN HÀNG' : 'HẾT HÀNG'}
                        color={vehicle.stockQuantity > 0 ? 'success' : 'error'}
                        sx={{ 
                          fontSize: '16px', 
                          padding: '12px 20px', 
                          height: 'auto',
                          fontWeight: '700'
                        }}
                      />
                      <Button 
                        size="large" 
                        variant="contained"
                        onClick={() => navigate(`/vehicles/${vehicle.id}`)}
                        sx={{ 
                          fontSize: '16px',
                          padding: '12px 24px',
                          bgcolor: '#1976d2',
                          '&:hover': { bgcolor: '#1565c0' }
                        }}
                      >
                        XEM CHI TIẾT
                      </Button>
                    </Box>
                  </CardContent>
                </Card>
              </Grid>
            ))}
          </Grid>
        )}

        {/* Comparison Table */}
        {compareVehicles.length > 0 && (
          <Paper sx={{ 
            borderRadius: 3, 
            overflow: 'hidden', 
            boxShadow: '0 4px 12px rgba(25, 118, 210, 0.15)',
            mb: 6,
            border: '1px solid #e3f2fd'
          }}>
            {getComparisonData().map((category, catIndex) => (
              <Box key={catIndex}>
                {/* Category Header */}
                <Box sx={{ 
                  bgcolor: '#bbdefb', 
                  p: 3,
                  borderBottom: '2px solid #90caf9'
                }}>
                  <Typography variant="h4" sx={{ fontWeight: 700, color: '#0d47a1' }}>
                    {category.category}
                  </Typography>
                </Box>

                {/* Specs */}
                <Box>
                  {category.specs.map((spec, specIndex) => (
                    <Box 
                      key={specIndex}
                      sx={{ 
                        display: 'flex',
                        borderBottom: '2px solid #f3f9ff',
                        '&:last-child': { borderBottom: 'none' },
                        '&:nth-of-type(odd)': { bgcolor: '#f8fbff' }
                      }}
                    >
                      <Box sx={{ 
                        width: '300px', 
                        p: 3, 
                        bgcolor: 'white',
                        borderRight: '2px solid #e3f2fd',
                        display: 'flex',
                        alignItems: 'center'
                      }}>
                        <Typography variant="h5" sx={{ fontWeight: 600, color: '#1976d2' }}>
                          {spec.label}
                        </Typography>
                      </Box>
                      
                      <Box sx={{ display: 'flex', flex: 1 }}>
                        {spec.values.map((value, valueIndex) => {
                          const best = spec.betterWhen && 
                            isBestValue(value, spec.values, spec.type, spec.betterWhen)
                          
                          return (
                            <Box 
                              key={valueIndex}
                              sx={{ 
                                flex: 1,
                                p: 3,
                                borderRight: valueIndex < spec.values.length - 1 ? '2px solid #e3f2fd' : 'none',
                                bgcolor: best ? '#e3f2fd' : 'transparent',
                                position: 'relative',
                                minHeight: '80px',
                                display: 'flex',
                                alignItems: 'center'
                              }}
                            >
                              <Box sx={{ display: 'flex', alignItems: 'center', gap: 2 }}>
                                {best && (
                                  <CheckIcon 
                                    sx={{ 
                                      color: '#1976d2',
                                      fontSize: 28
                                    }} 
                                  />
                                )}
                                <Typography 
                                  variant="h5"
                                  sx={{ 
                                    fontWeight: best ? 700 : 500,
                                    color: best ? '#1976d2' : '#37474f'
                                  }}
                                >
                                  {value}
                                </Typography>
                              </Box>

                              {/* Best value badge */}
                              {best && (
                                <Box
                                  sx={{
                                    position: 'absolute',
                                    top: 8,
                                    right: 8,
                                    bgcolor: '#1976d2',
                                    color: 'white',
                                    padding: '4px 8px',
                                    borderRadius: '4px',
                                    fontSize: '12px',
                                    fontWeight: '700'
                                  }}
                                >
                                  TỐT NHẤT
                                </Box>
                              )}
                            </Box>
                          )
                        })}
                      </Box>
                    </Box>
                  ))}
                </Box>
              </Box>
            ))}
          </Paper>
        )}

        {/* Quick Actions */}
        {compareVehicles.length > 1 && (
          <Box sx={{ textAlign: 'center' }}>
            <Typography variant="h4" sx={{ mb: 3, color: '#1976d2' }}>
              Đã so sánh {compareVehicles.length} xe điện
            </Typography>
            <Button 
              variant="contained" 
              size="large"
              onClick={() => navigate('/vehicles')}
              sx={{ 
                borderRadius: 3,
                bgcolor: '#1976d2',
                '&:hover': { bgcolor: '#1565c0' },
                fontSize: '20px',
                padding: '16px 32px',
                minWidth: '300px'
              }}
            >
              KHÁM PHÁ THÊM XE KHÁC
            </Button>
          </Box>
        )}
      </Container>
    </Box>
  )
}

export default VehicleCompare