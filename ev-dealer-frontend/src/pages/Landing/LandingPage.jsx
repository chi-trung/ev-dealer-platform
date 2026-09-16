/**
 * Landing Page - Modern 3D Design
 * Featuring Porsche Taycan 3D Model
 */

import { Component, Suspense, useEffect, useRef, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { Canvas, useLoader } from '@react-three/fiber'
import { OrbitControls, PerspectiveCamera, Environment, ContactShadows } from '@react-three/drei'
import { GLTFLoader } from 'three/examples/jsm/loaders/GLTFLoader'
import * as THREE from 'three'
// Issue #81: assets imported (hashed + immutable-cached by Vite) instead of
// "/src/assets/..." literals — those runtime paths are NOT valid on the built
// site: the SPA rewrite in vercel.json matches them and serves index.html
// (966B) as if it were the image, so every <img>/background broke.
import car1Img from '../../assets/img/car1.webp'
import car2Img from '../../assets/img/car2.webp'
import car3Img from '../../assets/img/car3.webp'
import car4Img from '../../assets/img/car4.webp'
import bgHeroImg from '../../assets/img/bg-hero.webp'
import {
  Box,
  Container,
  Typography,
  Button,
  Grid,
  Card,
  AppBar,
  Toolbar,
  Stack,
  Chip,
  CircularProgress,
} from '@mui/material'
import {
  ElectricCar,
  Speed,
  Assessment,
  People,
  Inventory,
  Description,
  ArrowForward,
  Login,
  PersonAdd,
} from '@mui/icons-material'

// Issue #81: the whole page used to go WHITE when WebGL was unavailable —
// <Canvas> throws "Error creating WebGL context" during render (common on
// crash-looped GPU drivers / remote desktop / blocklisted drivers) and nothing
// caught it, so React unmounted the entire tree. Three layers of defense now:
//   1. canRenderWebGL() probe BEFORE mounting the Canvas (cheap, synchronous);
//   2. an ErrorBoundary that catches mount-time AND later runtime failures;
//   3. IntersectionObserver gating so the 19 MB GLB + render only start when
//      the section is actually scrolled into view (landing got slow in part
//      because that download competed with first paint).

/** One-shot, side-effect-light WebGL availability probe (null = untested). */
function canRenderWebGL() {
  try {
    const canvas = document.createElement('canvas')
    const gl =
      canvas.getContext('webgl2') ||
      canvas.getContext('webgl') ||
      canvas.getContext('experimental-webgl')
    if (!gl) return false
    // Release the probe context: browsers cap concurrent WebGL contexts and a
    // leaked probe context can make the real Canvas creation fail afterwards.
    const lose = gl.getExtension('WEBGL_lose_context')
    if (lose) lose.loseContext()
    return true
  } catch {
    return false
  }
}

class SceneErrorBoundary extends Component {
  constructor(props) {
    super(props)
    this.state = { failed: false }
  }

  static getDerivedStateFromError() {
    return { failed: true }
  }

  componentDidCatch(error) {
    console.warn('3D scene failed, showing static fallback:', error?.message || error)
  }

  render() {
    return this.state.failed ? this.props.fallback : this.props.children
  }
}

/** Static car image shown instead of the 3D scene (no WebGL / scene crash). */
function SceneFallback() {
  return (
    <Box
      sx={{
        position: 'absolute',
        inset: 0,
        display: 'flex',
        flexDirection: 'column',
        alignItems: 'center',
        justifyContent: 'center',
        gap: 2,
      }}
    >
      <Box
        component="img"
        src={car3Img}
        alt="Porsche Taycan"
        loading="lazy"
        decoding="async"
        sx={{
          width: { xs: '80%', md: '60%' },
          maxWidth: 640,
          filter: 'drop-shadow(0 24px 48px rgba(102,126,234,0.35))',
        }}
      />
      <Typography variant="body2" sx={{ color: 'rgba(255,255,255,0.5)' }}>
        Chế độ 3D không khả dụng trên thiết bị này
      </Typography>
    </Box>
  )
}

/**
 * Mounts the 3D scene only once its section scrolls into view (rootMargin
 * preloads slightly ahead) and only if WebGL is usable. Until then it shows
 * the static fallback — cheap, on-brand, never a white screen.
 */
function LazyCarScene() {
  const holderRef = useRef(null)
  const [inView, setInView] = useState(false)
  const [webglOk] = useState(canRenderWebGL)

  useEffect(() => {
    if (inView) return
    const el = holderRef.current
    if (!el || typeof IntersectionObserver === 'undefined') {
      setInView(true) // no IO support: mount eagerly, still WebGL-guarded
      return
    }
    const observer = new IntersectionObserver(
      (entries) => {
        if (entries.some((e) => e.isIntersecting)) {
          setInView(true)
          observer.disconnect()
        }
      },
      { rootMargin: '300px' },
    )
    observer.observe(el)
    return () => observer.disconnect()
  }, [inView])

  return (
    <Box ref={holderRef} sx={{ position: 'absolute', inset: 0 }}>
      {inView && webglOk ? (
        <SceneErrorBoundary fallback={<SceneFallback />}>
          <Suspense fallback={<Loader />}>
            <CarScene />
          </Suspense>
        </SceneErrorBoundary>
      ) : (
        <SceneFallback />
      )}
    </Box>
  )
}

// 3D Porsche Taycan Model Component
function PorscheTaycan() {
  const gltf = useLoader(GLTFLoader, '/porsche_taycan.glb')
  const modelRef = useRef()

  return (
    <primitive
      ref={modelRef}
      object={gltf.scene}
      scale={1.4}
      position={[0, -0.6, 0]}
      rotation={[0, Math.PI * 0.15, 0]}
    />
  )
}

// Loading Component
function Loader() {
  return (
    <Box
      sx={{
        position: 'absolute',
        top: '50%',
        left: '50%',
        transform: 'translate(-50%, -50%)',
        textAlign: 'center',
      }}
    >
      <CircularProgress sx={{ color: '#667eea' }} />
      <Typography variant="body2" sx={{ color: 'white', mt: 2 }}>
        Đang tải model 3D...
      </Typography>
    </Box>
  )
}

// 3D Scene Component
function CarScene() {
  return (
    <Canvas
      shadows
      style={{
        width: '100%',
        height: '100%',
        background: 'transparent',
      }}
      gl={{
        alpha: true,
        antialias: true,
      }}
    >
      <PerspectiveCamera makeDefault position={[6, 3, 6]} fov={45} />

      <ambientLight intensity={1} />
      <directionalLight position={[10, 10, 5]} intensity={2} castShadow />
      <pointLight position={[-10, 5, -5]} intensity={1} color="#667eea" />
      <pointLight position={[10, 5, 5]} intensity={0.8} color="#f093fb" />
      <spotLight position={[0, 10, 0]} intensity={1} angle={0.3} penumbra={1} castShadow />

      <Environment preset="sunset" />

      <Suspense fallback={null}>
        <PorscheTaycan />
        <ContactShadows
          position={[0, -1, 0]}
          opacity={0.5}
          scale={20}
          blur={2}
          far={5}
        />
      </Suspense>

      <OrbitControls
        enableZoom={true}
        enablePan={false}
        minDistance={4}
        maxDistance={12}
        maxPolarAngle={Math.PI / 2}
        autoRotate
        autoRotateSpeed={0.8}
      />
    </Canvas>
  )
}

// Features Data
const features = [
  {
    icon: <ElectricCar sx={{ fontSize: 40 }} />,
    title: 'Quản Lý Xe Điện',
    description: 'Quản lý danh mục xe, cấu hình, màu sắc và tồn kho',
    color: '#667eea',
  },
  {
    icon: <Speed sx={{ fontSize: 40 }} />,
    title: 'Quản Lý Bán Hàng',
    description: 'Báo giá, đơn hàng, hợp đồng và thanh toán',
    color: '#f093fb',
  },
  {
    icon: <People sx={{ fontSize: 40 }} />,
    title: 'Quản Lý Khách Hàng',
    description: 'CRM, lịch lái thử và chăm sóc khách hàng',
    color: '#4facfe',
  },
  {
    icon: <Assessment sx={{ fontSize: 40 }} />,
    title: 'Báo Cáo & Thống Kê',
    description: 'Phân tích doanh số và dự báo nhu cầu',
    color: '#43e97b',
  },
  {
    icon: <Inventory sx={{ fontSize: 40 }} />,
    title: 'Quản Lý Đại Lý',
    description: 'Quản lý chỉ tiêu, công nợ và hợp đồng',
    color: '#fa709a',
  },
  {
    icon: <Description sx={{ fontSize: 40 }} />,
    title: 'Quản Lý Hợp Đồng',
    description: 'Tạo, theo dõi và quản lý hợp đồng',
    color: '#fee140',
  },
]

// Format price function
const formatPrice = (price) => {
  return price.toString().replace(/\B(?=(\d{3})+(?!\d))/g, ".")
}

export default function LandingPage() {
  const navigate = useNavigate()

  // Car data with detailed prices
  const cars = [
    {
      name: 'Tesla Model 3',
      price: '1.200.000.000',
      formattedPrice: '1.200.000.000 VNĐ',
      range: '580 km',
      speed: '0-100: 3.3s',
      color: '#667eea',
      image: car1Img,
    },
    {
      name: 'VinFast VF8',
      price: '1.100.000.000',
      formattedPrice: '1.100.000.000 VNĐ',
      range: '420 km',
      speed: '0-100: 5.5s',
      color: '#f093fb',
      image: car2Img,
    },
    {
      name: 'Porsche Taycan',
      price: '4.500.000.000',
      formattedPrice: '4.500.000.000 VNĐ',
      range: '484 km',
      speed: '0-100: 2.8s',
      color: '#4facfe',
      image: car3Img,
    },
    {
      name: 'BMW iX',
      price: '3.800.000.000',
      formattedPrice: '3.800.000.000 VNĐ',
      range: '630 km',
      speed: '0-100: 4.6s',
      color: '#43e97b',
      image: car4Img,
    },
  ]

  return (
    <Box
      sx={{
        minHeight: '100vh',
        background: '#0a0e27',
        position: 'relative',
        overflow: 'hidden',
      }}
    >
      {/* Navigation */}
      <AppBar
        position="sticky"
        elevation={0}
        sx={{
          background: 'rgba(10, 14, 39, 0.8)',
          backdropFilter: 'blur(20px)',
          borderBottom: '1px solid rgba(255, 255, 255, 0.1)',
        }}
      >
        <Toolbar>
          <ElectricCar sx={{ fontSize: 32, color: '#667eea', mr: 1 }} />
          <Typography variant="h6" sx={{ flexGrow: 1, fontWeight: 800, color: 'white' }}>
            EV Dealer
          </Typography>
          <Stack direction="row" spacing={2}>
            <Button
              variant="outlined"
              startIcon={<PersonAdd />}
              onClick={() => navigate('/register')}
              sx={{
                borderColor: 'rgba(255, 255, 255, 0.3)',
                color: 'white',
                fontWeight: 700,
                px: 3,
                '&:hover': {
                  borderColor: '#667eea',
                  background: 'rgba(102, 126, 234, 0.1)',
                },
              }}
            >
              ĐĂNG KÝ
            </Button>
            <Button
              variant="contained"
              startIcon={<Login />}
              onClick={() => navigate('/login')}
              sx={{
                background: 'linear-gradient(135deg, #667eea 0%, #764ba2 100%)',
                fontWeight: 700,
                px: 3,
              }}
            >
              ĐĂNG NHẬP
            </Button>
          </Stack>
        </Toolbar>
      </AppBar>

      {/* Hero Section with 3D Model */}
      <Box
        sx={{
          position: 'relative',
          minHeight: '100vh',
          display: 'flex',
          flexDirection: 'column',
          justifyContent: 'center',
          py: 8,
          '&::before': {
            content: '""',
            position: 'absolute',
            top: 0,
            left: 0,
            right: 0,
            bottom: 0,
            backgroundImage: `url(${bgHeroImg})`,
            backgroundSize: 'cover',
            backgroundPosition: 'center',
            backgroundAttachment: 'fixed',
            opacity: 0.12,
            zIndex: -1,
          },
        }}
      >
        <Container maxWidth="xl">
          {/* Top Content - Centered */}
          <Box sx={{ textAlign: 'center', mb: 6 }}>
            <Typography
              variant="h1"
              sx={{
                fontWeight: 900,
                color: 'white',
                mb: 3,
                fontSize: { xs: '3rem', md: '5rem' },
                lineHeight: 1.1,
              }}
            >
              Quản Lý Đại Lý{' '}
              <Box
                component="span"
                sx={{
                  background: 'linear-gradient(135deg, #667eea 0%, #764ba2 100%)',
                  WebkitBackgroundClip: 'text',
                  WebkitTextFillColor: 'transparent',
                }}
              >
                Xe Điện
              </Box>
            </Typography>

            <Typography
              variant="h5"
              sx={{
                color: 'rgba(255, 255, 255, 0.7)',
                mb: 4,
                lineHeight: 1.6,
                maxWidth: '800px',
                mx: 'auto',
              }}
            >
              Giải pháp toàn diện cho việc quản lý xe điện, bán hàng, khách hàng và báo cáo thống kê
            </Typography>

            <Stack direction="row" spacing={2} sx={{ mb: 6, justifyContent: 'center' }}>
              <Button
                variant="contained"
                size="large"
                endIcon={<ArrowForward />}
                onClick={() => navigate('/login')}
                sx={{
                  background: 'linear-gradient(135deg, #667eea 0%, #764ba2 100%)',
                  fontWeight: 700,
                  px: 5,
                  py: 2,
                  fontSize: '1.2rem',
                }}
              >
                BẮT ĐẦU NGAY
              </Button>
              <Button
                variant="outlined"
                size="large"
                sx={{
                  borderColor: 'rgba(255, 255, 255, 0.3)',
                  color: 'white',
                  fontWeight: 700,
                  px: 5,
                  py: 2,
                  fontSize: '1.2rem',
                  '&:hover': {
                    borderColor: '#667eea',
                    background: 'rgba(102, 126, 234, 0.1)',
                  },
                }}
              >
                TÌM HIỂU THÊM
              </Button>
            </Stack>
          </Box>

          {/* 3D Model - Full Width, Centered */}
          <Box
            sx={{
              height: { xs: 500, md: 700 },
              position: 'relative',
              mb: 6,
            }}
          >
            <LazyCarScene />
          </Box>

          {/* Stats - Centered */}
          <Stack direction="row" spacing={6} sx={{ justifyContent: 'center', flexWrap: 'wrap' }}>
            <Box sx={{ textAlign: 'center' }}>
              <Typography variant="h3" sx={{ fontWeight: 900, color: '#667eea', mb: 1 }}>
                99.9%
              </Typography>
              <Typography variant="h6" sx={{ color: 'rgba(255, 255, 255, 0.7)' }}>
                Độ Tin Cậy
              </Typography>
            </Box>
            <Box sx={{ textAlign: 'center' }}>
              <Typography variant="h3" sx={{ fontWeight: 900, color: '#f093fb', mb: 1 }}>
                24/7
              </Typography>
              <Typography variant="h6" sx={{ color: 'rgba(255, 255, 255, 0.7)' }}>
                Hỗ Trợ
              </Typography>
            </Box>
            <Box sx={{ textAlign: 'center' }}>
              <Typography variant="h3" sx={{ fontWeight: 900, color: '#4facfe', mb: 1 }}>
                100%
              </Typography>
              <Typography variant="h6" sx={{ color: 'rgba(255, 255, 255, 0.7)' }}>
                Bảo Mật
              </Typography>
            </Box>
          </Stack>
        </Container>
      </Box>

      {/* Features Section */}
      <Box sx={{ py: 12, position: 'relative', zIndex: 1 }}>
        <Container maxWidth="lg">
          <Box sx={{ textAlign: 'center', mb: 8 }}>
            <Typography
              variant="h2"
              sx={{
                fontWeight: 900,
                color: 'white',
                mb: 2,
                fontSize: { xs: '2rem', md: '3rem' },
              }}
            >
              Tính Năng Nổi Bật
            </Typography>
            <Typography variant="h6" sx={{ color: 'rgba(255, 255, 255, 0.6)' }}>
              Quản lý toàn diện mọi khía cạnh của đại lý xe điện
            </Typography>
          </Box>

          <Box
            sx={{
              display: 'grid',
              gridTemplateColumns: { xs: '1fr', sm: 'repeat(2, 1fr)', md: 'repeat(3, 1fr)' },
              gap: 3,
              maxWidth: 1200,
              mx: 'auto',
            }}
          >
            {features.map((feature, index) => (
              <Card
                key={index}
                elevation={0}
                sx={{
                  height: 240,
                  display: 'flex',
                  flexDirection: 'column',
                  background: 'rgba(255, 255, 255, 0.05)',
                  backdropFilter: 'blur(20px)',
                  border: '1px solid rgba(255, 255, 255, 0.1)',
                  borderRadius: 4,
                  p: 3,
                  transition: 'all 0.4s cubic-bezier(0.4, 0, 0.2, 1)',
                  cursor: 'pointer',
                  '&:hover': {
                    transform: 'translateY(-12px)',
                    boxShadow: `0 24px 60px ${feature.color}40`,
                    border: `1px solid ${feature.color}60`,
                    background: 'rgba(255, 255, 255, 0.08)',
                  },
                }}
              >
                <Box
                  sx={{
                    width: 60,
                    height: 60,
                    borderRadius: 3,
                    background: `linear-gradient(135deg, ${feature.color}20 0%, ${feature.color}05 100%)`,
                    display: 'flex',
                    alignItems: 'center',
                    justifyContent: 'center',
                    mb: 2,
                    color: feature.color,
                    flexShrink: 0,
                  }}
                >
                  {feature.icon}
                </Box>
                <Typography
                  variant="h6"
                  sx={{
                    fontWeight: 800,
                    color: 'white',
                    mb: 1.5,
                    fontSize: '1.15rem',
                    lineHeight: 1.3,
                    height: 40,
                    display: 'flex',
                    alignItems: 'center',
                  }}
                >
                  {feature.title}
                </Typography>
                <Typography
                  variant="body2"
                  sx={{
                    color: 'rgba(255, 255, 255, 0.6)',
                    lineHeight: 1.5,
                    fontSize: '0.9rem',
                    overflow: 'hidden',
                    textOverflow: 'ellipsis',
                    display: '-webkit-box',
                    WebkitLineClamp: 2,
                    WebkitBoxOrient: 'vertical',
                  }}
                >
                  {feature.description}
                </Typography>
              </Card>
            ))}
          </Box>
        </Container>
      </Box>

      {/* Why Choose Us Section */}
      <Box sx={{ py: 12, position: 'relative', zIndex: 1, background: 'rgba(0, 0, 0, 0.2)' }}>
        <Container maxWidth="lg">
          <Box sx={{ textAlign: 'center', mb: 8 }}>
            <Typography
              variant="h2"
              sx={{
                fontWeight: 900,
                color: 'white',
                mb: 2,
                fontSize: { xs: '2rem', md: '3rem' },
              }}
            >
              Tại Sao Chọn Chúng Tôi?
            </Typography>
            <Typography variant="h6" sx={{ color: 'rgba(255, 255, 255, 0.6)' }}>
              Những lợi ích vượt trội khi sử dụng hệ thống của chúng tôi
            </Typography>
          </Box>

          <Box
            sx={{
              display: 'grid',
              gridTemplateColumns: { xs: '1fr', sm: 'repeat(2, 1fr)' },
              gap: 4,
              maxWidth: 900,
              mx: 'auto',
            }}
          >
            <Card
              elevation={0}
              sx={{
                height: 220,
                background: 'rgba(255, 255, 255, 0.05)',
                backdropFilter: 'blur(20px)',
                border: '1px solid rgba(255, 255, 255, 0.1)',
                borderRadius: 4,
                p: 4,
                textAlign: 'center',
                transition: 'all 0.3s ease',
                '&:hover': {
                  transform: 'translateY(-8px)',
                  boxShadow: '0 20px 40px rgba(102, 126, 234, 0.3)',
                },
              }}
            >
              <Typography variant="h4" sx={{ mb: 2 }}>⚡</Typography>
              <Typography variant="h5" sx={{ fontWeight: 800, color: 'white', mb: 2 }}>
                Nhanh Chóng
              </Typography>
              <Typography variant="body1" sx={{ color: 'rgba(255, 255, 255, 0.6)', lineHeight: 1.7 }}>
                Xử lý dữ liệu siêu nhanh, giao diện mượt mà, tiết kiệm thời gian làm việc
              </Typography>
            </Card>

            <Card
              elevation={0}
              sx={{
                height: 220,
                background: 'rgba(255, 255, 255, 0.05)',
                backdropFilter: 'blur(20px)',
                border: '1px solid rgba(255, 255, 255, 0.1)',
                borderRadius: 4,
                p: 4,
                textAlign: 'center',
                transition: 'all 0.3s ease',
                '&:hover': {
                  transform: 'translateY(-8px)',
                  boxShadow: '0 20px 40px rgba(240, 147, 251, 0.3)',
                },
              }}
            >
              <Typography variant="h4" sx={{ mb: 2 }}>🔒</Typography>
              <Typography variant="h5" sx={{ fontWeight: 800, color: 'white', mb: 2 }}>
                An Toàn
              </Typography>
              <Typography variant="body1" sx={{ color: 'rgba(255, 255, 255, 0.6)', lineHeight: 1.7 }}>
                Bảo mật đa lớp, mã hóa dữ liệu, đảm bảo thông tin tuyệt đối an toàn
              </Typography>
            </Card>

            <Card
              elevation={0}
              sx={{
                height: 220,
                background: 'rgba(255, 255, 255, 0.05)',
                backdropFilter: 'blur(20px)',
                border: '1px solid rgba(255, 255, 255, 0.1)',
                borderRadius: 4,
                p: 4,
                textAlign: 'center',
                transition: 'all 0.3s ease',
                '&:hover': {
                  transform: 'translateY(-8px)',
                  boxShadow: '0 20px 40px rgba(67, 233, 123, 0.3)',
                },
              }}
            >
              <Typography variant="h4" sx={{ mb: 2 }}>💎</Typography>
              <Typography variant="h5" sx={{ fontWeight: 800, color: 'white', mb: 2 }}>
                Chuyên Nghiệp
              </Typography>
              <Typography variant="body1" sx={{ color: 'rgba(255, 255, 255, 0.6)', lineHeight: 1.7 }}>
                Giao diện hiện đại, tính năng đầy đủ, hỗ trợ tận tâm 24/7
              </Typography>
            </Card>

            <Card
              elevation={0}
              sx={{
                height: 220,
                background: 'rgba(255, 255, 255, 0.05)',
                backdropFilter: 'blur(20px)',
                border: '1px solid rgba(255, 255, 255, 0.1)',
                borderRadius: 4,
                p: 4,
                textAlign: 'center',
                transition: 'all 0.3s ease',
                '&:hover': {
                  transform: 'translateY(-8px)',
                  boxShadow: '0 20px 40px rgba(254, 225, 64, 0.3)',
                },
              }}
            >
              <Typography variant="h4" sx={{ mb: 2 }}>🚀</Typography>
              <Typography variant="h5" sx={{ fontWeight: 800, color: 'white', mb: 2 }}>
                Dễ Sử Dụng
              </Typography>
              <Typography variant="body1" sx={{ color: 'rgba(255, 255, 255, 0.6)', lineHeight: 1.7 }}>
                Giao diện trực quan, dễ học, không cần đào tạo phức tạp
              </Typography>
            </Card>
          </Box>
        </Container>
      </Box>

      {/* Demo Xe Section - Modern Card Layout */}
      <Box sx={{ py: 8, pb: 6, position: 'relative', zIndex: 1, overflow: 'hidden' }}>
        <Container maxWidth="lg">
          <Box sx={{ textAlign: 'center', mb: 10 }}>
            <Typography
              variant="h2"
              sx={{
                fontWeight: 900,
                color: 'white',
                mb: 2,
                fontSize: { xs: '2rem', md: '3rem' },
              }}
            >
              Xe Điện Nổi Bật
            </Typography>
            <Typography variant="h6" sx={{ color: 'rgba(255, 255, 255, 0.6)' }}>
              Khám phá các mẫu xe điện cao cấp với giá tốt nhất
            </Typography>
          </Box>

          {/* Modern Grid Layout */}
          <Box
            sx={{
              display: 'grid',
              gridTemplateColumns: { xs: '1fr', md: 'repeat(2, 1fr)' },
              gap: 4,
              maxWidth: 1100,
              mx: 'auto',
            }}
          >
            {cars.map((car, index) => (
              <Card
                key={index}
                elevation={0}
                sx={{
                  background: 'rgba(255, 255, 255, 0.03)',
                  backdropFilter: 'blur(20px)',
                  border: '1px solid rgba(255, 255, 255, 0.08)',
                  borderRadius: 5,
                  overflow: 'hidden',
                  cursor: 'pointer',
                  transition: 'all 0.4s cubic-bezier(0.4, 0, 0.2, 1)',
                  position: 'relative',
                  '&::before': {
                    content: '""',
                    position: 'absolute',
                    top: 0,
                    left: 0,
                    right: 0,
                    height: '50%',
                    background: `linear-gradient(180deg, ${car.color}15 0%, transparent 100%)`,
                    opacity: 0,
                    transition: 'opacity 0.4s ease',
                  },
                  '&:hover': {
                    transform: 'translateY(-12px)',
                    boxShadow: `0 20px 60px ${car.color}40`,
                    border: `1px solid ${car.color}60`,
                    '&::before': {
                      opacity: 1,
                    },
                    '& .car-image': {
                      transform: 'scale(1.1) translateY(-8px)',
                    },
                    '& .detail-btn': {
                      background: car.color,
                      transform: 'translateY(0)',
                      opacity: 1,
                    },
                  },
                }}
              >
                {/* Car Image Section */}
                <Box
                  sx={{
                    position: 'relative',
                    height: 280,
                    display: 'flex',
                    alignItems: 'center',
                    justifyContent: 'center',
                    overflow: 'hidden',
                    background: `radial-gradient(circle at 50% 80%, ${car.color}20 0%, transparent 60%)`,
                  }}
                >
                  <Box
                    component="img"
                    src={car.image}
                    alt={car.name}
                    loading="lazy"
                    decoding="async"
                    className="car-image"
                    sx={{
                      width: '85%',
                      height: '85%',
                      objectFit: 'contain',
                      transition: 'all 0.6s cubic-bezier(0.4, 0, 0.2, 1)',
                      filter: 'drop-shadow(0 20px 40px rgba(0,0,0,0.3))',
                    }}
                  />

                  {/* Price Badge - Updated with rounded corners and full price */}
                  <Box
                    sx={{
                      position: 'absolute',
                      top: 20,
                      right: 20,
                      background: `linear-gradient(135deg, ${car.color} 0%, ${car.color}dd 100%)`,
                      color: 'white',
                      px: 3,
                      py: 1.5,
                      borderRadius: '15px !important', // More rounded corners with important
                      fontWeight: 900,
                      fontSize: '1rem',
                      boxShadow: `0 8px 24px ${car.color}60`,
                      textAlign: 'center',
                      minWidth: 180,
                      border: '2px solid rgba(255, 255, 255, 0.2)',
                    }}
                  >
                    <Typography sx={{ fontSize: '0.9rem', fontWeight: 800, lineHeight: 1.2 }}>
                      {car.formattedPrice}
                    </Typography>
                  </Box>
                </Box>

                {/* Car Info */}
                <Box sx={{ p: 4 }}>
                  <Typography
                    variant="h5"
                    sx={{
                      fontWeight: 900,
                      color: 'white',
                      mb: 3,
                      fontSize: '1.5rem',
                    }}
                  >
                    {car.name}
                  </Typography>

                  {/* Specs */}
                  <Stack direction="row" spacing={2} sx={{ mb: 3 }}>
                    <Box
                      sx={{
                        flex: 1,
                        px: 2,
                        py: 1.5,
                        borderRadius: 2,
                        background: 'rgba(255, 255, 255, 0.05)',
                        border: '1px solid rgba(255, 255, 255, 0.1)',
                        textAlign: 'center',
                      }}
                    >
                      <Typography
                        sx={{
                          color: 'rgba(255, 255, 255, 0.5)',
                          fontSize: '0.75rem',
                          mb: 0.5,
                        }}
                      >
                        Phạm vi
                      </Typography>
                      <Typography
                        sx={{
                          color: car.color,
                          fontWeight: 800,
                          fontSize: '1rem',
                        }}
                      >
                        {car.range}
                      </Typography>
                    </Box>
                    <Box
                      sx={{
                        flex: 1,
                        px: 2,
                        py: 1.5,
                        borderRadius: 2,
                        background: 'rgba(255, 255, 255, 0.05)',
                        border: '1px solid rgba(255, 255, 255, 0.1)',
                        textAlign: 'center',
                      }}
                    >
                      <Typography
                        sx={{
                          color: 'rgba(255, 255, 255, 0.5)',
                          fontSize: '0.75rem',
                          mb: 0.5,
                        }}
                      >
                        Tốc độ
                      </Typography>
                      <Typography
                        sx={{
                          color: car.color,
                          fontWeight: 800,
                          fontSize: '1rem',
                        }}
                      >
                        {car.speed}
                      </Typography>
                    </Box>
                  </Stack>

                  {/* CTA Button */}
                  <Button
                    fullWidth
                    className="detail-btn"
                    sx={{
                      py: 1.8,
                      borderRadius: 3,
                      background: 'rgba(255, 255, 255, 0.05)',
                      border: `1px solid ${car.color}40`,
                      color: 'white',
                      fontWeight: 800,
                      fontSize: '1rem',
                      textTransform: 'none',
                      transition: 'all 0.4s cubic-bezier(0.4, 0, 0.2, 1)',
                      transform: 'translateY(8px)',
                      opacity: 0.7,
                      '&:hover': {
                        background: car.color,
                        border: `1px solid ${car.color}`,
                      },
                    }}
                    endIcon={<ArrowForward />}
                  >
                    Xem Chi Tiết
                  </Button>
                </Box>
              </Card>
            ))}
          </Box>
        </Container>
      </Box>

      {/* Team Members Section */}
      <Box sx={{ pt: 6, pb: 8, position: 'relative', zIndex: 1 }}>
        <Container maxWidth="md">
          <Card
            elevation={0}
            sx={{
              background: 'linear-gradient(135deg, #667eea 0%, #764ba2 100%)',
              borderRadius: 4,
              p: 6,
              textAlign: 'center',
            }}
          >
            <Typography
              variant="h3"
              sx={{
                fontWeight: 900,
                color: 'white',
                mb: 2,
                fontSize: { xs: '2rem', md: '2.5rem' },
              }}
            >
              Thành Viên Nhóm
            </Typography>
            <Typography variant="h6" sx={{ color: 'rgba(255, 255, 255, 0.9)', mb: 4 }}>
              Đội ngũ phát triển hệ thống quản lý đại lý xe điện
            </Typography>

            {/* Team Members Grid */}
            <Box
              sx={{
                display: 'grid',
                gridTemplateColumns: { xs: '1fr', sm: 'repeat(2, 1fr)' },
                gap: 3,
                mb: 4,
              }}
            >
              {[
                { name: 'Nguyễn Chí Trung', role: 'Developer' },
                { name: 'Trần Minh Nhựt', role: 'Developer' },
                { name: 'Đặng Thanh Duy', role: 'Team Leader' },
                { name: 'Huỳnh Nguyễn Đăng', role: 'Developer' },
              ].map((member, index) => (
                <Box
                  key={index}
                  sx={{
                    background: 'rgba(255, 255, 255, 0.15)',
                    backdropFilter: 'blur(10px)',
                    borderRadius: 3,
                    p: 3,
                    border: '1px solid rgba(255, 255, 255, 0.2)',
                    transition: 'all 0.3s ease',
                    '&:hover': {
                      background: 'rgba(255, 255, 255, 0.25)',
                      transform: 'translateY(-4px)',
                    },
                  }}
                >
                  <Typography
                    sx={{
                      color: 'white',
                      fontWeight: 800,
                      fontSize: '1.2rem',
                      mb: 0.5,
                    }}
                  >
                    {member.name}
                  </Typography>
                  <Typography
                    sx={{
                      color: 'rgba(255, 255, 255, 0.7)',
                      fontSize: '0.9rem',
                    }}
                  >
                    {member.role}
                  </Typography>
                </Box>
              ))}
            </Box>
            <Stack direction="row" spacing={2} sx={{ justifyContent: 'center' }}>
              <Button
                variant="contained"
                size="large"
                startIcon={<PersonAdd />}
                onClick={() => navigate('/register')}
                sx={{
                  background: 'white',
                  color: '#667eea',
                  fontWeight: 700,
                  px: 5,
                  py: 2,
                  fontSize: '1.1rem',
                  '&:hover': {
                    background: 'rgba(255, 255, 255, 0.9)',
                  },
                }}
              >
                ĐĂNG KÝ NGAY
              </Button>
              <Button
                variant="outlined"
                size="large"
                endIcon={<ArrowForward />}
                onClick={() => navigate('/login')}
                sx={{
                  borderColor: 'white',
                  color: 'white',
                  fontWeight: 700,
                  px: 5,
                  py: 2,
                  fontSize: '1.1rem',
                  '&:hover': {
                    borderColor: 'white',
                    background: 'rgba(255, 255, 255, 0.1)',
                  },
                }}
              >
                ĐĂNG NHẬP
              </Button>
            </Stack>
          </Card>
        </Container>
      </Box>

      {/* Footer */}
      <Box
        sx={{
          py: 6,
          borderTop: '1px solid rgba(255, 255, 255, 0.1)',
          background: 'rgba(0, 0, 0, 0.2)',
        }}
      >
        <Container maxWidth="lg">
          <Grid container spacing={4}>
            <Grid item xs={12} md={6}>
              <Stack direction="row" spacing={1} alignItems="center" sx={{ mb: 2 }}>
                <ElectricCar sx={{ fontSize: 32, color: '#667eea' }} />
                <Typography variant="h6" sx={{ fontWeight: 800, color: 'white' }}>
                  EV Dealer Management
                </Typography>
              </Stack>
              <Typography variant="body2" sx={{ color: 'rgba(255, 255, 255, 0.6)' }}>
                Hệ thống quản lý đại lý xe điện toàn diện
              </Typography>
            </Grid>
            <Grid item xs={12} md={6}>
              <Typography variant="body2" sx={{ color: 'rgba(255, 255, 255, 0.6)', textAlign: { xs: 'left', md: 'right' } }}>
                © 2025 EV Dealer Management System. All rights reserved.
              </Typography>
            </Grid>
          </Grid>
        </Container>
      </Box>
    </Box>
  )
}