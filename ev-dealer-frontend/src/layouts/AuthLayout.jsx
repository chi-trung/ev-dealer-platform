/**
 * Auth Layout - For Login, Register, Forgot Password pages
 * Beautiful centered layout with gradient background
 */

import { Outlet } from 'react-router-dom'
import { Box, Container, Typography, Paper } from '@mui/material'
import { ElectricCar } from '@mui/icons-material'
// Issue #81: was '../assets/img/bg.jpg' — the SAME artwork as the landing
// hero, but 6,582 kB vs this 107 kB WebP (LANCZOS-downscaled to 1920px,
// q78). One hashed URL now serves both pages.
import bgImage from '../assets/img/bg-hero.webp'

const AuthLayout = () => {
  return (
    <Box
      sx={{
        minHeight: '100vh',
        width: '100%',
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        position: 'relative',
        overflow: 'hidden',
        backgroundImage: `url(${bgImage})`,
        backgroundSize: 'cover',
        backgroundPosition: 'center',
        backgroundRepeat: 'no-repeat',
        '&::before': {
          content: '""',
          position: 'absolute',
          top: 0,
          left: 0,
          right: 0,
          bottom: 0,
          background: 'linear-gradient(135deg, rgba(102, 126, 234, 0.85) 0%, rgba(118, 75, 162, 0.85) 100%)',
          zIndex: 1,
        },
      }}
    >
      <Container
        maxWidth="sm"
        sx={{
          position: 'relative',
          zIndex: 2,
          py: 2,
        }}
      >
        {/* Logo & Title */}
        <Box sx={{ textAlign: 'center', mb: 2 }}>
          <ElectricCar
            sx={{
              fontSize: 50,
              color: 'white',
              mb: 0.5,
              filter: 'drop-shadow(0 4px 12px rgba(0,0,0,0.3))',
            }}
          />
          <Typography
            variant="h5"
            component="h1"
            sx={{
              fontWeight: 700,
              color: 'white',
              textShadow: '0 2px 10px rgba(0,0,0,0.3)',
              mb: 0.3,
            }}
          >
            EV Dealer Management
          </Typography>
          <Typography
            variant="caption"
            sx={{
              color: 'rgba(255, 255, 255, 0.95)',
              textShadow: '0 1px 3px rgba(0,0,0,0.2)',
            }}
          >
            Hệ thống quản lý đại lý xe điện chuyên nghiệp
          </Typography>
        </Box>

        {/* Form Container - Glassmorphism */}
        <Paper
          elevation={0}
          sx={{
            padding: { xs: 2, sm: 2.5 },
            borderRadius: 3,
            background: 'rgba(255, 255, 255, 0.95)',
            backdropFilter: 'blur(20px)',
            boxShadow: '0 8px 32px rgba(0, 0, 0, 0.2)',
            border: '1px solid rgba(255, 255, 255, 0.3)',
          }}
        >
          <Outlet />
        </Paper>

        {/* Footer */}
        <Typography
          variant="caption"
          align="center"
          sx={{
            display: 'block',
            mt: 1.5,
            color: 'rgba(255, 255, 255, 0.9)',
            textShadow: '0 1px 3px rgba(0,0,0,0.3)',
            fontSize: '0.7rem',
          }}
        >
          © 2025 EV Dealer Management
        </Typography>
      </Container>
    </Box>
  )
}

export default AuthLayout

