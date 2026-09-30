/**
 * Notifications Page - Modern UI with Material-UI
 * Features:
 * - List of notifications with unread indicators
 * - Filter by type (Orders, Deliveries, Payments, System)
 * - Mark as read/unread functionality
 * - Delete notification
 * - Mark all as read button
 * - Link to notification preferences
 */

import { useState, useEffect, useCallback } from 'react'
import { useNavigate } from 'react-router-dom'
import {
  Box,
  Typography,
  Button,
  Card,
  CardContent,
  Chip,
  ToggleButtonGroup,
  ToggleButton,
  IconButton,
  List,
  ListItem,
  ListItemAvatar,
  ListItemText,
  ListItemSecondaryAction,
  Avatar,
  Badge,
  Container,
  Grid,
  Paper,
  Alert,
  CircularProgress,
  Tooltip,
  Divider,
  Fab,
  Fade,
  Breadcrumbs,
  Link
} from '@mui/material'
import {
  Notifications as NotificationsIcon,
  ShoppingCart as OrdersIcon,
  LocalShipping as DeliveriesIcon,
  Payment as PaymentsIcon,
  Settings as SystemIcon,
  DoneAll as MarkAllReadIcon,
  Settings as PreferencesIcon,
  Delete as DeleteIcon,
  CheckCircle as ReadIcon,
  RadioButtonUnchecked as UnreadIcon,
  NavigateNext as NextIcon,
  Home as HomeIcon,
  NotificationsActive as ActiveNotificationsIcon
} from '@mui/icons-material'

import notificationService from '../../services/notificationService'
import { notificationTypes } from '../../data/notificationTypes'

