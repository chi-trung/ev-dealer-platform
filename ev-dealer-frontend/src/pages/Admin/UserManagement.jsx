import { useState, useEffect } from 'react';
import {
  Box,
  Typography,
  CircularProgress,
  Alert,
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TableRow,
  Paper,
  Button,
  Chip,
  Stack,
  Dialog,
  DialogTitle,
  DialogContent,
  DialogActions,
  TextField,
  Select,
  MenuItem,
  InputLabel,
  FormControl,
  InputAdornment,
  IconButton,
} from '@mui/material';
import Visibility from '@mui/icons-material/Visibility';
import VisibilityOff from '@mui/icons-material/VisibilityOff';
import adminService from '../../services/adminService';

const UserManagement = () => {
  const [users, setUsers] = useState([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [success, setSuccess] = useState('');
  const [openCreateUserModal, setOpenCreateUserModal] = useState(false);
  const [newUserData, setNewUserData] = useState({
    email: '',
    fullName: '',
    password: '',
    confirmPassword: '',
    role: 'DealerStaff',
    dealerId: null,
  });
  const [, setDealers] = useState([]); // Still fetch dealers for context, but not for dropdown
  const [showPassword, setShowPassword] = useState(false);
  const [showConfirmPassword, setShowConfirmPassword] = useState(false);
  const [passwordError, setPasswordError] = useState('');
  const [confirmPasswordError, setConfirmPasswordError] = useState('');

  const fetchUsers = async () => {
    try {
      setLoading(true);
      const data = await adminService.getUsers();
      setUsers(data.users || []);
    } catch {
      setError('Không thể tải danh sách người dùng.');
    } finally {
      setLoading(false);
    }
  };

  // We still fetch dealers to potentially validate the entered dealerId,
  // even if not using a dropdown for input.
  const fetchDealers = async () => {
    try {
      const data = await adminService.getDealers();
      setDealers(data || []);
    } catch (err) {
      console.error('Không thể tải danh sách đại lý:', err);
    }
  };

  useEffect(() => {
    fetchUsers();
    fetchDealers();
  }, []);

  const handleApprove = async (userId) => {
    try {
      setError('');
      setSuccess('');
      await adminService.approveUser(userId);
      setSuccess('Duyệt người dùng thành công!');
      fetchUsers();
    } catch {
      setError('Duyệt người dùng thất bại.');
    }
  };

  const handleReject = async (userId) => {
    if (window.confirm('Bạn có chắc chắn muốn từ chối và xóa người dùng này không?')) {
      try {
        setError('');
        setSuccess('');
        await adminService.rejectUser(userId);
        setSuccess('Đã từ chối và xóa người dùng.');
        fetchUsers();
      } catch {
        setError('Từ chối người dùng thất bại.');
      }
    }
  };

  const handleInputChange = (e) => {
    const { name, value } = e.target;
    setNewUserData((prev) => {
      let updatedValue = value;
      if (name === 'dealerId') {
        // Allow empty string for optional dealerId, convert to null for API
        updatedValue = value === '' ? null : parseInt(value, 10);
        if (isNaN(updatedValue)) updatedValue = null; // Ensure it's null if not a valid number
      }
      return { ...prev, [name]: updatedValue };
    });

    // Clear password errors on input change
    if (name === 'password') setPasswordError('');
    if (name === 'confirmPassword') setConfirmPasswordError('');
  };

  const handleRoleChange = (e) => {
    const { value } = e.target;
    setNewUserData((prev) => ({
      ...prev,
      role: value,
      dealerId: (value === 'DealerStaff' || value === 'DealerManager') ? prev.dealerId : null, // Reset dealerId if role changes to non-dealer
    }));
  };

  const handleClickShowPassword = () => setShowPassword((show) => !show);
  const handleClickShowConfirmPassword = () => setShowConfirmPassword((show) => !show);
  const handleMouseDownPassword = (event) => { event.preventDefault(); }; // Prevent focus loss

  const handleCreateUser = async () => {
    setError('');
    setSuccess('');
    setPasswordError('');
    setConfirmPasswordError('');

    if (!newUserData.email || !newUserData.fullName || !newUserData.password || !newUserData.confirmPassword) {
      setError('Vui lòng điền đầy đủ tất cả các trường bắt buộc.');
      return;
    }

    if (newUserData.password !== newUserData.confirmPassword) {
      setConfirmPasswordError('Mật khẩu xác nhận không khớp.');
      return;
    }
    if (newUserData.password.length < 6) {
      setPasswordError('Mật khẩu phải có ít nhất 6 ký tự.');
      return;
    }

    // Prepare data for backend, using email as username
    const dataToSend = {
      username: newUserData.email, // Use email as username as per request
      email: newUserData.email,
      fullName: newUserData.fullName,
      password: newUserData.password,
      role: newUserData.role,
      dealerId: newUserData.dealerId,
    };

    try {
      await adminService.createApprovedUser(dataToSend);
      setSuccess('Tạo người dùng thành công và đã được kích hoạt!');
      setOpenCreateUserModal(false);
      setNewUserData({
        email: '',
        fullName: '',
        password: '',
        confirmPassword: '',
        role: 'DealerStaff',
        dealerId: null,
      });
      fetchUsers();
    } catch (err) {
      setError('Tạo người dùng thất bại: ' + (err.response?.data?.message || err.message));
    }
  };

  if (loading) {
    return <CircularProgress />;
  }

  return (
    <Box>
      <Typography variant="h4" gutterBottom>
        Quản lý người dùng
      </Typography>

      {error && <Alert severity="error" sx={{ mb: 2 }}>{error}</Alert>}
      {success && <Alert severity="success" sx={{ mb: 2 }}>{success}</Alert>}

      <Button
        variant="contained"
        color="primary"
        onClick={() => {
          setOpenCreateUserModal(true);
          // Reset form data when opening the modal
          setNewUserData({
            email: '',
            fullName: '',
            password: '',
            confirmPassword: '',
            role: 'DealerStaff',
            dealerId: null,
          });
          setError('');
          setSuccess('');
          setPasswordError('');
          setConfirmPasswordError('');
        }}
        sx={{ mb: 2 }}
      >
        Tạo người dùng mới
      </Button>

      <TableContainer component={Paper}>
        <Table>
          <TableHead>
            <TableRow>
              <TableCell>ID</TableCell>
              <TableCell>Tên</TableCell>
              <TableCell>Email</TableCell>
              <TableCell>Vai trò</TableCell>
              <TableCell>Trạng thái</TableCell>
              <TableCell align="center">Hành động</TableCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {users.map((user) => (
              <TableRow key={user.id}>
                <TableCell>{user.id}</TableCell>
                <TableCell>{user.fullName}</TableCell>
                <TableCell>{user.email}</TableCell>
                <TableCell>{user.role}</TableCell>
                <TableCell>
                  <Chip
                    label={user.isActive ? 'Đang hoạt động' : 'Chờ duyệt'}
                    color={user.isActive ? 'success' : 'warning'}
                    size="small"
                  />
                </TableCell>
                <TableCell align="center">
                  {!user.isActive && (
                    <Stack direction="row" spacing={1} justifyContent="center">
                      <Button
                        variant="contained"
                        color="success"
                        size="small"
                        onClick={() => handleApprove(user.id)}
                      >
                        Duyệt
                      </Button>
                      <Button
                        variant="contained"
                        color="error"
                        size="small"
                        onClick={() => handleReject(user.id)}
                      >
                        Từ chối
                      </Button>
                    </Stack>
                  )}
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </TableContainer>

      {/* Create User Modal */}
      <Dialog open={openCreateUserModal} onClose={() => setOpenCreateUserModal(false)}>
        <DialogTitle>Tạo người dùng mới</DialogTitle>
        <DialogContent>
          <TextField
            autoFocus
            margin="dense"
            name="email"
            label="Email (Tên đăng nhập)"
            type="email"
            fullWidth
            variant="standard"
            value={newUserData.email}
            onChange={handleInputChange}
            sx={{ mb: 2 }}
          />
          <TextField
            margin="dense"
            name="fullName"
            label="Họ và tên"
            type="text"
            fullWidth
            variant="standard"
            value={newUserData.fullName}
            onChange={handleInputChange}
            sx={{ mb: 2 }}
          />
          <TextField
            margin="dense"
            name="password"
            label="Mật khẩu"
            type={showPassword ? 'text' : 'password'}
            fullWidth
            variant="standard"
            value={newUserData.password}
            onChange={handleInputChange}
            error={!!passwordError}
            helperText={passwordError}
            InputProps={{
              endAdornment: (
                <InputAdornment position="end">
                  <IconButton
                    aria-label="toggle password visibility"
                    onClick={handleClickShowPassword}
                    onMouseDown={handleMouseDownPassword}
                    edge="end"
                  >
                    {showPassword ? <VisibilityOff /> : <Visibility />}
                  </IconButton>
                </InputAdornment>
              ),
            }}
            sx={{ mb: 2 }}
          />
          <TextField
            margin="dense"
            name="confirmPassword"
            label="Xác nhận mật khẩu"
            type={showConfirmPassword ? 'text' : 'password'}
            fullWidth
            variant="standard"
            value={newUserData.confirmPassword}
            onChange={handleInputChange}
            error={!!confirmPasswordError}
            helperText={confirmPasswordError}
            InputProps={{
              endAdornment: (
                <InputAdornment position="end">
                  <IconButton
                    aria-label="toggle confirm password visibility"
                    onClick={handleClickShowConfirmPassword}
                    onMouseDown={handleMouseDownPassword}
                    edge="end"
                  >
                    {showConfirmPassword ? <VisibilityOff /> : <Visibility />}
                  </IconButton>
                </InputAdornment>
              ),
            }}
            sx={{ mb: 2 }}
          />
          <FormControl fullWidth margin="dense" variant="standard" sx={{ mb: 2 }}>
            <InputLabel id="role-label">Vai trò</InputLabel>
            <Select
              labelId="role-label"
              name="role"
              value={newUserData.role}
              label="Vai trò"
              onChange={handleRoleChange}
            >
              <MenuItem value="DealerStaff">DealerStaff</MenuItem>
              <MenuItem value="DealerManager">DealerManager</MenuItem>
              <MenuItem value="EVMStaff">EVMStaff</MenuItem>
              <MenuItem value="Admin">Admin</MenuItem>
            </Select>
          </FormControl>
          {(newUserData.role === 'DealerStaff' || newUserData.role === 'DealerManager') && (
            <TextField
              margin="dense"
              name="dealerId"
              label="ID Đại lý (tùy chọn)"
              type="number"
              fullWidth
              variant="standard"
              value={newUserData.dealerId === null ? '' : newUserData.dealerId}
              onChange={handleInputChange}
              sx={{ mb: 2 }}
              helperText="Để trống nếu không có ID đại lý"
            />
          )}
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setOpenCreateUserModal(false)}>Hủy</Button>
          <Button onClick={handleCreateUser}>Tạo</Button>
        </DialogActions>
      </Dialog>
    </Box>
  );
};

export default UserManagement;