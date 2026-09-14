/**
 * Settings Page
 * TODO: Implement settings with tabs:
 * - Profile (name, email, phone, avatar)
 * - Change Password
 * - Role & Permissions (Admin only)
 * - System Settings (Admin only)
 */

import React, { useState } from "react";
import api from "../../services/api";
import authService from "../../services/authService";
import {
  Container,
  Box,
  Grid,
  Paper,
  Typography,
  Avatar,
  TextField,
  Button,
  Tabs,
  Tab,
  Card,
  CardContent,
  Divider,
  Alert,
  Switch,
  FormControlLabel,
  useTheme,
  useMediaQuery,
  Chip,
  Stack,
} from "@mui/material";
import {
  Person,
  Lock,
  Security,
  Save,
  Key,
  Email,
  Phone,
  Upload,
} from "@mui/icons-material";
import { PageHeader } from "../../components/common";

const TabPanel = ({ children, value, index, ...other }) => (
  <div
    role="tabpanel"
    hidden={value !== index}
    id={`settings-tabpanel-${index}`}
    aria-labelledby={`settings-tab-${index}`}
    {...other}
  >
    {value === index && <Box sx={{ pt: 4 }}>{children}</Box>}
  </div>
);

const Settings = () => {
  const [activeTab, setActiveTab] = useState(0);
  const [isSubmitting, setIsSubmitting] = useState(false);
  const theme = useTheme();
  const isMobile = useMediaQuery(theme.breakpoints.down("md"));

  const handleTabChange = (event, newValue) => {
    setActiveTab(newValue);
  };

  const handleProfileSubmit = async (e) => {
    e.preventDefault();
    setIsSubmitting(true);
    const form = e.target;
    const fd = new FormData();
    fd.append("fullName", form.fullName?.value || "");
    fd.append("email", form.email?.value || "");
    fd.append("phone", form.phone?.value || "");
    const avatarFile = form.avatar?.files && form.avatar.files[0];
    if (avatarFile) fd.append("avatar", avatarFile);

    try {
      await api.put("/users/me", fd);
      alert("Cập nhật profile thành công");
    } catch (err) {
      alert("Cập nhật profile thất bại: " + (err || ""));
    } finally {
      setIsSubmitting(false);
    }
  };

  const handlePasswordSubmit = async (e) => {
    e.preventDefault();
    setIsSubmitting(true);
    const form = e.target;
    const currentPassword = form.currentPassword?.value || "";
    const newPassword = form.newPassword?.value || "";
    const confirmPassword = form.confirmPassword?.value || "";

    if (!newPassword || newPassword !== confirmPassword) {
      alert("Mật khẩu mới không khớp hoặc trống");
      setIsSubmitting(false);
      return;
    }

    try {
      await api.post("/auth/change-password", { currentPassword, newPassword });
      alert("Đổi mật khẩu thành công");
      form.reset();
    } catch (err) {
      // Issue #50: the backend answers failures with {success,message} (400) or
      // nothing (401 — session expired, which the api interceptor already
      // redirects to /login). The old string-concat showed "[object Object]".
      const reason = err?.response?.data?.message || err?.message || "lỗi không xác định";
      alert("Đổi mật khẩu thất bại: " + reason);
    } finally {
      setIsSubmitting(false);
    }
  };

  const handleSaveRoles = async (e) => {
    e.preventDefault();
    setIsSubmitting(true);
    try {
      alert("Lưu quyền (stub)");
    } finally {
      setIsSubmitting(false);
    }
  };

  const user = authService.getCurrentUser();

  return (
    <Container maxWidth="xl" sx={{ py: 4 }}>
      <PageHeader
        title="Cài đặt"
        subtitle="Quản lý thông tin người dùng và quyền truy cập"
      />

      <Grid container spacing={4}>
        {/* Main Content */}
        <Grid item xs={12}>
          <Paper
            sx={{
              p: { xs: 3, md: 6 },
              borderRadius: 4,
              boxShadow: 3,
              background: theme.palette.background.paper,
              minHeight: "70vh",
            }}
          >
            {/* User Info Header */}
            <Box sx={{ display: "flex", alignItems: "center", mb: 6, gap: 4 }}>
              <Avatar
                sx={{
                  width: 100,
                  height: 100,
                  fontSize: "2.5rem",
                  bgcolor: "primary.main",
                  border: `4px solid ${theme.palette.primary.light}`,
                }}
                src={user?.avatar}
              >
                {(user?.fullName || "U").charAt(0).toUpperCase()}
              </Avatar>
              <Box sx={{ flex: 1 }}>
                <Typography variant="h3" sx={{ fontWeight: 700, mb: 2 }}>
                  {user?.fullName || "Người dùng"}
                </Typography>
                <Typography variant="h5" color="text.secondary" sx={{ mb: 2 }}>
                  {user?.email}
                </Typography>
                <Chip
                  label={user?.role || "Người dùng"}
                  color="primary"
                  sx={{
                    fontSize: "1rem",
                    padding: "8px 16px",
                    height: "auto",
                  }}
                />
              </Box>
            </Box>

            <Divider sx={{ mb: 4 }} />

            {/* Tabs - Larger and more spaced */}
            <Box
              sx={{
                borderBottom: 2,
                borderColor: "divider",
                mb: 6,
                "& .MuiTabs-indicator": {
                  height: 4,
                  borderRadius: "2px 2px 0 0",
                },
              }}
            >
              <Tabs
                value={activeTab}
                onChange={handleTabChange}
                variant={isMobile ? "scrollable" : "fullWidth"}
                scrollButtons="auto"
                sx={{
                  "& .MuiTab-root": {
                    py: 3,
                    px: 4,
                    fontSize: "1.1rem",
                    fontWeight: 600,
                    minHeight: "70px",
                    minWidth: { xs: "160px", md: "auto" },
                    "&.Mui-selected": {
                      color: "primary.main",
                    },
                  },
                }}
              >
                <Tab
                  icon={<Person sx={{ fontSize: "1.5rem" }} />}
                  iconPosition="start"
                  label="Hồ sơ cá nhân"
                />
                <Tab
                  icon={<Lock sx={{ fontSize: "1.5rem" }} />}
                  iconPosition="start"
                  label="Bảo mật & Mật khẩu"
                />
                <Tab
                  icon={<Security sx={{ fontSize: "1.5rem" }} />}
                  iconPosition="start"
                  label="Phân quyền hệ thống"
                />
              </Tabs>
            </Box>

            {/* Tab Content - More spacious */}
            <Box sx={{ width: "100%" }}>
              {/* Tab 1: Profile */}
              <TabPanel value={activeTab} index={0}>
                <Box sx={{ maxWidth: "800px", mx: "auto" }}>
                  <Typography
                    variant="h4"
                    sx={{
                      mb: 4,
                      fontWeight: 600,
                      color: "primary.main",
                    }}
                  >
                    <Person
                      sx={{ mr: 3, verticalAlign: "middle", fontSize: "2rem" }}
                    />
                    Cập nhật thông tin cá nhân
                  </Typography>

                  <form onSubmit={handleProfileSubmit}>
                    <Box
                      sx={{
                        display: "flex",
                        flexDirection: "column",
                        gap: 4,
                        mb: 6,
                      }}
                    >
                      <TextField
                        name="fullName"
                        label="Họ và tên"
                        defaultValue={user?.fullName || ""}
                        fullWidth
                        variant="outlined"
                        size="medium"
                        InputProps={{
                          startAdornment: (
                            <Person
                              sx={{
                                mr: 2,
                                color: "text.secondary",
                                fontSize: "1.5rem",
                              }}
                            />
                          ),
                          sx: { fontSize: "1.1rem", py: 1 },
                        }}
                        sx={{
                          "& .MuiOutlinedInput-root": {
                            borderRadius: 2,
                          },
                        }}
                      />
                      <TextField
                        name="email"
                        type="email"
                        label="Địa chỉ email"
                        defaultValue={user?.email || ""}
                        fullWidth
                        variant="outlined"
                        size="medium"
                        InputProps={{
                          startAdornment: (
                            <Email
                              sx={{
                                mr: 2,
                                color: "text.secondary",
                                fontSize: "1.5rem",
                              }}
                            />
                          ),
                          sx: { fontSize: "1.1rem", py: 1 },
                        }}
                        sx={{
                          "& .MuiOutlinedInput-root": {
                            borderRadius: 2,
                          },
                        }}
                      />
                      <TextField
                        name="phone"
                        type="tel"
                        label="Số điện thoại"
                        defaultValue={user?.phone || ""}
                        fullWidth
                        variant="outlined"
                        size="medium"
                        InputProps={{
                          startAdornment: (
                            <Phone
                              sx={{
                                mr: 2,
                                color: "text.secondary",
                                fontSize: "1.5rem",
                              }}
                            />
                          ),
                          sx: { fontSize: "1.1rem", py: 1 },
                        }}
                        sx={{
                          "& .MuiOutlinedInput-root": {
                            borderRadius: 2,
                          },
                        }}
                      />

                      <Card
                        sx={{
                          border: `3px dashed ${theme.palette.primary.main}`,
                          borderRadius: 3,
                          p: 4,
                          textAlign: "center",
                          backgroundColor: "action.hover",
                          mt: 2,
                        }}
                      >
                        <Typography
                          variant="h5"
                          sx={{ mb: 3, fontWeight: 600 }}
                        >
                          <Upload
                            sx={{
                              mr: 2,
                              verticalAlign: "middle",
                              fontSize: "2rem",
                            }}
                          />
                          Ảnh đại diện
                        </Typography>
                        <Box
                          component="input"
                          name="avatar"
                          type="file"
                          accept="image/*"
                          sx={{
                            width: "100%",
                            p: 3,
                            "::file-selector-button": {
                              px: 4,
                              py: 2,
                              border: "none",
                              borderRadius: 2,
                              backgroundColor: "primary.main",
                              color: "white",
                              cursor: "pointer",
                              mr: 3,
                              fontWeight: 600,
                              fontSize: "1rem",
                              "&:hover": {
                                backgroundColor: "primary.dark",
                              },
                            },
                          }}
                        />
                        <Typography
                          variant="body1"
                          color="text.secondary"
                          sx={{ mt: 2 }}
                        >
                          Chọn file ảnh với định dạng JPG, PNG hoặc GIF (tối đa
                          5MB)
                        </Typography>
                      </Card>
                    </Box>

                    <Box
                      sx={{
                        display: "flex",
                        gap: 3,
                        flexWrap: "wrap",
                      }}
                    >
                      <Button
                        type="submit"
                        variant="contained"
                        startIcon={<Save sx={{ fontSize: "1.5rem" }} />}
                        disabled={isSubmitting}
                        sx={{
                          px: 6,
                          py: 2,
                          borderRadius: 2,
                          fontWeight: 600,
                          fontSize: "1.1rem",
                          minWidth: "200px",
                        }}
                      >
                        {isSubmitting ? "Đang cập nhật..." : "Lưu thay đổi"}
                      </Button>
                      <Button
                        variant="outlined"
                        sx={{
                          px: 6,
                          py: 2,
                          borderRadius: 2,
                          fontWeight: 600,
                          fontSize: "1.1rem",
                          minWidth: "200px",
                        }}
                      >
                        Hủy bỏ
                      </Button>
                    </Box>
                  </form>
                </Box>
              </TabPanel>

              {/* Tab 2: Security */}
              <TabPanel value={activeTab} index={1}>
                <Box sx={{ maxWidth: "800px", mx: "auto" }}>
                  <Typography
                    variant="h4"
                    sx={{
                      mb: 4,
                      fontWeight: 600,
                      color: "primary.main",
                    }}
                  >
                    <Key
                      sx={{ mr: 3, verticalAlign: "middle", fontSize: "2rem" }}
                    />
                    Quản lý bảo mật
                  </Typography>

                  <Alert
                    severity="info"
                    sx={{ mb: 5, fontSize: "1.1rem", py: 2 }}
                  >
                    Mật khẩu mới phải có ít nhất 8 ký tự, bao gồm chữ hoa, chữ
                    thường, số và ký tự đặc biệt.
                  </Alert>

                  <form onSubmit={handlePasswordSubmit}>
                    <Box
                      sx={{
                        display: "flex",
                        flexDirection: "column",
                        gap: 4,
                        mb: 6,
                      }}
                    >
                      <TextField
                        name="currentPassword"
                        type="password"
                        label="Mật khẩu hiện tại"
                        fullWidth
                        variant="outlined"
                        size="medium"
                        required
                        InputProps={{
                          startAdornment: (
                            <Lock
                              sx={{
                                mr: 2,
                                color: "text.secondary",
                                fontSize: "1.5rem",
                              }}
                            />
                          ),
                          sx: { fontSize: "1.1rem", py: 1 },
                        }}
                        sx={{
                          "& .MuiOutlinedInput-root": {
                            borderRadius: 2,
                          },
                        }}
                      />
                      <TextField
                        name="newPassword"
                        type="password"
                        label="Mật khẩu mới"
                        fullWidth
                        variant="outlined"
                        size="medium"
                        required
                        InputProps={{
                          startAdornment: (
                            <Key
                              sx={{
                                mr: 2,
                                color: "text.secondary",
                                fontSize: "1.5rem",
                              }}
                            />
                          ),
                          sx: { fontSize: "1.1rem", py: 1 },
                        }}
                        sx={{
                          "& .MuiOutlinedInput-root": {
                            borderRadius: 2,
                          },
                        }}
                      />
                      <TextField
                        name="confirmPassword"
                        type="password"
                        label="Xác nhận mật khẩu mới"
                        fullWidth
                        variant="outlined"
                        size="medium"
                        required
                        InputProps={{
                          startAdornment: (
                            <Key
                              sx={{
                                mr: 2,
                                color: "text.secondary",
                                fontSize: "1.5rem",
                              }}
                            />
                          ),
                          sx: { fontSize: "1.1rem", py: 1 },
                        }}
                        sx={{
                          "& .MuiOutlinedInput-root": {
                            borderRadius: 2,
                          },
                        }}
                      />
                    </Box>

                    <Card
                      sx={{
                        border: `3px dashed ${theme.palette.primary.main}`,
                        borderRadius: 3,
                        p: 4,
                        textAlign: "center",
                        backgroundColor: "action.hover",
                        mb: 6,
                      }}
                    >
                      <Typography variant="h5" sx={{ mb: 3, fontWeight: 600 }}>
                        <Security
                          sx={{
                            mr: 2,
                            verticalAlign: "middle",
                            fontSize: "2rem",
                          }}
                        />
                        Xác thực hai yếu tố (2FA)
                      </Typography>
                      <Typography
                        variant="body1"
                        color="text.secondary"
                        sx={{ mb: 3 }}
                      >
                        Bảo vệ tài khoản của bạn bằng cách kích hoạt xác thực
                        hai yếu tố
                      </Typography>
                      <Button
                        variant="contained"
                        color="primary"
                        startIcon={<Security sx={{ fontSize: "1.5rem" }} />}
                        sx={{
                          px: 4,
                          py: 2,
                          borderRadius: 2,
                          fontWeight: 600,
                          fontSize: "1.1rem",
                        }}
                      >
                        Thiết lập xác thực 2FA
                      </Button>
                    </Card>

                    <Box
                      sx={{
                        display: "flex",
                        gap: 3,
                        flexWrap: "wrap",
                      }}
                    >
                      <Button
                        type="submit"
                        variant="contained"
                        startIcon={<Save sx={{ fontSize: "1.5rem" }} />}
                        disabled={isSubmitting}
                        sx={{
                          px: 6,
                          py: 2,
                          borderRadius: 2,
                          fontWeight: 600,
                          fontSize: "1.1rem",
                          minWidth: "200px",
                        }}
                      >
                        {isSubmitting ? "Đang xử lý..." : "Cập nhật mật khẩu"}
                      </Button>
                      <Button
                        variant="outlined"
                        sx={{
                          px: 6,
                          py: 2,
                          borderRadius: 2,
                          fontWeight: 600,
                          fontSize: "1.1rem",
                          minWidth: "200px",
                        }}
                      >
                        Hủy bỏ
                      </Button>
                    </Box>
                  </form>
                </Box>
              </TabPanel>

              {/* Tab 3: Permissions */}
              <TabPanel value={activeTab} index={2}>
                <Box sx={{ maxWidth: "800px", mx: "auto" }}>
                  <Typography
                    variant="h4"
                    sx={{
                      mb: 4,
                      fontWeight: 600,
                      color: "primary.main",
                    }}
                  >
                    <Security
                      sx={{ mr: 3, verticalAlign: "middle", fontSize: "2rem" }}
                    />
                    Quản lý phân quyền hệ thống
                  </Typography>

                  {!user || user.role !== "Admin" ? (
                    <Alert
                      severity="warning"
                      sx={{ mb: 4, fontSize: "1.1rem", py: 2 }}
                    >
                      Bạn không có quyền truy cập vào phần quản lý quyền.
                    </Alert>
                  ) : (
                    <form onSubmit={handleSaveRoles}>
                      <Alert
                        severity="info"
                        sx={{ mb: 5, fontSize: "1.1rem", py: 2 }}
                      >
                        Quản lý vai trò và quyền truy cập cho người dùng hệ
                        thống.
                      </Alert>

                      <Card sx={{ mb: 5, p: 4, borderRadius: 3 }}>
                        <Typography
                          variant="h5"
                          sx={{ mb: 3, fontWeight: 600 }}
                        >
                          Quyền quản trị
                        </Typography>
                        <Box
                          sx={{
                            display: "flex",
                            flexDirection: "column",
                            gap: 3,
                          }}
                        >
                          <FormControlLabel
                            control={
                              <Switch
                                name="manageUsers"
                                defaultChecked
                                color="primary"
                                sx={{ "& .MuiSwitch-switchBase": { mr: 1 } }}
                              />
                            }
                            label={
                              <Box>
                                <Typography
                                  variant="h6"
                                  fontWeight={600}
                                  sx={{ mb: 1 }}
                                >
                                  Quản lý người dùng
                                </Typography>
                                <Typography
                                  variant="body1"
                                  color="text.secondary"
                                >
                                  Cho phép thêm, sửa, xóa người dùng hệ thống
                                </Typography>
                              </Box>
                            }
                            sx={{ alignItems: "flex-start" }}
                          />
                          <FormControlLabel
                            control={
                              <Switch
                                name="manageSettings"
                                color="primary"
                                sx={{ "& .MuiSwitch-switchBase": { mr: 1 } }}
                              />
                            }
                            label={
                              <Box>
                                <Typography
                                  variant="h6"
                                  fontWeight={600}
                                  sx={{ mb: 1 }}
                                >
                                  Quản lý hệ thống
                                </Typography>
                                <Typography
                                  variant="body1"
                                  color="text.secondary"
                                >
                                  Cho phép thay đổi cấu hình hệ thống
                                </Typography>
                              </Box>
                            }
                            sx={{ alignItems: "flex-start" }}
                          />
                        </Box>
                      </Card>

                      <Button
                        type="submit"
                        variant="contained"
                        startIcon={<Save sx={{ fontSize: "1.5rem" }} />}
                        disabled={isSubmitting}
                        sx={{
                          px: 6,
                          py: 2,
                          borderRadius: 2,
                          fontWeight: 600,
                          fontSize: "1.1rem",
                        }}
                      >
                        {isSubmitting
                          ? "Đang lưu..."
                          : "Lưu cài đặt phân quyền"}
                      </Button>
                    </form>
                  )}
                </Box>
              </TabPanel>
            </Box>
          </Paper>
        </Grid>
      </Grid>
    </Container>
  );
};

export default Settings;
