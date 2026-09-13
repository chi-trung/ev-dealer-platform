import { useState, useEffect, useRef } from 'react';
import { createPortal } from 'react-dom';
import { useNavigate, useLocation } from 'react-router-dom';
import api from '../../services/api'; // Shared gateway axios instance
import { useAuth } from '../../context/AuthContext'; // Import useAuth

// Simple SVG Icons
const SearchIcon = () => (
  <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
    <circle cx="11" cy="11" r="8"/><path d="m21 21-4.35-4.35"/>
  </svg>
);

const FilterIcon = () => (
  <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
    <polygon points="22 3 2 3 10 12.46 10 19 14 21 14 12.46 22 3"/>
  </svg>
);

const EyeIcon = () => (
  <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
    <path d="M1 12s4-8 11-8 11 8 11 8-4 8-11 8-11-8-11-8z"/><circle cx="12" cy="12" r="3"/>
  </svg>
);

const PlusIcon = () => (
  <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
    <line x1="12" y1="5" x2="12" y2="19"/><line x1="5" y1="12" x2="19" y2="12"/>
  </svg>
);

const UserIcon = () => (
  <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
    <path d="M20 21v-2a4 4 0 0 0-4-4H8a4 4 0 0 0-4 4v2"/><circle cx="12" cy="7" r="4"/>
  </svg>
);

const CheckIcon = () => (
  <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
    <polyline points="20 6 9 17 4 12"/>
  </svg>
);

const MoreVerticalIcon = () => (
  // Chuyển thành 3 gạch ngang (hamburger-like) để dễ nhìn
  <svg width="16" height="16" viewBox="0 0 24 24" fill="none" xmlns="http://www.w3.org/2000/svg">
    <rect x="4" y="6" width="16" height="2" rx="1" fill="currentColor" />
    <rect x="4" y="11" width="16" height="2" rx="1" fill="currentColor" />
    <rect x="4" y="16" width="16" height="2" rx="1" fill="currentColor" />
  </svg>
);

const PencilIcon = () => (
    <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
        <path d="M17 3a2.828 2.828 0 1 1 4 4L7.5 20.5 2 22l1.5-5.5L17 3z" />
    </svg>
);

const FilePlusIcon = () => (
    <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
        <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z" />
        <polyline points="14 2 14 8 20 8" />
        <line x1="12" y1="18" x2="12" y2="12" />
        <line x1="9" y1="15" x2="15" y2="15" />
    </svg>
);

const TruckIcon = () => (
    <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
        <rect x="1" y="3" width="15" height="13" />
        <polygon points="16 8 20 8 23 11 23 16 16 16 16 8" />
        <circle cx="5.5" cy="18.5" r="2.5" />
        <circle cx="18.5" cy="18.5" r="2.5" />
    </svg>
);

const CheckCircleIcon = () => (
    <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
        <path d="M22 11.08V12a10 10 0 1 1-5.93-9.14" />
        <polyline points="22 4 12 14.01 9 11.01" />
    </svg>
);

const XCircleIcon = () => (
    <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
        <circle cx="12" cy="12" r="10" />
        <line x1="15" y1="9" x2="9" y2="15" />
        <line x1="9" y1="9" x2="15" y2="15" />
    </svg>
);


// New StatusBadge component
const StatusBadge = ({ status }) => {
  const getVietnameseStatus = (status) => {
    switch (status?.toLowerCase()) {
        case 'pending': return { text: 'Chờ xử lý', color: '#D97706', backgroundColor: '#FEF3C7' };
        case 'pendingapproval': return { text: 'Chờ duyệt hợp đồng', color: '#2563EB', backgroundColor: '#DBEAFE' };
        case 'readyfordelivery': return { text: 'Sẵn sàng giao', color: '#059669', backgroundColor: '#D1FAE5' };
        case 'delivering': return { text: 'Đang giao', color: '#3B82F6', backgroundColor: '#EFF6FF' };
        case 'delivered': return { text: 'Đã giao', color: '#16A34A', backgroundColor: '#DCFCE7' };
        case 'cancelled': return { text: 'Đã hủy', color: '#DC2626', backgroundColor: '#FEE2E2' };
        default: return { text: status || 'Không xác định', color: '#64748B', backgroundColor: '#F1F5F9' };
    }
  };

  const style = getVietnameseStatus(status);

  return (
    <span style={{
      display: 'inline-block',
      padding: '4px 12px',
      borderRadius: '9999px',
      fontSize: '12px',
      fontWeight: '500',
      color: style.color,
      backgroundColor: style.backgroundColor,
    }}>
      {style.text}
    </span>
  );
};


