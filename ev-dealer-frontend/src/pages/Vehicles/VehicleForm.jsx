/**
 * Vehicle Form (Thêm/Sửa Xe) - Giao diện hiện đại
 * Giao diện trực quan với màu xanh dương nhạt
 */

import { useState, useEffect } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import {
  Box,
  Typography,
  Button,
  TextField,
  FormControl,
  InputLabel,
  Select,
  MenuItem,
  Card,
  CardContent,
  Alert,
  CircularProgress,
  Container,
  Grid,
  InputAdornment,
  IconButton,
  Paper,
  Chip,
  Divider,
  Stepper,
  Step,
  StepLabel,
  Avatar,
  Fade,
  Slide
} from '@mui/material'
import {
  Save as SaveIcon,
  Cancel as CancelIcon,
  PhotoCamera as PhotoCameraIcon,
  Delete as DeleteIcon,
  ArrowBack as ArrowBackIcon,
  DriveEta as CarIcon,
  Build as BuildIcon,
  AttachMoney as MoneyIcon,
  Description as DescriptionIcon,
  CheckCircle as CheckCircleIcon,
  Error as ErrorIcon,
  CalendarToday as CalendarIcon,
  Speed as SpeedIcon,
  LocalOffer as TagIcon,
  DirectionsCar as CarModelIcon,
  Person as PersonIcon,
  Info as InfoIcon,
  Check as CheckIcon
} from '@mui/icons-material'

import vehicleService from '../../services/vehicleService'
import resolveImagePath from '../../utils/imageUtils'

