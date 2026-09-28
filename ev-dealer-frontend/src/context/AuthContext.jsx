import React, { createContext, useState, useEffect, useContext } from 'react';
import api from '../services/api';

// 1. Tạo Context
const AuthContext = createContext(null);

// 2. Tạo Provider Component
export const AuthProvider = ({ children }) => {
  const [user, setUser] = useState(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    const fetchCurrentUser = async () => {
      // Ask /users/me ONLY when a token exists. Without this guard the app
      // fired the request on every mount — including the public landing
      // page, before anyone pressed Login — the server answered 401, and
      // api.js's response interceptor treated that as an expired session:
      // it wiped storage and did window.location.href = "/login", kicking a
      // visitor off "/" on first load. No token means we already know the
      // answer (signed out); the authChanged listener re-runs this after
      // login, when there is a token to send.
      if (!localStorage.getItem('token')) {
        setUser(null)
        setLoading(false)
        return
      }

      try {
        // ---- Lấy dữ liệu người dùng thật từ API ----
        // Dùng api service có interceptor để gửi token tự động
        const response = await api.get('/users/me');

        // Backend cần trả về một user object có chứa thuộc tính 'role'
        // Ví dụ: { id: 1, username: 'admin', email: 'admin@test.com', role: 'Admin', ... }
        setUser(response);
        console.log('✅ User loaded from API:', response);

      } catch (error) {
        console.error('Failed to fetch current user. User might not be logged in.', error);
        setUser(null); // Đảm bảo user là null nếu có lỗi hoặc chưa đăng nhập
      } finally {
        setLoading(false);
      }
    };

    // Fetch once on mount
    fetchCurrentUser();

    // Re-fetch when auth state changes (login/logout)
    const onAuthChanged = () => {
      setLoading(true);
      fetchCurrentUser();
    };

    window.addEventListener('authChanged', onAuthChanged);

    return () => {
      window.removeEventListener('authChanged', onAuthChanged);
    };
  }, []);

  // Giá trị cung cấp cho các component con
  const value = {
    user,
    isAuthenticated: !!user,
    loading,
    // Các biến kiểm tra vai trò - based on actual database roles: "DealerStaff", "DealerManager", "EVMStaff", "Admin"
    isStaff: user?.role && (
      user.role === 'DealerStaff' || 
      user.role === 'EVMStaff'
    ),
    isManager: user?.role && (
      user.role === 'DealerManager'
    ),
    isAdmin: user?.role && (
      user.role === 'Admin'
    ),
  };

  // Chỉ render children khi đã tải xong thông tin user để tránh render sai UI
  return (
    <AuthContext.Provider value={value}>
      {!loading && children}
    </AuthContext.Provider>
  );
};

// 3. Tạo custom hook để sử dụng context dễ dàng hơn
export const useAuth = () => {
  const context = useContext(AuthContext);
  if (!context) {
    throw new Error('useAuth must be used within an AuthProvider');
  }
  return context;
};