export default function SalesDashboard() {
  const { user, isStaff, isManager, isAdmin } = useAuth(); // Get user role info
  
  // Debug logging
  console.log('🔍 User Auth Info:', { user, isStaff, isManager, isAdmin });
  
  // Helper to read fields with multiple casing/variants from API responses
  const pick = (obj, ...keys) => {
    for (const k of keys) {
      if (!obj) continue;
      if (Object.prototype.hasOwnProperty.call(obj, k) && obj[k] !== undefined) return obj[k];
    }
    return undefined;
  };
  const [searchTerm, setSearchTerm] = useState('');
  const [customerSearch, setCustomerSearch] = useState('');
  const [statusFilter, setStatusFilter] = useState('all');
  const [orders, setOrders] = useState([]); // Change to orders
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState(null);
  const navigate = useNavigate();
  const location = useLocation();

  const [activeDropdown, setActiveDropdown] = useState(null); // id of active dropdown
  const [dropdownPos, setDropdownPos] = useState(null); // { top, left }
  const [removedOrderId, setRemovedOrderId] = useState(null);
  const dropdownRefs = useRef({}); // Keep for buttons if needed
  const portalRef = useRef(null);
  const sanitizeOrderId = (value) => String(value ?? '');


  const fetchOrders = async () => {
    setLoading(true);
    setError(null);
    try {
      // api interceptor unwraps response.data, so `response` here is the JSON payload
      const response = await api.get('/Orders');
      console.log('Raw API response:', response);

      // Handle new JSON format with $id and $values for cycle preservation
      const ordersData = response.$values || response.value || response.data || response || [];
      
      console.log('Parsed orders:', ordersData);
      setOrders(ordersData);
    } catch (err) {
      console.error('Error fetching orders:', err);
      setError('Failed to load sales data.');
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    fetchOrders();
  }, []);

  // Remove a recently rejected order immediately after returning from contract detail page
  useEffect(() => {
    const incomingId = location.state?.removedOrderId;
    if (!incomingId) return;
    setRemovedOrderId(sanitizeOrderId(incomingId));
    navigate(location.pathname, { replace: true });
  }, [location.state?.removedOrderId, location.pathname, navigate]);

    // Effect to handle clicks outside dropdowns
  useEffect(() => {
    const handleClickOutside = (event) => {
      if (activeDropdown) {
        if (portalRef.current && portalRef.current.contains(event.target)) {
          return;
        }
        if (dropdownRefs.current[activeDropdown] && dropdownRefs.current[activeDropdown].contains(event.target)) {
          return;
        }
        setActiveDropdown(null);
        setDropdownPos(null);
      }
    };
    document.addEventListener('mousedown', handleClickOutside);
    return () => {
      document.removeEventListener('mousedown', handleClickOutside);
    };
  }, [activeDropdown]);

  const formatCurrency = (amount) => {
    return new Intl.NumberFormat('vi-VN', {
      style: 'currency',
      currency: 'VND',
    }).format(amount);
  };

  // Adjust filtering logic for orders (use safe fallbacks)
  const visibleOrders = removedOrderId
    ? orders.filter(order => sanitizeOrderId(pick(order, 'orderID', 'OrderID', 'orderId', 'id')) !== removedOrderId)
    : orders;

  const activeOrder = activeDropdown
    ? visibleOrders.find(o => sanitizeOrderId(pick(o, 'orderID','OrderID','orderId','id')) === sanitizeOrderId(activeDropdown))
    : null;

  const filteredOrders = visibleOrders.filter(order => {
    const idVal = pick(order, 'orderID', 'OrderID', 'orderId', 'id');
    const custVal = pick(order, 'customerId', 'CustomerId');
    const statusVal = String(pick(order, 'status', 'Status') ?? '').toLowerCase();

    const matchesSearchTerm = String(idVal ?? '').includes(searchTerm);
    const matchesCustomerSearch = String(custVal ?? '').includes(customerSearch);
    const matchesStatus = statusFilter === 'all' || statusVal === statusFilter;

    return matchesSearchTerm && matchesCustomerSearch && matchesStatus;
  });

  const handleViewDetail = (order) => {
    const orderId = pick(order, 'orderID', 'OrderID', 'orderId', 'id');
    const status = pick(order, 'status', 'Status')?.toLowerCase();
    const contractId = pick(order, 'contract', 'Contract')?.contractId;

    if (!orderId) {
        console.error('Invalid order ID:', orderId);
        alert('Lỗi: ID đơn hàng không hợp lệ.');
        return;
    }

    if (status === 'pendingapproval' && contractId) {
        console.log(`Navigating to contract detail for contract: ${contractId}`);
        navigate(`/sales/contract/detail/${contractId}`);
    } else {
        console.log(`Navigating to order detail for order: ${orderId}`);
        navigate(`/sales/order/${orderId}`);
    }
    setActiveDropdown(null);
};

  const handleCreateQuote = () => {
    navigate('/sales/quote/new');
  };

  const handleViewQuotes = () => {
    navigate('/sales/quotes'); // New route for quote list
  };

  const handleEditOrder = (orderId) => {
    navigate(`/sales/orders/${orderId}/edit`);
    setActiveDropdown(null);
  };

  const handleCreateContract = (orderId) => {
      navigate(`/sales/contract/create/${orderId}`);
      setActiveDropdown(null);
  };

  const updateOrderStatus = async (orderId, newStatus, successMessage) => {
      if (!window.confirm(`Bạn có chắc chắn muốn ${successMessage.toLowerCase()} cho đơn hàng ID ${orderId}?`)) return;
      
      setLoading(true);
      try {
          // Changed the API endpoint from /api/Sales/orders to /api/Orders
          await api.put(`/Orders/${orderId}/status`, { status: newStatus });
          alert(successMessage);
          fetchOrders(); // Refresh data
      } catch (err) {
          console.error(`Error updating order status to ${newStatus}:`, err);
          alert(`Không thể cập nhật trạng thái đơn hàng. Lỗi: ${err.response?.data?.message || err.message}`);
      } finally {
          setLoading(false);
          setActiveDropdown(null);
      }
  };

  const handleStartDelivery = (orderId) => {
      updateOrderStatus(orderId, 'Delivering', 'Bắt đầu giao hàng');
  };

  const handleFinishDelivery = (orderId) => {
      updateOrderStatus(orderId, 'Delivered', 'Hoàn tất giao hàng');
  };

  const handleCancelDelivery = (orderId) => {
      updateOrderStatus(orderId, 'Cancelled', 'Hủy đơn hàng');
  };


  if (loading) {
    return (
      <div style={{ 
        display: 'flex', 
        justifyContent: 'center', 
        alignItems: 'center', 
        height: '100vh',
        backgroundColor: '#F8FAFC'
      }}>
        <div>Đang tải dữ liệu...</div>
      </div>
    );
  }

  if (error) {
    return (
      <div style={{ 
        display: 'flex', 
        justifyContent: 'center', 
        alignItems: 'center', 
        height: '100vh',
        backgroundColor: '#F8FAFC',
        color: 'red'
      }}>
        <div>Lỗi: {error}</div>
      </div>
    );
  }

  return (
    <div style={{ 
      minHeight: '100vh', 
      backgroundColor: '#F8FAFC',
      fontFamily: '-apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif'
    }}>
      {/* Header */}
      <header style={{
        backgroundColor: 'white',
        borderBottom: '1px solid #E2E8F0',
        padding: '16px 24px',
        boxShadow: '0 1px 3px 0 rgba(0, 0, 0, 0.05)'
      }}>
        <div style={{
          display: 'flex',
          justifyContent: 'space-between',
          alignItems: 'center',
          maxWidth: '1280px',
          margin: '0 auto'
        }}>
          <h1 style={{ 
            fontSize: '24px', 
            fontWeight: '700', 
            color: '#0F172A', 
            margin: 0 
          }}>
            Quản lý Đơn hàng
          </h1>
          
          <div style={{ display: 'flex', alignItems: 'center', gap: '16px' }}>
            <div style={{
              display: 'flex',
              alignItems: 'center',
              gap: '12px'
            }}>
              <UserIcon />
              <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'flex-end' }}>
                <span style={{ fontSize: '14px', fontWeight: 600, color: '#0F172A' }}>{user?.fullName || user?.name || '...'}</span>
                <span style={{ fontSize: '12px', backgroundColor: '#D1FAE5', color: '#059669', padding: '4px 8px', borderRadius: '9999px', fontWeight: 600 }}>
                  {user?.role ? user.role.replace(/([a-z])([A-Z])/g, '$1 $2') : 'Chưa xác định'}
                </span>
              </div>
            </div>
          </div>
        </div>
      </header>

      <div style={{ 
        maxWidth: '1280px', 
        margin: '0 auto',
        padding: '24px'
      }}>
        {/* Role card removed per request; header now shows compact role badge */}

        {/* Control Panel (Sticky) */}
        <div style={{
          position: 'sticky', // Make this section sticky
          top: 0, // Stick to the top
          zIndex: 10, // Ensure it stays above other content
          backgroundColor: '#F8FAFC', // Match background to avoid transparency issues
          paddingTop: '24px', // Add padding to compensate for sticky position
          paddingBottom: '24px', // Add padding to separate from content below
          display: 'grid',
          gridTemplateColumns: '1fr 300px',
          gap: '24px',
          marginBottom: '0px' // Remove bottom margin as paddingBottom handles spacing
        }}>
          {/* Bộ lọc và tìm kiếm - BỐ CỤC MỚI 2x2 */}
          <div style={{
            backgroundColor: 'white',
            borderRadius: '12px',
            border: '1px solid #E2E8F0',
            padding: '20px'
          }}>
            <h2 style={{ 
              fontSize: '18px', 
              fontWeight: '600', 
              color: '#0F172A', 
              margin: '0 0 20px 0',
              display: 'flex',
              alignItems: 'center',
              gap: '8px'
            }}>
              <FilterIcon />
              Bộ lọc & Tìm kiếm
            </h2>
            
            {/* BỐ CỤC MỚC: 2 HÀNG 2 CỘT */}
            <div style={{ 
              display: 'grid', 
              gridTemplateColumns: '1fr 1fr', 
              gridTemplateRows: 'auto auto',
              gap: '20px',
              alignItems: 'end'
            }}>
              {/* Hàng 1 - Cột 1: Tìm kiếm mã đơn */}
              <div style={{ minWidth: 0 }}>
                <label style={{ 
                  display: 'block', 
                  fontSize: '14px', 
                  fontWeight: '500', 
                  color: '#374151', 
                  marginBottom: '8px' 
                }}>
                  Mã đơn hàng
                </label>
                <div style={{ position: 'relative' }}>
                  <div style={{ 
                    position: 'absolute', 
                    left: '12px', 
                    top: '50%', 
                    transform: 'translateY(-50%)', 
                    color: '#9CA3AF',
                    zIndex: 1
                  }}>
                    <SearchIcon />
                  </div>
                  <input
                    type="text"
                    placeholder="Nhập mã đơn..."
                    value={searchTerm}
                    onChange={(e) => setSearchTerm(e.target.value)}
                    style={{
                      width: '100%',
                      paddingLeft: '40px',
                      paddingRight: '12px',
                      paddingTop: '10px',
                      paddingBottom: '10px',
                      border: '1px solid #D1D5DB',
                      borderRadius: '8px',
                      fontSize: '14px',
                      backgroundColor: '#F9FAFB',
                      position: 'relative',
                      height: '40px',
                      boxSizing: 'border-box'
                    }}
                  />
                </div>
              </div>

              {/* Hàng 1 - Cột 2: Tìm kiếm khách hàng */}
              <div style={{ minWidth: 0 }}>
                <label style={{ 
                  display: 'block', 
                  fontSize: '14px', 
                  fontWeight: '500', 
                  color: '#374151', 
                  marginBottom: '8px' 
                }}>
                  ID Khách hàng
                </label>
                <div style={{ position: 'relative' }}>
                  <div style={{ 
                    position: 'absolute', 
                    left: '12px', 
                    top: '50%', 
                    transform: 'translateY(-50%)', 
                    color: '#9CA3AF',
                    zIndex: 1
                  }}>
                    <SearchIcon />
                  </div>
                  <input
                    type="text"
                    placeholder="Nhập ID khách hàng..."
                    value={customerSearch}
                    onChange={(e) => setCustomerSearch(e.target.value)}
                    style={{
                      width: '100%',
                      paddingLeft: '40px',
                      paddingRight: '12px',
                      paddingTop: '10px',
                      paddingBottom: '10px',
                      border: '1px solid #D1D5DB',
                      borderRadius: '8px',
                      fontSize: '14px',
                      backgroundColor: '#F9FAFB',
                      position: 'relative',
                      height: '40px',
                      boxSizing: 'border-box'
                    }}
                  />
                </div>
              </div>

              {/* Hàng 2 - Cột 1: Trạng thái */}
              <div style={{ minWidth: 0 }}>
                <label style={{ 
                  display: 'block', 
                  fontSize: '14px', 
                  fontWeight: '500', 
                  color: '#374151', 
                  marginBottom: '8px' 
                }}>
                  Trạng thái
                </label>
                <div style={{ position: 'relative' }}>
                  <select
                    value={statusFilter}
                    onChange={(e) => setStatusFilter(e.target.value)}
                    style={{
                      width: '100%',
                      padding: '10px 12px',
                      border: '1px solid #D1D5DB',
                      borderRadius: '8px',
                      fontSize: '14px',
                      backgroundColor: '#F9FAFB',
                      color: '#374151',
                      height: '40px',
                      boxSizing: 'border-box',
                      overflow: 'hidden',
                      textOverflow: 'ellipsis',
                      whiteSpace: 'nowrap',
                      minWidth: '100%',
                      maxWidth: '100%',
                      appearance: 'none',
                      backgroundImage: `url("data:image/svg+xml;charset=UTF-8,%3csvg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 24 24' fill='none' stroke='currentColor' stroke-width='2' stroke-linecap='round' stroke-linejoin='round'%3e%3cpolyline points='6 9 12 15 18 9'%3e%3c/polyline%3e%3c/svg%3e")`,
                      backgroundRepeat: 'no-repeat',
                      backgroundPosition: 'right 12px center',
                      backgroundSize: '16px',
                      paddingRight: '36px'
                    }}
                  >
                    <option value="all">Tất cả trạng thái</option>
                    <option value="pending">Chờ xử lý</option>
                    <option value="pendingapproval">Chờ duyệt hợp đồng</option>
                    <option value="readyfordelivery">Sẵn sàng giao</option>
                    <option value="delivering">Đang giao</option>
                    <option value="delivered">Đã giao</option>
                    <option value="cancelled">Đã hủy</option>
                  </select>
                </div>
              </div>
            </div>
          </div>

          {/* Thống kê nhanh & Actions */}
          <div style={{
            backgroundColor: 'white',
            borderRadius: '12px',
            border: '1px solid #E2E8F0',
            padding: '20px',
            display: 'flex',
            flexDirection: 'column'
          }}>
            <h2 style={{ 
              fontSize: '18px', 
              fontWeight: '600', 
              color: '#0F172A', 
              margin: '0 0 16px 0' 
            }}>
              Thao tác
            </h2>
            
            <div style={{ display: 'flex', flexDirection: 'column', gap: '8px', flexGrow: 1 }}>
              {/* Nút cho Staff */}
              {(isStaff || isAdmin) && (
                <button 
                  onClick={handleCreateQuote}
                  style={{
                    width: '100%',
                    display: 'flex',
                    alignItems: 'center',
                    justifyContent: 'center',
                    gap: '8px',
                    backgroundColor: '#3B82F6',
                    color: 'white',
                    padding: '12px',
                    borderRadius: '8px',
                    fontWeight: '500',
                    border: 'none',
                    cursor: 'pointer',
                    fontSize: '14px'
                  }}
                >
                  <PlusIcon />
                  Tạo báo giá
                </button>
              )}

              {/* Nút cho Manager */}
              {(isManager || isAdmin) && (
                <button 
                  // onClick={handleApproveQuotes} // Sẽ thêm hàm này sau
                  style={{
                    width: '100%',
                    display: 'flex',
                    alignItems: 'center',
                    justifyContent: 'center',
                    gap: '8px',
                    backgroundColor: '#F59E0B',
                    color: 'white',
                    padding: '12px',
                    borderRadius: '8px',
                    fontWeight: '500',
                    border: 'none',
                    cursor: 'pointer',
                    fontSize: '14px'
                  }}
                >
                  <CheckIcon />
                  Duyệt báo giá
                </button>
              )}

              {/* Nút chung */}
              <button 
                onClick={handleViewQuotes}
                style={{
                  width: '100%',
                  display: 'flex',
                  alignItems: 'center',
                  justifyContent: 'center',
                  gap: '8px',
                  backgroundColor: '#10B981',
                  color: 'white',
                  padding: '12px',
                  borderRadius: '8px',
                  fontWeight: '500',
                  border: 'none',
                  cursor: 'pointer',
                  fontSize: '14px'
                }}
              >
                <EyeIcon />
                Danh sách báo giá
              </button>
            </div>
          </div>
        </div>

        {/* Danh sách đơn hàng */}
        <div style={{
          backgroundColor: 'white',
          borderRadius: '12px',
          border: '1px solid #E2E8F0',
          overflow: 'visible'
        }}>
          <div style={{ 
            padding: '20px 24px', 
            borderBottom: '1px solid #E2E8F0',
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'space-between'
          }}>
            <div>
              <h2 style={{ 
                fontSize: '18px', 
                fontWeight: '600', 
                color: '#0F172A', 
                margin: '0 0 4px 0' 
              }}>
                Danh sách đơn hàng
              </h2>
              <p style={{ 
                fontSize: '14px', 
                color: '#64748B', 
                margin: 0 
              }}>
                {filteredOrders.length} đơn hàng được tìm thấy
              </p>
            </div>
          </div>

          <div style={{ overflowX: 'auto' }}>
            <table style={{ 
              width: '100%', 
              borderCollapse: 'collapse',
              minWidth: '1200px', // Increased min-width for better spacing
              tableLayout: 'fixed'
            }}>
              <colgroup>
                 <col style={{ width: '5%' }} />
                 {['12%','15%','15%','12%','19%'].map((w, i) => (
                  <col key={i} style={{ width: w }} />
                ))}
              </colgroup>
              <thead style={{ 
                backgroundColor: '#F8FAFC', 
                borderBottom: '1px solid #E2E8F0' 
              }}>
                <tr>
                  <th style={{ 
                    padding: '16px 12px', 
                    textAlign: 'left', 
                    fontSize: '12px', 
                    fontWeight: '600', 
                    color: '#475569', 
                    textTransform: 'uppercase', 
                    letterSpacing: '0.05em',
                    overflow: 'hidden',
                    textOverflow: 'ellipsis',
                    whiteSpace: 'nowrap'
                  }}>
                    Mã đơn hàng
                  </th>
                  <th style={{ 
                    padding: '16px 12px', 
                    textAlign: 'left', 
                    fontSize: '12px', 
                    fontWeight: '600', 
                    color: '#475569', 
                    textTransform: 'uppercase', 
                    letterSpacing: '0.05em',
                    overflow: 'hidden',
                    textOverflow: 'ellipsis',
                    whiteSpace: 'nowrap'
                  }}>
                    ID Khách hàng
                  </th>
                  <th style={{ 
                    padding: '16px 12px', 
                    textAlign: 'left',
                    fontSize: '12px', 
                    fontWeight: '600', 
                    color: '#475569', 
                    textTransform: 'uppercase', 
                    letterSpacing: '0.05em',
                    whiteSpace: 'nowrap',
                    overflow: 'hidden',
                    textOverflow: 'ellipsis'
                  }}>
                    Xe
                  </th>
                  <th style={{ 
                    padding: '16px 12px', 
                    textAlign: 'left',
                    fontSize: '12px', 
                    fontWeight: '600', 
                    color: '#475569', 
                    textTransform: 'uppercase', 
                    letterSpacing: '0.05em',
                    whiteSpace: 'nowrap',
                    overflow: 'hidden',
                    textOverflow: 'ellipsis'
                  }}>
                    Tổng tiền
                  </th>
                  <th style={{ 
                    padding: '16px 12px', 
                    textAlign: 'left',
                    fontSize: '12px', 
                    fontWeight: '600', 
                    color: '#475569', 
                    textTransform: 'uppercase', 
                    letterSpacing: '0.05em',
                    whiteSpace: 'nowrap',
                    overflow: 'hidden',
                    textOverflow: 'ellipsis'
                  }}>
                    Trạng thái
                  </th>
                  <th style={{ 
                    padding: '16px 12px', 
                    whiteSpace: 'nowrap', 
                    textAlign: 'center',
                    overflow: 'hidden'
                  }}>
                    Ghi chú
                  </th>
                  <th style={{ 
                    padding: '16px 12px', 
                    whiteSpace: 'nowrap', 
                    textAlign: 'center',
                    overflow: 'hidden'
                  }}>
                    Thao tác
                  </th>
                </tr>
              </thead>
              <tbody>
                {filteredOrders.length > 0 ? (
                  filteredOrders.map((order, index) => {
                    // Lấy vehicleId và quantity trực tiếp từ đối tượng order
                    const vehicleId = pick(order, 'vehicleId', 'VehicleId');
                    const quantity = pick(order, 'quantity', 'Quantity');
                    
                    const orderId = pick(order, 'orderID','OrderID','orderId','id');
                    const orderStatus = pick(order, 'status', 'Status')?.toLowerCase();
                    const contractStatus = pick(order, 'contract', 'Contract')?.status?.toLowerCase();

                    return (
                    <tr 
                      key={orderId ?? Math.random()} 
                      style={{ 
                        borderBottom: index < filteredOrders.length - 1 ? '1px solid #F1F5F9' : 'none'
                      }}
                    >
                      <td style={{ 
                        padding: '16px 12px', 
                        whiteSpace: 'nowrap',
                        fontSize: '14px',
                        overflow: 'hidden',
                        textOverflow: 'ellipsis'
                      }}>
                        <span style={{ 
                          fontWeight: '600', 
                          color: '#3B82F6'
                        }}>
                          {pick(order, 'orderNumber','OrderNumber') ?? orderId}
                        </span>
                      </td>
                      <td style={{ 
                        padding: '16px 12px', 
                        whiteSpace: 'nowrap',
                        overflow: 'hidden',
                        textOverflow: 'ellipsis'
                      }}>
                        <div>
                          <div style={{ 
                            fontSize: '14px', 
                            fontWeight: '500', 
                            color: '#0F172A',
                            overflow: 'hidden',
                            textOverflow: 'ellipsis',
                            whiteSpace: 'nowrap'
                          }}>
                            {pick(order,'customerId','CustomerId')}
                          </div>
                        </div>
                      </td>
                      <td style={{ 
                        padding: '16px 12px', 
                        whiteSpace: 'nowrap',
                        overflow: 'hidden',
                        textOverflow: 'ellipsis'
                      }}>
                        <div style={{ 
                          display: 'flex', 
                          alignItems: 'center',
                          gap: '12px'
                        }}>
                          <span style={{ 
                            color: '#0F172A',
                            fontSize: '14px',
                            overflow: 'hidden',
                            textOverflow: 'ellipsis',
                            whiteSpace: 'nowrap'
                          }}>
                            {`ID: ${vehicleId} (SL: ${quantity})`}
                          </span>
                        </div>
                      </td>
                      <td style={{ 
                        padding: '16px 12px', 
                        whiteSpace: 'nowrap',
                        overflow: 'hidden',
                        textOverflow: 'ellipsis'
                      }}>
                        <div style={{ 
                          fontWeight: '600', 
                          color: '#0F172A',
                          fontSize: '14px',
                          overflow: 'text-overflow',
                          whiteSpace: 'nowrap'
                        }}>
                          {formatCurrency(pick(order,'totalPrice','TotalPrice') ?? 0)}
                        </div>
                      </td>
                      <td style={{ 
                        padding: '16px 12px', 
                        whiteSpace: 'nowrap',
                        overflow: 'hidden',
                        textOverflow: 'ellipsis'
                      }}>
                        <StatusBadge status={pick(order, 'status', 'Status')} />
                      </td>
                      <td style={{ 
                        padding: '16px 12px', 
                        whiteSpace: 'nowrap', 
                        textAlign: 'center',
                        overflow: 'hidden'
                      }}>
                        {pick(order, 'notes', 'Notes') || 'N/A'}
                      </td>
                      <td style={{ padding: '16px 12px', position: 'relative' }}>
                          <div ref={el => dropdownRefs.current[orderId] = el} style={{ position: 'relative', display: 'inline-block' }}>
                              <button
                                  onClick={(e) => {
                                      e.stopPropagation();
                                      const rect = e.currentTarget.getBoundingClientRect();
                                      if (activeDropdown === orderId) {
                                          setActiveDropdown(null);
                                      } else {
                                          setActiveDropdown(orderId);
                                          setDropdownPos({ top: rect.bottom + window.scrollY, left: rect.left - 150 }); // Adjust to show on the left
                                      }
                                  }}
                                  style={{ background: 'none', border: 'none', cursor: 'pointer', padding: '4px', borderRadius: '4px', display: 'flex', alignItems: 'center', justifyContent: 'center', color: '#64748B' }}
                                  onMouseOver={(e) => e.currentTarget.style.backgroundColor = '#F3F4F6'}
                                  onMouseOut={(e) => e.currentTarget.style.backgroundColor = 'transparent'}
                              >
                                  <MoreVerticalIcon />
                              </button>
                          </div>
                      </td>
                    </tr>
                  );
                  })
                ) : (
                  <tr>
                    <td colSpan="6" style={{ // Changed colSpan to 6
                      padding: '48px 24px', 
                      textAlign: 'center' 
                    }}>
                      <div style={{ 
                        color: '#94A3B8'
                      }}>
                        Không tìm thấy đơn hàng nào
                      </div>
                    </td>
                  </tr>
                )}
              </tbody>
            </table>
          </div>
        </div>
      </div>

      {activeDropdown && dropdownPos && createPortal(
          <div
              ref={portalRef}
              style={{
                  position: 'absolute',
                  top: dropdownPos.top + 8,
                  left: dropdownPos.left - 150, // Adjust to show on the left
                  backgroundColor: 'white',
                  border: '1px solid #E2E8F0',
                  borderRadius: '8px',
                  boxShadow: '0 6px 12px rgba(0,0,0,0.12)',
                  zIndex: 9999,
                  minWidth: '200px',
                  overflow: 'hidden'
              }}
          >
              {activeOrder && (
                  <>
                      {/* --- Actions for Dealer Staff --- */}
                      {(isStaff || isAdmin) && (
                          <>
                              {/* 1. Chỉnh sửa đơn hàng (khi status = Pending) */}
                              {activeOrder.status?.toLowerCase() === 'pending' && (
                                  <button onClick={() => handleEditOrder(activeDropdown)} style={menuButtonStyle}>
                                      <PencilIcon /> Chỉnh sửa đơn hàng
                                  </button>
                              )}

                              {/* 2. Tạo hợp đồng (khi status = Pending) */}
                              {activeOrder.status?.toLowerCase() === 'pending' && !activeOrder.contract && (
                                  <button onClick={() => handleCreateContract(activeDropdown)} style={menuButtonStyle}>
                                      <FilePlusIcon /> Tạo hợp đồng
                                  </button>
                              )}
                          </>
                      )}

                      {/* --- Actions for Dealer Manager --- */}
                      {(isManager || isAdmin) && (
                          <>
                              {/* 5. Bắt đầu giao xe */}
                              {activeOrder.status?.toLowerCase() === 'readyfordelivery' && (
                                  <button onClick={() => handleStartDelivery(activeDropdown)} style={menuButtonStyle}>
                                      <TruckIcon /> Bắt đầu giao xe
                                  </button>
                              )}

                              {/* 5. Hoàn tất giao xe */}
                              {activeOrder.status?.toLowerCase() === 'delivering' && (
                                  <button onClick={() => handleFinishDelivery(activeDropdown)} style={menuButtonStyle}>
                                      <CheckCircleIcon /> Hoàn tất giao hàng
                                  </button>
                              )}

                              {/* 5. Hủy giao xe (nếu có sự cố) */}
                              {(activeOrder.status?.toLowerCase() === 'readyfordelivery' || activeOrder.status?.toLowerCase() === 'delivering') && (
                                  <button onClick={() => handleCancelDelivery(activeDropdown)} style={{...menuButtonStyle, color: '#EF4444'}}>
                                      <XCircleIcon /> Hủy giao hàng
                                  </button>
                              )}
                          </>
                      )}

                      {/* --- Common Actions --- */}
                      <button onClick={() => handleViewDetail(activeOrder)} style={menuButtonStyle}>
                          <EyeIcon /> Xem chi tiết
                      </button>
                  </>
              )}
          </div>,
          document.body
      )}


      {/* CSS để đảm bảo tính ổn định */}
      <style>{`
        /* Cố định hoàn toàn kích thước select và input */
        select, input[type="text"] {
          height: 40px !important;
          box-sizing: border-box !important;
          min-width: 100% !important;
          max-width: 100% !important;
        }
        
        select {
          overflow: hidden !important;
          text-overflow: ellipsis !important;
          white-space: nowrap !important;
        }
        
        /* Đảm bảo bảng không thay đổi kích thước */
        table {
          table-layout: fixed !important;
        }
        
        /* Đảm bảo các cột có kích thước cố định */
        col {
          width: auto !important;
        }
        
        /* Xử lý text overflow trong bảng */
        td, th {
          text-overflow: ellipsis !important;
        }
        
        /* Cải thiện hiển thị options trong select */
        select option {
          background-color: white;
          color: #374151;
          padding: 8px 12px;
        }
        
        select option:hover,
        select option:focus,
        select option:active {
          background-color: #3B82F6 !important;
          color: white !important;
        }
        
        select option:checked {
          background-color: #3B82F6 !important;
          color: white !important;
        }
      `}</style>
    </div>
  );
}

const menuButtonStyle = {
    display: 'flex',
    alignItems: 'center',
    gap: '8px',
    width: '100%',
    padding: '10px 12px',
    background: 'none',
    border: 'none',
    textAlign: 'left',
    cursor: 'pointer',
    fontSize: '14px',
    color: '#374151'
};