const VehicleForm = () => {
  const navigate = useNavigate()
  const { id } = useParams()
  const isEditMode = !!id

  // Form state
  const [formData, setFormData] = useState({
    vehicleName: '',
    brand: '',
    model: '',
    year: '',
    price: '',
    status: '',
    description: '',
    batteryCapacity: '',
    range: '',
    image: null
    // Các trường khác có thể thêm vào đây nếu cần
  })

  // UI state
  const [loading, setLoading] = useState(false)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState(null)
  const [success, setSuccess] = useState(false)
  const [imagePreview, setImagePreview] = useState(null)
  const [dealers, setDealers] = useState([])
  const [vehicleTypes, setVehicleTypes] = useState([])
  const [activeStep, setActiveStep] = useState(0)

  // Color scheme - Màu xanh dương nhạt hiện đại
  const colors = {
    primary: '#2196f3', // Xanh dương chính
    secondary: '#64b5f6', // Xanh dương nhạt hơn
    accent: '#e3f2fd', // Xanh dương rất nhạt
    success: '#4caf50',
    warning: '#ff9800',
    error: '#f44336',
    background: '#fafafa',
    card: '#ffffff',
    text: '#212121',
    textLight: '#757575'
  }

  const steps = [
    { label: 'Thông tin cơ bản', icon: PersonIcon, description: 'Nhập thông tin xe cơ bản' },
    { label: 'Thông số kỹ thuật', icon: SpeedIcon, description: 'Thêm thông số kỹ thuật' },
    { label: 'Xem lại & hoàn tất', icon: CheckIcon, description: 'Kiểm tra và lưu' }
  ]

  // Options - Các tùy chọn
  const brands = [
    'Tesla', 'Toyota', 'Honda', 'Ford', 'Chevrolet',
    'BMW', 'Mercedes-Benz', 'Audi', 'Volkswagen', 'Nissan',
    'Hyundai', 'Kia', 'Lexus', 'Porsche', 'Jaguar' , 'VinFast'
  ]

  const statuses = [
    { value: 'Available', label: 'Có sẵn', color: 'success', icon: CheckCircleIcon },
    { value: 'Sold', label: 'Đã bán', color: 'error', icon: ErrorIcon },
    { value: 'In Maintenance', label: 'Đang bảo dưỡng', color: 'warning', icon: BuildIcon },
    { value: 'Reserved', label: 'Đã đặt cọc', color: 'info', icon: InfoIcon }
  ]

  // Load data on mount
  useEffect(() => {
    if (isEditMode) {
      loadVehicle()
    }
  }, [id])

  const loadVehicle = async () => {
    try {
      setLoading(true)
      const vehicle = await vehicleService.getVehicleById(id)
      setFormData({
        vehicleName: vehicle.vehicleName || '',
        brand: vehicle.brand || '',
        model: vehicle.model || '',
        year: vehicle.year || new Date().getFullYear(),
        price: vehicle.price || '',
        status: vehicle.status || 'Available',
        description: vehicle.description || '',
        image: null
      })
      if (vehicle.images && vehicle.images[0]) {
        setImagePreview(resolveImagePath(vehicle.images[0]))
      }
    } catch (err) {
      setError(err.message || 'Không thể tải thông tin xe')
      console.error('Lỗi tải xe:', err)
    } finally {
      setLoading(false)
    }
  }

  const handleInputChange = (field, value) => {
    setFormData(prev => ({ ...prev, [field]: value }))
  }

  const handleImageUpload = (event) => {
    const file = event.target.files[0]
    if (file) {
      setFormData(prev => ({ ...prev, image: file }))
      const reader = new FileReader()
      reader.onload = (e) => setImagePreview(e.target.result)
      reader.readAsDataURL(file)
    }
  }

  const handleRemoveImage = () => {
    setFormData(prev => ({ ...prev, image: null }))
    setImagePreview(null)
  }

  const handleNext = () => {
    setActiveStep((prev) => prev + 1)
  }

  const handleBack = () => {
    setActiveStep((prev) => prev - 1)
  }

  const validateStep = (step) => {
    switch (step) {
      case 0:
        return formData.vehicleName && formData.brand && formData.model
      case 1:
        return formData.price
      default:
        return true
    }
  }

  const handleSubmit = async (event) => {
    event.preventDefault()

    // Validation
    if (!formData.vehicleName || !formData.brand || !formData.model || !formData.price) {
      setError('Vui lòng điền đầy đủ thông tin bắt buộc')
      setActiveStep(0)
      return
    }

    try {
      setSaving(true)
      setError(null)

      const submitData = {
        model: formData.vehicleName || [formData.brand, formData.model].filter(Boolean).join(' '),
        type: formData.type || 'Electric',
        price: parseFloat(formData.price),
        batteryCapacity: parseFloat(formData.batteryCapacity) || 80.5,
        range: parseInt(formData.range, 10) || 450,
        stockQuantity: parseInt(formData.stockQuantity, 10) || 1,
        description: formData.description,
        dealerId: formData.dealerId || 1
      }

      // Attach the image file if it exists
      if (formData.image) {
        submitData.images = [formData.image]
      }

      if (isEditMode) {
        await vehicleService.updateVehicle(id, submitData)
      } else {
        await vehicleService.createVehicle(submitData)
      }

      setSuccess(true)
      setTimeout(() => {
        navigate('/vehicles')
      }, 1500)

    } catch (err) {
      setError(err.message || 'Không thể lưu xe')
      console.error('Lỗi lưu xe:', err)
    } finally {
      setSaving(false)
    }
  }

  const handleCancel = () => {
    navigate('/vehicles')
  }

  if (loading) {
    return (
      <Container maxWidth="md" sx={{ py: 4, background: colors.background, minHeight: '100vh' }}>
        <Box sx={{
          display: 'flex',
          justifyContent: 'center',
          alignItems: 'center',
          height: '50vh',
          flexDirection: 'column',
          gap: 3
        }}>
          <CircularProgress size={60} sx={{ color: colors.primary }} />
          <Typography variant="h6" sx={{ color: colors.text }}>
            Đang tải thông tin xe...
          </Typography>
        </Box>
      </Container>
    )
  }

  return (
    <Container maxWidth="md" sx={{ py: 4, background: colors.background, minHeight: '100vh' }}>
      {/* Header */}
      <Box sx={{ mb: 4, textAlign: 'center', position: 'relative' }}>
        <IconButton
          onClick={handleCancel}
          sx={{
            position: 'absolute',
            left: 0,
            top: '50%',
            transform: 'translateY(-50%)',
            backgroundColor: 'white',
            boxShadow: '0 2px 8px rgba(0,0,0,0.1)',
            '&:hover': {
              backgroundColor: colors.accent,
              transform: 'translateY(-50%) scale(1.05)',
            },
            transition: 'all 0.2s ease'
          }}
        >
          <ArrowBackIcon />
        </IconButton>

        <Box sx={{ display: 'flex', alignItems: 'center', justifyContent: 'center', mb: 2 }}>
          <Avatar sx={{
            width: 60,
            height: 60,
            background: `linear-gradient(135deg, ${colors.primary}, ${colors.secondary})`,
            boxShadow: '0 4px 20px rgba(33, 150, 243, 0.3)',
            mr: 2
          }}>
            <CarIcon sx={{ fontSize: 32, color: 'white' }} />
          </Avatar>
          <Box textAlign="left">
            <Typography
              variant="h4"
              component="h1"
              sx={{
                fontWeight: '700',
                color: colors.text,
                background: `linear-gradient(135deg, ${colors.primary}, ${colors.secondary})`,
                backgroundClip: 'text',
                WebkitBackgroundClip: 'text',
                WebkitTextFillColor: 'transparent',
              }}
            >
              {isEditMode ? 'Chỉnh sửa xe' : 'Thêm xe mới'}
            </Typography>
            <Typography variant="subtitle1" sx={{ color: colors.textLight, fontWeight: 400 }}>
              {isEditMode ? 'Cập nhật thông tin xe của bạn' : 'Thêm xe mới vào kho'}
            </Typography>
          </Box>
        </Box>
      </Box>

      {/* Progress Stepper */}
      <Box sx={{ mb: 4 }}>
        <Stepper activeStep={activeStep} alternativeLabel>
          {steps.map((step, index) => (
            <Step key={step.label}>
              <StepLabel
                StepIconComponent={() => (
                  <Avatar sx={{
                    width: 40,
                    height: 40,
                    backgroundColor: activeStep >= index ? colors.primary : colors.textLight,
                    color: 'white'
                  }}>
                    <step.icon />
                  </Avatar>
                )}
                sx={{
                  '& .MuiStepLabel-label': {
                    color: colors.text,
                    fontWeight: activeStep >= index ? 600 : 400,
                    mt: 1,
                    fontSize: '0.9rem'
                  },
                  '& .MuiStepLabel-description': {
                    color: colors.textLight,
                    fontSize: '0.8rem'
                  }
                }}
              >
                {step.label}
                <br />
                <Typography variant="caption" sx={{ color: colors.textLight }}>
                  {step.description}
                </Typography>
              </StepLabel>
            </Step>
          ))}
        </Stepper>
      </Box>

      {/* Success Message */}
      {success && (
        <Fade in={success}>
          <Alert
            severity="success"
            sx={{
              mb: 3,
              borderRadius: 3,
              boxShadow: '0 4px 12px rgba(76, 175, 80, 0.2)',
              border: `1px solid ${colors.success}33`,
              backgroundColor: `${colors.success}15`
            }}
            icon={<CheckCircleIcon />}
          >
            <Typography variant="body1" sx={{ fontWeight: '600' }}>
              Xe {isEditMode ? 'đã được cập nhật' : 'đã được tạo'} thành công! Đang chuyển hướng...
            </Typography>
          </Alert>
        </Fade>
      )}

      {/* Error Message */}
      {error && (
        <Fade in={!!error}>
          <Alert
            severity="error"
            sx={{
              mb: 3,
              borderRadius: 3,
              boxShadow: '0 4px 12px rgba(244, 67, 54, 0.2)',
              border: `1px solid ${colors.error}33`,
              backgroundColor: `${colors.error}15`
            }}
            icon={<ErrorIcon />}
          >
            <Typography variant="body1" sx={{ fontWeight: '600' }}>
              {error}
            </Typography>
          </Alert>
        </Fade>
      )}

      {/* Form Container */}
      <Card
        sx={{
          borderRadius: 4,
          boxShadow: '0 8px 32px rgba(0,0,0,0.08)',
          background: colors.card,
          border: `1px solid ${colors.accent}`,
          overflow: 'visible'
        }}
      >
        <CardContent sx={{ p: 0 }}>
          <form onSubmit={handleSubmit}>
            {/* Step 1: Thông tin cơ bản */}
            {activeStep === 0 && (
              <Slide direction="right" in={activeStep === 0} mountOnEnter unmountOnExit>
                <Box sx={{ p: 4 }}>
                  <Typography variant="h5" sx={{
                    mb: 4,
                    color: colors.text,
                    fontWeight: 600,
                    display: 'flex',
                    alignItems: 'center',
                    gap: 1
                  }}>
                    <PersonIcon sx={{ color: colors.primary }} />
                    Thông tin cơ bản
                  </Typography>

                  <Grid container spacing={3}>
                    <Grid xs={12}>
                      <TextField
                        fullWidth
                        label="Tên xe"
                        placeholder="Ví dụ: Tesla Model S Plaid"
                        value={formData.vehicleName}
                        onChange={(e) => handleInputChange('vehicleName', e.target.value)}
                        required
                        variant="outlined"
                        sx={{
                          '& .MuiOutlinedInput-root': {
                            borderRadius: 2,
                            backgroundColor: `${colors.background}`,
                            '& fieldset': {
                              borderColor: colors.accent,
                            },
                            '&:hover fieldset': {
                              borderColor: colors.primary,
                            },
                            '&.Mui-focused fieldset': {
                              borderColor: colors.primary,
                              borderWidth: 2
                            }
                          },
                          '& .MuiInputLabel-root': {
                            color: colors.textLight,
                          }
                        }}
                      />
                    </Grid>

                    <Grid xs={12} md={6}>
                      <FormControl fullWidth required>
                        <InputLabel sx={{ color: colors.textLight }}>Hãng xe</InputLabel>
                        <Select
                          value={formData.brand}
                          label="Hãng xe"
                          onChange={(e) => handleInputChange('brand', e.target.value)}
                          sx={{
                            borderRadius: 2,
                            backgroundColor: `${colors.background}`,
                            '& .MuiOutlinedInput-notchedOutline': {
                              borderColor: colors.accent,
                            },
                            '&:hover .MuiOutlinedInput-notchedOutline': {
                              borderColor: colors.primary,
                            },
                            '&.Mui-focused .MuiOutlinedInput-notchedOutline': {
                              borderColor: colors.primary,
                              borderWidth: 2
                            }
                          }}
                        >
                          <MenuItem value="">Chọn hãng</MenuItem>
                          {brands.map((brand) => (
                            <MenuItem key={brand} value={brand} sx={{ color: colors.text }}>
                              {brand}
                            </MenuItem>
                          ))}
                        </Select>
                      </FormControl>
                    </Grid>

                    <Grid xs={12} md={6}>
                      <TextField
                        fullWidth
                        label="Model"
                        placeholder="Ví dụ: Model 3"
                        value={formData.model}
                        onChange={(e) => handleInputChange('model', e.target.value)}
                        required
                        variant="outlined"
                        sx={{
                          '& .MuiOutlinedInput-root': {
                            borderRadius: 2,
                            backgroundColor: `${colors.background}`,
                            '& fieldset': {
                              borderColor: colors.accent,
                            },
                            '&:hover fieldset': {
                              borderColor: colors.primary,
                            },
                            '&.Mui-focused fieldset': {
                              borderColor: colors.primary,
                              borderWidth: 2
                            }
                          }
                        }}
                      />
                    </Grid>

                    <Grid xs={12}>
                      <TextField
                        fullWidth
                        label="Năm sản xuất"
                        type="number"
                        value={formData.year}
                        onChange={(e) => handleInputChange('year', e.target.value)}
                        inputProps={{
                          min: 1900,
                          max: new Date().getFullYear() + 1
                        }}
                        variant="outlined"
                        sx={{
                          '& .MuiOutlinedInput-root': {
                            borderRadius: 2,
                            backgroundColor: `${colors.background}`,
                            '& fieldset': {
                              borderColor: colors.accent,
                            },
                            '&:hover fieldset': {
                              borderColor: colors.primary,
                            },
                            '&.Mui-focused fieldset': {
                              borderColor: colors.primary,
                              borderWidth: 2
                            }
                          }
                        }}
                      />
                    </Grid>
                  </Grid>
                </Box>
              </Slide>
            )}

            {/* Step 2: Chi tiết & giá */}
            {activeStep === 1 && (
              <Slide direction="left" in={activeStep === 1} mountOnEnter unmountOnExit>
                <Box sx={{ p: 4 }}>
                  <Typography variant="h5" sx={{
                    mb: 4,
                    color: colors.text,
                    fontWeight: 600,
                    display: 'flex',
                    alignItems: 'center',
                    gap: 1
                  }}>
                    <MoneyIcon sx={{ color: colors.primary }} />
                    Chi tiết & Giá cả
                  </Typography>

                  <Grid container spacing={3}>
                    <Grid xs={12} md={6}>
                      <TextField
                        fullWidth
                        label="Giá xe"
                        type="number"
                        placeholder="45000"
                        value={formData.price}
                        onChange={(e) => handleInputChange('price', e.target.value)}
                        required
                        InputProps={{
                          startAdornment: (
                            <InputAdornment position="start">
                              <Typography sx={{ color: colors.textLight, fontWeight: 600 }}>₫</Typography>
                            </InputAdornment>
                          ),
                        }}
                        variant="outlined"
                        sx={{
                          '& .MuiOutlinedInput-root': {
                            borderRadius: 2,
                            backgroundColor: `${colors.background}`,
                            '& fieldset': {
                              borderColor: colors.accent,
                            },
                            '&:hover fieldset': {
                              borderColor: colors.primary,
                            },
                            '&.Mui-focused fieldset': {
                              borderColor: colors.primary,
                              borderWidth: 2
                            }
                          }
                        }}
                      />
                    </Grid>

                    <Grid xs={12} md={6}>
                      <FormControl fullWidth>
                        <InputLabel sx={{ color: colors.textLight }}>Trạng thái</InputLabel>
                        <Select
                          value={formData.status}
                          label="Trạng thái"
                          onChange={(e) => handleInputChange('status', e.target.value)}
                          sx={{
                            borderRadius: 2,
                            backgroundColor: `${colors.background}`,
                            '& .MuiOutlinedInput-notchedOutline': {
                              borderColor: colors.accent,
                            },
                            '&:hover .MuiOutlinedInput-notchedOutline': {
                              borderColor: colors.primary,
                            },
                            '&.Mui-focused .MuiOutlinedInput-notchedOutline': {
                              borderColor: colors.primary,
                              borderWidth: 2
                            }
                          }}
                        >
                          <MenuItem value="">Chọn trạng thái</MenuItem>
                          {statuses.map((status) => (
                            <MenuItem key={status.value} value={status.value}>
                              <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
                                <status.icon sx={{ color: `${status.color}.main`, fontSize: 20 }} />
                                <Chip
                                  label={status.label}
                                  color={status.color}
                                  size="small"
                                  variant="filled"
                                  sx={{ fontWeight: 'bold' }}
                                />
                              </Box>
                            </MenuItem>
                          ))}
                        </Select>
                      </FormControl>
                    </Grid>

                    <Grid xs={12} md={6}>
                      <TextField
                        fullWidth
                        label="Dung lượng pin"
                        type="number"
                        placeholder="80.5"
                        value={formData.batteryCapacity}
                        onChange={(e) => handleInputChange('batteryCapacity', e.target.value)}
                        InputProps={{
                          endAdornment: (
                            <InputAdornment position="end">
                              <Typography sx={{ color: colors.textLight, fontWeight: 600 }}>kWh</Typography>
                            </InputAdornment>
                          ),
                        }}
                        variant="outlined"
                        sx={{
                          '& .MuiOutlinedInput-root': {
                            borderRadius: 2,
                            backgroundColor: `${colors.background}`,
                            '& fieldset': {
                              borderColor: colors.accent,
                            },
                            '&:hover fieldset': {
                              borderColor: colors.primary,
                            },
                            '&.Mui-focused fieldset': {
                              borderColor: colors.primary,
                              borderWidth: 2
                            }
                          }
                        }}
                      />
                    </Grid>

                    <Grid xs={12} md={6}>
                      <TextField
                        fullWidth
                        label="Quãng đường tối đa"
                        type="number"
                        placeholder="450"
                        value={formData.range}
                        onChange={(e) => handleInputChange('range', e.target.value)}
                        InputProps={{
                          endAdornment: (
                            <InputAdornment position="end">
                              <Typography sx={{ color: colors.textLight, fontWeight: 600 }}>km</Typography>
                            </InputAdornment>
                          ),
                        }}
                        variant="outlined"
                        sx={{
                          '& .MuiOutlinedInput-root': {
                            borderRadius: 2,
                            backgroundColor: `${colors.background}`,
                            '& fieldset': {
                              borderColor: colors.accent,
                            },
                            '&:hover fieldset': {
                              borderColor: colors.primary,
                            },
                            '&.Mui-focused fieldset': {
                              borderColor: colors.primary,
                              borderWidth: 2
                            }
                          }
                        }}
                      />
                    </Grid>

                    <Grid xs={12}>
                      <TextField
                        fullWidth
                        label="Mô tả chi tiết"
                        placeholder="Mô tả tính năng, tình trạng và thông số kỹ thuật của xe..."
                        value={formData.description}
                        onChange={(e) => handleInputChange('description', e.target.value)}
                        multiline
                        rows={4}
                        variant="outlined"
                        sx={{
                          '& .MuiOutlinedInput-root': {
                            borderRadius: 2,
                            backgroundColor: `${colors.background}`,
                            '& fieldset': {
                              borderColor: colors.accent,
                            },
                            '&:hover fieldset': {
                              borderColor: colors.primary,
                            },
                            '&.Mui-focused fieldset': {
                              borderColor: colors.primary,
                              borderWidth: 2
                            }
                          }
                        }}
                      />
                    </Grid>
                  </Grid>
                </Box>
              </Slide>
            )}

            {/* Step 3: Xem lại & hoàn tất */}
            {activeStep === 2 && (
              <Slide direction="up" in={activeStep === 2} mountOnEnter unmountOnExit>
                <Box sx={{ p: 4 }}>
                  <Typography variant="h5" sx={{
                    mb: 4,
                    color: colors.text,
                    fontWeight: 600,
                    display: 'flex',
                    alignItems: 'center',
                    gap: 1
                  }}>
                    <CheckIcon sx={{ color: colors.primary }} />
                    Xem lại & Hoàn tất
                  </Typography>

                  <Grid container spacing={4}>
                    <Grid xs={12} md={7}>
                      <Paper sx={{
                        p: 3,
                        borderRadius: 3,
                        backgroundColor: `${colors.background}`,
                        border: `1px solid ${colors.accent}`
                      }}>
                        <Typography variant="h6" sx={{ mb: 3, color: colors.text, fontWeight: 600 }}>
                          Tóm tắt thông tin xe
                        </Typography>

                        <Box sx={{ display: 'flex', flexDirection: 'column', gap: 2 }}>
                          <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                            <Typography sx={{ color: colors.textLight }}>Tên xe:</Typography>
                            <Typography sx={{ color: colors.text, fontWeight: 500 }}>{formData.vehicleName}</Typography>
                          </Box>
                          <Divider />
                          <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                            <Typography sx={{ color: colors.textLight }}>Hãng:</Typography>
                            <Typography sx={{ color: colors.text, fontWeight: 500 }}>{formData.brand}</Typography>
                          </Box>
                          <Divider />
                          <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                            <Typography sx={{ color: colors.textLight }}>Model:</Typography>
                            <Typography sx={{ color: colors.text, fontWeight: 500 }}>{formData.model}</Typography>
                          </Box>
                          <Divider />
                          <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                            <Typography sx={{ color: colors.textLight }}>Năm:</Typography>
                            <Typography sx={{ color: colors.text, fontWeight: 500 }}>{formData.year}</Typography>
                          </Box>
                          <Divider />
                          <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                            <Typography sx={{ color: colors.textLight }}>Giá:</Typography>
                            <Typography sx={{ color: colors.text, fontWeight: 500 }}>₫{formData.price}</Typography>
                          </Box>
                          <Divider />
                          <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                            <Typography sx={{ color: colors.textLight }}>Dung lượng pin:</Typography>
                            <Typography sx={{ color: colors.text, fontWeight: 500 }}>{formData.batteryCapacity || '-'} kWh</Typography>
                          </Box>
                          <Divider />
                          <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                            <Typography sx={{ color: colors.textLight }}>Quãng đường tối đa:</Typography>
                            <Typography sx={{ color: colors.text, fontWeight: 500 }}>{formData.range || '-'} km</Typography>
                          </Box>
                          <Divider />
                          <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                            <Typography sx={{ color: colors.textLight }}>Trạng thái:</Typography>
                            {(() => {
                              const status = statuses.find(s => s.value === formData.status)
                              return (
                                <Chip
                                  label={status?.label || 'Chưa chọn'}
                                  color={status?.color}
                                  size="small"
                                  icon={status ? <status.icon /> : undefined}
                                />
                              )
                            })()}
                          </Box>
                        </Box>
                      </Paper>
                    </Grid>

                    <Grid xs={12} md={5}>
                      <Paper sx={{
                        p: 3,
                        borderRadius: 3,
                        backgroundColor: `${colors.background}`,
                        border: `1px solid ${colors.accent}`,
                        textAlign: 'center'
                      }}>
                        <Typography variant="h6" sx={{ mb: 3, color: colors.text, fontWeight: 600 }}>
                          Hình ảnh xe
                        </Typography>

                        <input
                          accept="image/*"
                          style={{ display: 'none' }}
                          id="image-upload"
                          type="file"
                          onChange={handleImageUpload}
                        />
                        <label htmlFor="image-upload">
                          <Button
                            variant="outlined"
                            component="span"
                            startIcon={<PhotoCameraIcon />}
                            sx={{
                              borderRadius: 2,
                              border: `2px dashed ${colors.primary}`,
                              color: colors.primary,
                              px: 3,
                              py: 2,
                              mb: 2,
                              '&:hover': {
                                backgroundColor: `${colors.primary}08`,
                                borderColor: colors.secondary
                              }
                            }}
                          >
                            Chọn hình ảnh
                          </Button>
                        </label>

                        {imagePreview && (
                          <Box sx={{ mt: 3, position: 'relative' }}>
                            <Box
                              component="img"
                              src={imagePreview}
                              alt="Xem trước xe"
                              sx={{
                                width: '100%',
                                maxHeight: 200,
                                objectFit: 'cover',
                                borderRadius: 2,
                                border: `1px solid ${colors.accent}`
                              }}
                            />
                            <IconButton
                              onClick={handleRemoveImage}
                              sx={{
                                position: 'absolute',
                                top: 8,
                                right: 8,
                                backgroundColor: 'rgba(255,255,255,0.9)',
                                '&:hover': {
                                  backgroundColor: 'white'
                                }
                              }}
                            >
                              <DeleteIcon />
                            </IconButton>
                          </Box>
                        )}
                      </Paper>
                    </Grid>
                  </Grid>
                </Box>
              </Slide>
            )}

            {/* Navigation Buttons */}
            <Box sx={{ p: 3, borderTop: `1px solid ${colors.accent}` }}>
              <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                <Button
                  onClick={activeStep === 0 ? handleCancel : handleBack}
                  startIcon={activeStep === 0 ? <CancelIcon /> : <ArrowBackIcon />}
                  sx={{
                    color: colors.textLight,
                    fontWeight: 600,
                    '&:hover': {
                      color: colors.primary,
                      backgroundColor: `${colors.primary}08`
                    }
                  }}
                >
                  {activeStep === 0 ? 'Hủy' : 'Quay lại'}
                </Button>

                <Box sx={{ display: 'flex', gap: 2 }}>
                  {activeStep < 2 && (
                    <Button
                      onClick={handleNext}
                      disabled={!validateStep(activeStep)}
                      variant="contained"
                      sx={{
                        backgroundColor: colors.primary,
                        borderRadius: 2,
                        px: 4,
                        fontWeight: 600,
                        '&:hover': {
                          backgroundColor: colors.secondary,
                          transform: 'translateY(-1px)',
                          boxShadow: `0 4px 12px ${colors.primary}33`
                        },
                        '&:disabled': {
                          backgroundColor: colors.textLight,
                          color: 'white'
                        }
                      }}
                    >
                      Tiếp theo
                    </Button>
                  )}

                  {activeStep === 2 && (
                    <Button
                      type="submit"
                      variant="contained"
                      disabled={saving}
                      startIcon={saving ? <CircularProgress size={20} /> : <SaveIcon />}
                      sx={{
                        backgroundColor: colors.success,
                        borderRadius: 2,
                        px: 4,
                        fontWeight: 600,
                        '&:hover': {
                          backgroundColor: '#4caf50',
                          transform: 'translateY(-1px)',
                          boxShadow: `0 4px 12px ${colors.success}33`
                        },
                        '&:disabled': {
                          backgroundColor: `${colors.textLight}33`,
                          color: `${colors.textLight}66`
                        }
                      }}
                    >
                      {saving ? 'Đang lưu...' : (isEditMode ? 'Cập nhật xe' : 'Tạo xe mới')}
                    </Button>
                  )}
                </Box>
              </Box>
            </Box>
          </form>
        </CardContent>
      </Card>
    </Container>
  )
}

export default VehicleForm