const Notifications = () => {
  const navigate = useNavigate()

  // State management
  const [notifications, setNotifications] = useState([])
  const [filteredNotifications, setFilteredNotifications] = useState([])
  const [stats, setStats] = useState(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState(null)
  const [activeFilter, setActiveFilter] = useState('all')
  const [markingAll, setMarkingAll] = useState(false)

  // useCallback with [notifications, activeFilter] makes the effect dependency
  // honest: the filter effect re-runs exactly when the data or the selected
  // type changes, and never on an unrelated re-render.
  //
  // Declared ABOVE the effect on purpose: the dependency array is evaluated
  // during render, so naming a const declared further down throws
  // `ReferenceError: Cannot access 'filterNotifications' before initialization`
  // (measured with a throwaway render test, not assumed).
  const filterNotifications = useCallback(() => {
    if (activeFilter === 'all') {
      setFilteredNotifications(notifications)
    } else {
      setFilteredNotifications(
        notifications.filter(notification => notification.type === activeFilter)
      )
    }
  }, [notifications, activeFilter])

  // Load notifications on mount
  useEffect(() => {
    loadNotifications()
  }, [])

  // Filter notifications when filter changes
  useEffect(() => {
    filterNotifications()
  }, [filterNotifications])

  // Listen for real-time Firebase notifications
  useEffect(() => {
    const handleNewNotification = (event) => {
      console.log('🔔 Real-time notification received:', event.detail)
      // Reload notifications to show the new one
      loadNotifications()
    }

    window.addEventListener('firebase-notification-received', handleNewNotification)

    return () => {
      window.removeEventListener('firebase-notification-received', handleNewNotification)
    }
  }, [])

  const loadNotifications = async () => {
    try {
      setLoading(true)
      setError(null)

      const [notificationsResponse, statsResponse] = await Promise.all([
        notificationService.getNotifications(),
        notificationService.getNotificationStats()
      ])

      setNotifications(notificationsResponse.data)
      setStats(statsResponse.data)
    } catch (err) {
      setError(err.message || 'Failed to load notifications')
      console.error('Error loading notifications:', err)
    } finally {
      setLoading(false)
    }
  }

  const handleMarkAsRead = async (notificationId, isRead) => {
    try {
      await notificationService.markAsRead(notificationId, isRead)

      // Update local state
      setNotifications(prev =>
        prev.map(notification =>
          notification.id === notificationId
            ? { ...notification, isRead }
            : notification
        )
      )

      // Update stats
      if (stats) {
        setStats(prev => ({
          ...prev,
          unread: isRead ? prev.unread - 1 : prev.unread + 1
        }))
      }
    } catch (err) {
      setError('Failed to update notification status')
      console.error('Error updating notification:', err)
    }
  }

  const handleMarkAllAsRead = async () => {
    try {
      setMarkingAll(true)
      await notificationService.markAllAsRead()

      // Update local state
      setNotifications(prev =>
        prev.map(notification => ({ ...notification, isRead: true }))
      )

      // Update stats
      if (stats) {
        setStats(prev => ({ ...prev, unread: 0 }))
      }
    } catch (err) {
      setError('Failed to mark all notifications as read')
      console.error('Error marking all as read:', err)
    } finally {
      setMarkingAll(false)
    }
  }

  const handleDeleteNotification = async (notificationId) => {
    try {
      await notificationService.deleteNotification(notificationId)

      // Update local state
      setNotifications(prev =>
        prev.filter(notification => notification.id !== notificationId)
      )

      // Update stats
      if (stats) {
        const deletedNotification = notifications.find(n => n.id === notificationId)
        if (deletedNotification && !deletedNotification.isRead) {
          setStats(prev => ({
            ...prev,
            unread: prev.unread - 1,
            total: prev.total - 1,
            byType: {
              ...prev.byType,
              [deletedNotification.type]: prev.byType[deletedNotification.type] - 1
            }
          }))
        } else {
          setStats(prev => ({
            ...prev,
            total: prev.total - 1,
            byType: {
              ...prev.byType,
              [deletedNotification.type]: prev.byType[deletedNotification.type] - 1
            }
          }))
        }
      }
    } catch (err) {
      setError('Failed to delete notification')
      console.error('Error deleting notification:', err)
    }
  }

  const getNotificationIcon = (type) => {
    switch (type) {
      case 'orders':
        return <OrdersIcon />
      case 'deliveries':
        return <DeliveriesIcon />
      case 'payments':
        return <PaymentsIcon />
      case 'system':
        return <SystemIcon />
      default:
        return <NotificationsIcon />
    }
  }

  const getNotificationColor = (type) => {
    switch (type) {
      case 'orders':
        return '#2196f3'
      case 'deliveries':
        return '#ff9800'
      case 'payments':
        return '#4caf50'
      case 'system':
        return '#9c27b0'
      default:
        return '#757575'
    }
  }

  const formatTimeAgo = (dateString) => {
    const now = new Date()
    const date = new Date(dateString)
    const diffInSeconds = Math.floor((now - date) / 1000)

    if (diffInSeconds < 60) return 'Vừa xong'
    if (diffInSeconds < 3600) return `${Math.floor(diffInSeconds / 60)} phút trước`
    if (diffInSeconds < 86400) return `${Math.floor(diffInSeconds / 3600)} giờ trước`
    if (diffInSeconds < 604800) return `${Math.floor(diffInSeconds / 86400)} ngày trước`

    return date.toLocaleDateString('vi-VN')
  }

  if (loading) {
    return (
      <Container maxWidth="lg" sx={{ py: 4 }}>
        <Box sx={{ display: 'flex', justifyContent: 'center', alignItems: 'center', height: '50vh' }}>
          <CircularProgress size={60} />
          <Typography variant="h6" sx={{ ml: 2 }}>
            Đang tải thông báo...
          </Typography>
        </Box>
      </Container>
    )
  }

  return (
    <Box
      sx={{
        minHeight: '100vh',
        background: 'linear-gradient(135deg, #f5f7fa 0%, #c3cfe2 100%)',
        py: 4
      }}
    >
      <Container maxWidth="lg">
        {/* Breadcrumbs */}
        

        {/* Hero Header */}
        <Box
          sx={{
            mb: 6,
            textAlign: 'center',
            background: 'linear-gradient(135deg, rgba(255,255,255,0.9) 0%, rgba(255,255,255,0.7) 100%)',
            borderRadius: 4,
            p: 6,
            boxShadow: '0 20px 40px rgba(0,0,0,0.1)',
            border: '1px solid rgba(255,255,255,0.2)',
            position: 'relative',
            overflow: 'hidden',
            '&::before': {
              content: '""',
              position: 'absolute',
              top: 0,
              left: 0,
              right: 0,
              bottom: 0,
              background: 'url("data:image/svg+xml,%3Csvg width="60" height="60" viewBox="0 0 60 60" xmlns="http://www.w3.org/2000/svg"%3E%3Cg fill="none" fill-rule="evenodd"%3E%3Cg fill="%239C92AC" fill-opacity="0.05"%3E%3Ccircle cx="30" cy="30" r="4"/%3E%3C/g%3E%3C/g%3E%3C/svg%3E")',
              opacity: 0.3
            }
          }}
        >
          <Box sx={{ display: 'flex', alignItems: 'center', justifyContent: 'center', mb: 3, position: 'relative', zIndex: 1 }}>
            <Typography
              variant="h2"
              component="h1"
              sx={{
                fontWeight: 800,
                background: 'linear-gradient(45deg, #1976d2, #42a5f5, #1565c0)',
                backgroundClip: 'text',
                WebkitBackgroundClip: 'text',
                WebkitTextFillColor: 'transparent',
                textShadow: '0 2px 4px rgba(0,0,0,0.1)',
                letterSpacing: '-0.02em',
                border : '24px',
              }}
            >
              Thông báo
            </Typography>
          </Box>
          <Typography
            variant="h5"
            color="text.secondary"
            sx={{
              fontWeight: 400,
              maxWidth: 600,
              mx: 'auto',
              lineHeight: 1.6,
              opacity: 0.8
            }}
          >
            Theo dõi tất cả hoạt động và cập nhật quan trọng của hệ thống
          </Typography>
        </Box>

      {/* Error Message */}
      {error && (
        <Alert severity="error" sx={{ mb: 3, borderRadius: 2 }}>
          {error}
        </Alert>
      )}

      {/* Stats Cards */}
      {stats && (
          <Grid container spacing={3} sx={{ mb: 6 }}>
          <Grid size={{ xs: 12, sm: 6, md: 3}}>
              <Paper
              elevation={2}
              sx={{
                p: 4,
                textAlign: 'center',
                borderRadius: 4,
                background: 'linear-gradient(135deg, #eef6ff 0%, #e4f1ff 100%)',
                border: '1px solid rgba(13,71,161,0.06)',
                minHeight: 160,
                width: '100%',
                display: 'flex',
                flexDirection: 'column',
                justifyContent: 'center',
                position: 'relative',
                overflow: 'hidden',
                boxShadow: '0 12px 30px rgba(13,71,161,0.06)',
                '&:hover': {
                  transform: 'translateY(-6px) scale(1.01)',
                  boxShadow: '0 16px 36px rgba(13,71,161,0.08)'
                },
                '&::before': {
                  content: '""',
                  position: 'absolute',
                  top: -40,
                  right: -40,
                  width: 220,
                  height: 80,
                  background: 'rgba(21,101,192,0.04)',
                  borderRadius: '50%',
                  zIndex: 0
                }
              }}
            >
              <Box sx={{ position: 'relative', zIndex: 1 }}>
                <NotificationsIcon sx={{ fontSize: 44, color: '#0d47a1', mb: 1.5, opacity: 0.95 }} />
                <Typography variant="h1" sx={{ fontWeight: 700, color: '#0d47a1', mb: 0.5, fontSize: '2.6rem' }}>
                  {stats.total}
                </Typography>
                <Typography variant="h5" sx={{ color: '#133b6b', fontWeight: 600, fontSize: '1.05rem' }}>
                  Tổng số thông báo
                </Typography>
              </Box>
            </Paper>
          </Grid>
          <Grid size={{ xs: 12, sm: 6, md: 3 }}>
              <Paper
              elevation={2}
              sx={{
                p: 4,
                textAlign: 'center',
                borderRadius: 4,
                background: 'linear-gradient(135deg, #eef6ff 0%, #e8f6ff 100%)',
                border: '1px solid rgba(13,71,161,0.06)',
                minHeight: 160,
                width: '100%',
                display: 'flex',
                flexDirection: 'column',
                justifyContent: 'center',
                position: 'relative',
                overflow: 'hidden',
                boxShadow: '0 12px 30px rgba(13,71,161,0.06)',
                '&:hover': {
                  transform: 'translateY(-6px) scale(1.01)',
                  boxShadow: '0 16px 36px rgba(13,71,161,0.08)'
                },
                '&::before': {
                  content: '""',
                  position: 'absolute',
                  top: -40,
                  right: -40,
                  width: 120,
                  height: 120,
                  background: 'rgba(21,101,192,0.04)',
                  borderRadius: '50%',
                  zIndex: 0
                }
              }}
            >
              <Box sx={{ position: 'relative', zIndex: 1 }}>
                <Badge
                  badgeContent="!"
                  color="primary"
                  sx={{
                    '& .MuiBadge-badge': {
                      fontSize: '0.95rem',
                      fontWeight: 700,
                      width: 24,
                      height: 24,
                      borderRadius: '50%',
                      background: 'linear-gradient(45deg, #1976d2, #42a5f5)',
                      boxShadow: '0 1px 4px rgba(21,101,192,0.08)'
                    }
                  }}
                >
                  <NotificationsIcon sx={{ fontSize: 44, color: '#0d47a1', mb: 1.5, opacity: 0.95 }} />
                </Badge>
                <Typography variant="h1" sx={{ fontWeight: 700, color: '#0d47a1', mb: 0.5, fontSize: '2.6rem' }}>
                  {stats.unread}
                </Typography>
                <Typography variant="h5" sx={{ color: '#133b6b', fontWeight: 600, fontSize: '1.05rem' }}>
                  Chưa đọc
                </Typography>
              </Box>
            </Paper>
          </Grid>
          <Grid size={{ xs: 12, sm: 6, md: 3 }}>
              <Paper
              elevation={2}
              sx={{
                p: 3.5,
                textAlign: 'center',
                borderRadius: 4,
                background: 'linear-gradient(135deg, #eef6ff 0%, #e4f7ff 100%)',
                border: '1px solid rgba(13,71,161,0.06)',
                minHeight: 160,
                width: '100%',
                display: 'flex',
                flexDirection: 'column',
                justifyContent: 'center',
                position: 'relative',
                overflow: 'hidden',
                boxShadow: '0 12px 30px rgba(13,71,161,0.06)',
                '&:hover': {
                  transform: 'translateY(-6px) scale(1.01)',
                  boxShadow: '0 16px 36px rgba(13,71,161,0.08)'
                },
                '&::before': {
                  content: '""',
                  position: 'absolute',
                  top: -40,
                  right: -40,
                  width: 100,
                  height: 100,
                  background: 'rgba(21,101,192,0.04)',
                  borderRadius: '50%',
                  zIndex: 0
                }
              }}
            >
              <Box sx={{ position: 'relative', zIndex: 1 }}>
                <OrdersIcon sx={{ fontSize: 44, color: '#0d47a1', mb: 1.5, opacity: 0.95 }} />
                <Typography variant="h1" sx={{ fontWeight: 700, color: '#0d47a1', mb: 0.5, fontSize: '2.6rem' }}>
                  {stats.byType.orders}
                </Typography>
                <Typography variant="h5" sx={{ color: '#133b6b', fontWeight: 600, fontSize: '1.05rem' }}>
                  Đơn hàng
                </Typography>
              </Box>
            </Paper>
          </Grid>
          <Grid size={{ xs: 12, sm: 6, md: 3 }}>
              <Paper
              elevation={2}
              sx={{
                p: 3.5,
                textAlign: 'center',
                borderRadius: 4,
                background: 'linear-gradient(135deg, #eef6ff 0%, #e6f9ff 100%)',
                border: '1px solid rgba(13,71,161,0.06)',
                minHeight: 160,
                width: '100%',
                display: 'flex',
                flexDirection: 'column',
                justifyContent: 'center',
                position: 'relative',
                overflow: 'hidden',
                boxShadow: '0 12px 30px rgba(13,71,161,0.06)',
                '&:hover': {
                  transform: 'translateY(-6px) scale(1.01)',
                  boxShadow: '0 16px 36px rgba(13,71,161,0.08)'
                },
                '&::before': {
                  content: '""',
                  position: 'absolute',
                  top: -40,
                  right: -40,
                  width: 100,
                  height: 100,
                  background: 'rgba(21,101,192,0.04)',
                  borderRadius: '50%',
                  zIndex: 0
                }
              }}
            >
              <Box sx={{ position: 'relative', zIndex: 1 }}>
                <DeliveriesIcon sx={{ fontSize: 44, color: '#0d47a1', mb: 1.5, opacity: 0.95 }} />
                <Typography variant="h1" sx={{ fontWeight: 700, color: '#0d47a1', mb: 0.5, fontSize: '2.6rem' }}>
                  {stats.byType.deliveries}
                </Typography>
                <Typography variant="h5" sx={{ color: '#133b6b', fontWeight: 600, fontSize: '1.05rem' }}>
                  Giao hàng
                </Typography>
              </Box>
            </Paper>
          </Grid>
        </Grid>
      )}

      {/* Actions Bar */}
      <Paper
        elevation={4}
        sx={{
          p: 6,
          mb: 5,
          borderRadius: 5,
          background: 'linear-gradient(135deg, #fafafa, #f5f5f5)',
          border: '3px solid rgba(0,0,0,0.15)',
          boxShadow: '0 12px 30px rgba(0,0,0,0.15)',
          minHeight: 120
        }}
      >
        <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', flexWrap: 'wrap', gap: 2 }}>
          <Box sx={{ display: 'flex', gap: 2, alignItems: 'center' }}>
            <Typography variant="h6" sx={{ fontWeight: 'bold' }}>
              Bộ lọc:
            </Typography>

            <ToggleButtonGroup
              value={activeFilter}
              exclusive
              onChange={(e, val) => { if (val !== null) setActiveFilter(val) }}
              aria-label="Bộ lọc thông báo"
              size="medium"
              sx={{
                ml: 1,
                borderRadius: 0,
                '& .MuiToggleButtonGroup-grouped': {
                  margin: 0,
                  border: 'none'
                },
                '& .MuiToggleButton-root': {
                  textTransform: 'none'
                }
              }}
            >
              {notificationTypes.map((type) => (
                <ToggleButton
                  key={type.value}
                  value={type.value}
                  aria-label={type.label}
                  sx={{
                    borderRadius: 0,
                    px: 2,
                    py: 1,
                    mr: 1,
                    minWidth: 90,
                    display: 'flex',
                    alignItems: 'center',
                    gap: 1,
                    border: 'none',
                    '&.Mui-selected': {
                      bgcolor: 'primary.main',
                      color: 'common.white',
                      boxShadow: '0 8px 20px rgba(21,101,192,0.12)',
                      '&:hover': {
                        bgcolor: 'primary.dark'
                      }
                    },
                    '&:hover': {
                      bgcolor: 'rgba(13,71,161,0.04)'
                    }
                  }}
                >
                  <Box component="span" sx={{ fontSize: 18 }}>{type.icon}</Box>
                  <Typography sx={{ fontWeight: 600, fontSize: '0.95rem' }}>{type.label}</Typography>
                </ToggleButton>
              ))}
            </ToggleButtonGroup>
          </Box>

          <Box sx={{ display: 'flex', gap: 2 }}>
            <Button
              variant="contained"
              startIcon={markingAll ? <CircularProgress size={20} /> : <MarkAllReadIcon />}
              onClick={handleMarkAllAsRead}
              disabled={markingAll || stats?.unread === 0}
              sx={{
                borderRadius: 2,
                textTransform: 'none',
                fontWeight: 'bold',
                background: 'linear-gradient(45deg, #4caf50, #66bb6a)',
                '&:hover': {
                  background: 'linear-gradient(45deg, #388e3c, #4caf50)',
                  transform: 'translateY(-1px)'
                },
                transition: 'all 0.2s ease'
              }}
            >
              {markingAll ? 'Đang xử lý...' : 'Đánh dấu tất cả đã đọc'}
            </Button>

            <Button
              variant="outlined"
              startIcon={<PreferencesIcon />}
              onClick={() => navigate('/notifications/preferences')}
              sx={{
                borderRadius: 2,
                textTransform: 'none',
                fontWeight: 'bold',
                borderColor: '#1976d2',
                color: '#1976d2',
                '&:hover': {
                  borderColor: '#1565c0',
                  backgroundColor: 'rgba(25, 118, 210, 0.04)',
                  transform: 'translateY(-1px)'
                },
                transition: 'all 0.2s ease'
              }}
            >
              Tùy chọn
            </Button>
          </Box>
        </Box>
      </Paper>

      {/* Notifications List */}
      <Card
        elevation={3}
        sx={{
          borderRadius: 3,
          overflow: 'hidden',
          background: 'linear-gradient(135deg, #ffffff 0%, #fafafa 100%)',
          border: '1px solid rgba(0,0,0,0.08)'
        }}
      >
        <CardContent sx={{ p: 0 }}>
          {filteredNotifications.length === 0 ? (
            <Box sx={{ textAlign: 'center', py: 8, px: 3 }}>
              <NotificationsIcon sx={{ fontSize: 80, color: 'text.disabled', mb: 2 }} />
              <Typography variant="h6" color="text.secondary" sx={{ mb: 1 }}>
                Không có thông báo nào
              </Typography>
              <Typography variant="body2" color="text.secondary">
                {activeFilter === 'all'
                  ? 'Bạn chưa có thông báo nào.'
                  : `Không có thông báo loại ${notificationTypes.find(t => t.value === activeFilter)?.label.toLowerCase()}.`
                }
              </Typography>
            </Box>
          ) : (
            <List sx={{ py: 0 }}>
              {filteredNotifications.map((notification, index) => (
                <Fade in={true} timeout={300 + index * 100} key={notification.id}>
                  <div>
                    <ListItem
                      sx={{
                        py: 3,
                        px: 4,
                        backgroundColor: notification.isRead ? 'transparent' : 'rgba(25, 118, 210, 0.02)',
                        borderLeft: notification.isRead ? 'none' : `4px solid ${getNotificationColor(notification.type)}`,
                        '&:hover': {
                          backgroundColor: notification.isRead ? 'rgba(0,0,0,0.02)' : 'rgba(25, 118, 210, 0.04)',
                          transform: 'translateX(4px)',
                          transition: 'all 0.2s ease'
                        },
                        transition: 'all 0.2s ease'
                      }}
                    >
                      <ListItemAvatar>
                        <Badge
                          variant="dot"
                          color="error"
                          invisible={notification.isRead}
                          sx={{
                            '& .MuiBadge-badge': {
                              width: 12,
                              height: 12,
                              borderRadius: '50%',
                              border: '2px solid white'
                            }
                          }}
                        >
                          <Avatar
                            sx={{
                              bgcolor: getNotificationColor(notification.type),
                              width: 50,
                              height: 50,
                              boxShadow: '0 4px 12px rgba(0,0,0,0.15)'
                            }}
                          >
                            {getNotificationIcon(notification.type)}
                          </Avatar>
                        </Badge>
                      </ListItemAvatar>

                      <ListItemText
                        primary={
                          <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, mb: 1 }}>
                            <Box
                              component="span"
                              sx={{
                                fontWeight: notification.isRead ? 'normal' : 'bold',
                                color: notification.isRead ? 'text.primary' : 'text.primary',
                                fontSize: '1.1rem'
                              }}
                            >
                              {notification.title}
                            </Box>
                            <Chip
                              label={notificationTypes.find(t => t.value === notification.type)?.label}
                              size="small"
                              sx={{
                                bgcolor: getNotificationColor(notification.type),
                                color: 'white',
                                fontSize: '0.75rem',
                                fontWeight: 'bold',
                                textTransform: 'uppercase',
                                letterSpacing: 0.5
                              }}
                            />
                          </Box>
                        }
                        secondary={
                          <Box component="div">
                            <Box
                              component="div"
                              sx={{
                                color: 'text.primary',
                                mb: 1,
                                fontSize: '1rem'
                              }}
                            >
                              {notification.message}
                            </Box>
                            <Box
                              component="span"
                              sx={{
                                color: 'text.secondary',
                                fontSize: '0.875rem'
                              }}
                            >
                              {formatTimeAgo(notification.createdAt)}
                            </Box>
                          </Box>
                        }
                      />

                      <ListItemSecondaryAction>
                        <Box sx={{ display: 'flex', gap: 1 }}>
                          <Tooltip title={notification.isRead ? 'Đánh dấu chưa đọc' : 'Đánh dấu đã đọc'}>
                            <IconButton
                              onClick={() => handleMarkAsRead(notification.id, !notification.isRead)}
                              sx={{
                                color: notification.isRead ? '#757575' : '#1976d2',
                                '&:hover': {
                                  backgroundColor: notification.isRead ? 'rgba(117, 117, 117, 0.1)' : 'rgba(25, 118, 210, 0.1)',
                                  transform: 'scale(1.1)'
                                },
                                transition: 'all 0.2s ease'
                              }}
                            >
                              {notification.isRead ? <UnreadIcon /> : <ReadIcon />}
                            </IconButton>
                          </Tooltip>

                          <Tooltip title="Xóa thông báo">
                            <IconButton
                              onClick={() => handleDeleteNotification(notification.id)}
                              sx={{
                                color: '#f44336',
                                '&:hover': {
                                  backgroundColor: 'rgba(244, 67, 54, 0.1)',
                                  transform: 'scale(1.1)'
                                },
                                transition: 'all 0.2s ease'
                              }}
                            >
                              <DeleteIcon />
                            </IconButton>
                          </Tooltip>
                        </Box>
                      </ListItemSecondaryAction>
                    </ListItem>
                    {index < filteredNotifications.length - 1 && (
                      <Divider sx={{ mx: 4, borderColor: 'rgba(0,0,0,0.06)' }} />
                    )}
                  </div>
                </Fade>
              ))}
            </List>
          )}
        </CardContent>
      </Card>

      {/* Floating Action Button for Quick Actions */}
      <Fab
        color="primary"
        sx={{
          position: 'fixed',
          bottom: 24,
          right: 24,
          background: 'linear-gradient(45deg, #1976d2, #42a5f5)',
          '&:hover': {
            background: 'linear-gradient(45deg, #1565c0, #1976d2)',
            transform: 'scale(1.1)'
          },
          transition: 'all 0.3s ease'
        }}
        onClick={handleMarkAllAsRead}
        disabled={markingAll || stats?.unread === 0}
      >
        <MarkAllReadIcon />
      </Fab>
      </Container>
    </Box>
  )
}

export default Notifications

