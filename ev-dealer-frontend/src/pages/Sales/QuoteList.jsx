import React, { useState, useEffect, useRef } from 'react';
import { createPortal } from 'react-dom';
import { useNavigate } from 'react-router-dom';
import api from '../../services/api';
import { useAuth } from '../../context/AuthContext';

// Icons (re-using from SalesList for consistency)
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

const PlusIcon = () => (
  <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
    <line x1="12" y1="5" x2="12" y2="19"/><line x1="5" y1="12" x2="19" y2="12"/>
  </svg>
);

const EyeIcon = () => (
  <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
    <path d="M1 12s4-8 11-8 11 8 11 8-4 8-11 8-11-8-11-8z"/><circle cx="12" cy="12" r="3"/>
  </svg>
);

// Back Icon for navigation
const BackIcon = () => (
  <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
    <path d="M19 12H5M12 19l-7-7 7-7"/>
  </svg>
);

// New Icons for actions
const MoreVerticalIcon = () => (
  // Chuyển thành 3 gạch ngang (hamburger-like) để dễ nhìn
  <svg width="16" height="16" viewBox="0 0 24 24" fill="none" xmlns="http://www.w3.org/2000/svg">
    <rect x="4" y="6" width="16" height="2" rx="1" fill="currentColor" />
    <rect x="4" y="11" width="16" height="2" rx="1" fill="currentColor" />
    <rect x="4" y="16" width="16" height="2" rx="1" fill="currentColor" />
  </svg>
);

const XCircleIcon = () => (
  <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
    <circle cx="12" cy="12" r="10"/><line x1="15" y1="9" x2="9" y2="15"/><line x1="9" y1="9" x2="15" y2="15"/>
  </svg>
);

// PencilIcon is removed as edit functionality is removed
// const PencilIcon = () => (
//     <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
//         <path d="M17 3a2.828 2.828 0 1 1 4 4L7.5 20.5 2 22l1.5-5.5L17 3z" />
//     </svg>
// );

export default function QuoteList() {
  const { isStaff, isManager, isAdmin } = useAuth();
  const [quotes, setQuotes] = useState([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(null);
  const [quoteIdSearchTerm, setQuoteIdSearchTerm] = useState('');
  const [createdDateSearchTerm, setCreatedDateSearchTerm] = useState('');
  const [customerSearch, setCustomerSearch] = useState('');
  const [statusFilter, setStatusFilter] = useState('all');
  const [activeDropdown, setActiveDropdown] = useState(null); // id of active dropdown
  const [dropdownPos, setDropdownPos] = useState(null); // { top, left }
  const navigate = useNavigate();
  const dropdownRefs = useRef({}); // Keep for buttons if needed
  const portalRef = useRef(null);
  const activeQuote = activeDropdown ? quotes.find(q => q.id === activeDropdown) : null;

  useEffect(() => {
    fetchQuotes();
  }, []);

  // Effect to handle clicks outside dropdowns
  useEffect(() => {
    const handleClickOutside = (event) => {
      // Check if the click is outside any active dropdown
      if (activeDropdown) {
        // If portal dropdown exists and contains click, keep open
        if (portalRef.current && portalRef.current.contains(event.target)) {
          return;
        }
        // If the clicked element is inside the button that opened dropdown, ignore
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
  }, [activeDropdown]); // Depend on activeDropdown to re-attach listener when it changes

  const fetchQuotes = async () => {
    setLoading(true);
    setError(null);
    try {
      // api resolves to response.data, so one .data level is removed
      const data = await api.get('/Quotes'); // Corrected port to 5003
      // Handle cases where the API returns an object with the array inside a property (e.g., from .NET backend with $values)
      setQuotes(Array.isArray(data) ? data : data?.$values || []);
    } catch (err) {
      console.error('Error fetching quotes:', err);
      setError('Failed to load quote data.');
      setQuotes([]); // On error, ensure quotes is an empty array
    } finally {
      setLoading(false);
    }
  };

  const getVietnameseStatus = (status) => {
    switch (status) {
      case 'Active':
        return 'Đang hoạt động';
      case 'ConvertedToOrder':
        return 'Đã Tạo Đơn Hàng';
      case 'Cancelled':
        return 'Đã Hủy';
      default:
        return status;
    }
  };

  const handleCancelQuote = async (quoteId) => {
    if (window.confirm(`Bạn có chắc chắn muốn hủy báo giá ID ${quoteId} không?`)) {
      try {
        setLoading(true);
        // Assuming there's an API endpoint to update quote status
        await api.put(`/Quotes/${quoteId}/status`, { status: 'Cancelled' }); // Corrected port and endpoint
        alert(`Báo giá ID ${quoteId} đã được hủy.`);
        fetchQuotes(); // Re-fetch quotes to update the list
      } catch (err) {
        console.error('Error cancelling quote:', err);
        alert('Không thể hủy báo giá. Vui lòng thử lại.');
      } finally {
        setLoading(false);
        setActiveDropdown(null); // Close dropdown
      }
    }
  };

  // handleApproveQuote is removed as per new logic (no approval step)

  const handleCreateOrderFromQuote = (quoteId) => {
    navigate(`/sales/orders/create-from-quote/${quoteId}`); // Navigate to order creation page
    setActiveDropdown(null); // Close dropdown
  };

  const handleViewQuoteDetail = (quoteId) => {
    // Navigate to read-only quote view
    navigate(`/sales/quotes/${quoteId}/view`);
    setActiveDropdown(null); // Close dropdown
  };

  // handleEditQuote is removed as per new logic (no edit functionality)
  // const handleEditQuote = (quoteId) => {
  //   navigate(`/sales/quotes/${quoteId}/edit`); // Navigate to edit quote page
  //   setActiveDropdown(null); // Close dropdown
  // };

  const formatCurrency = (amount) => {
    return new Intl.NumberFormat('vi-VN', {
      style: 'currency',
      currency: 'VND',
    }).format(amount);
  };

  const formatDate = (dateString) => {
    if (!dateString) return '';
    const date = new Date(dateString);
    const day = String(date.getDate()).padStart(2, '0');
    const month = String(date.getMonth() + 1).padStart(2, '0');
    const year = date.getFullYear();
    return `${day}/${month}/${year}`;
  };

  const filteredQuotes = quotes.filter(quote => {
    // Filter by Quote ID
    const matchesQuoteId = quoteIdSearchTerm
      ? quote.id?.toString().includes(quoteIdSearchTerm)
      : true;

    // Filter by Customer ID
    const matchesCustomerSearch = customerSearch
      ? quote.customerId?.toString().includes(customerSearch)
      : true;

    // Filter by status
    const matchesStatus = statusFilter === 'all' || quote.status === statusFilter;

    // Filter by Created Date
    const matchesCreatedDate = createdDateSearchTerm
      ? formatDate(quote.createdAt).includes(createdDateSearchTerm)
      : true;

    return matchesQuoteId && matchesCustomerSearch && matchesStatus && matchesCreatedDate;
  });

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
            Quản lý Báo giá
          </h1>

          <div style={{ display: 'flex', alignItems: 'center', gap: '16px' }}>
            {/* User info can be added here if needed */}
          </div>
        </div>
      </header>

      <div style={{ maxWidth: '1280px', margin: '0 auto', padding: '24px' }}>
        {/* Header with Back Button */}
        <div style={{
          position: 'sticky',
          top: 0,
          zIndex: 10,
          backgroundColor: '#F8FAFC',
          paddingTop: '24px',
          paddingBottom: '16px',
          borderBottom: '1px solid #E2E8F0',
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'space-between',
          marginBottom: '24px',
        }}>
          <div style={{ display: 'flex', alignItems: 'center', gap: '12px' }}>
            <button
              onClick={() => navigate('/sales')}
              style={{
                display: 'flex',
                alignItems: 'center',
                gap: '8px',
                padding: '8px 16px',
                backgroundColor: 'white',
                border: '1px solid #D1D5DB',
                borderRadius: '8px',
                cursor: 'pointer',
                transition: 'all 0.2s',
                fontSize: '14px',
                fontWeight: '500',
                color: '#374151'
              }}
              onMouseOver={(e) => e.target.style.backgroundColor = '#F3F4F6'}
              onMouseOut={(e) => e.target.style.backgroundColor = 'white'}
            >
              <BackIcon />
              Quay lại
            </button>
            <div>
              <h1 style={{ fontSize: '24px', fontWeight: '700', color: '#0F172A', margin: 0 }}>
                Danh sách Báo giá
              </h1>
            </div>
          </div>
        </div>

        {/* Control Panel */}
        <div style={{
          display: 'grid',
          gridTemplateColumns: '1fr 300px',
          gap: '24px',
          marginBottom: '24px'
        }}>
          {/* Bộ lọc và tìm kiếm */}
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

            <div style={{
              display: 'grid',
              gridTemplateColumns: '1fr 1fr',
              gridTemplateRows: 'auto auto',
              gap: '20px',
              alignItems: 'end'
            }}>
              {/* Hàng 1 - Cột 1: Tìm kiếm Mã báo giá */}
              <div>
                <label style={{
                  display: 'block',
                  fontSize: '14px',
                  fontWeight: '500',
                  color: '#374151',
                  marginBottom: '8px'
                }}>
                  Mã báo giá
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
                    placeholder="Tìm kiếm mã báo giá..."
                    value={quoteIdSearchTerm}
                    onChange={(e) => setQuoteIdSearchTerm(e.target.value)}
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
                      boxSizing: 'border-box',
                      color: '#374151'
                    }}
                  />
                </div>
              </div>

              {/* Hàng 1 - Cột 2: Tìm kiếm ID Khách hàng */}
              <div>
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
                    placeholder="Tìm kiếm ID khách hàng..."
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
                      boxSizing: 'border-box',
                      color: '#374151'
                    }}
                  />
                </div>
              </div>
              {/* Hàng 2 - Cột 1: Bộ lọc trạng thái */}
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
                      paddingRight: '36px',
                      color: '#374151'
                    }}
                  >
                    <option value="all">Tất cả trạng thái</option>
                    <option value="Active">Đang hoạt động</option>
                    <option value="ConvertedToOrder">Đã Tạo Đơn Hàng</option>
                    <option value="Cancelled">Đã Hủy</option>
                  </select>
                </div>
              </div>

              {/* Hàng 2 - Cột 2: Tìm kiếm Ngày tạo báo giá */}
              <div>
                <label style={{
                  display: 'block',
                  fontSize: '14px',
                  fontWeight: '500',
                  color: '#374151',
                  marginBottom: '8px'
                }}>
                  Ngày tạo báo giá (dd/mm/yyyy)
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
                    placeholder="Tìm kiếm ngày tạo báo giá..."
                    value={createdDateSearchTerm}
                    onChange={(e) => setCreatedDateSearchTerm(e.target.value)}
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
                      boxSizing: 'border-box',
                      color: '#374151'
                    }}
                  />
                </div>
              </div>
              {/* Nút Xóa bộ lọc */}
              <div style={{ marginTop: '12px', gridColumn: '1 / -1', display: 'flex', gap: '12px' }}>
                <button
                  onClick={() => {
                    setQuoteIdSearchTerm('');
                    setCustomerSearch('');
                    setStatusFilter('all');
                    setCreatedDateSearchTerm('');
                  }}
                  style={{
                    padding: '10px 14px',
                    backgroundColor: 'white',
                    border: '1px solid #D1D5DB',
                    borderRadius: '8px',
                    color: '#374151',
                    fontWeight: 600,
                    cursor: 'pointer',
                    boxShadow: '0 1px 2px rgba(0,0,0,0.04)'
                  }}
                  onMouseOver={(e) => e.currentTarget.style.backgroundColor = '#F3F4F6'}
                  onMouseOut={(e) => e.currentTarget.style.backgroundColor = 'white'}
                >
                  Xóa bộ lọc
                </button>
              </div>
            </div>
          </div>

          {/* Thống kê nhanh */}
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
              margin: '0 0 16px 0'
            }}>
              Tổng quan
            </h2>

            <div style={{ display: 'flex', flexDirection: 'column', gap: '12px' }}>
              <div style={{
                display: 'flex',
                justifyContent: 'space-between',
                alignItems: 'center',
                padding: '12px',
                backgroundColor: '#F0F9FF',
                borderRadius: '8px'
              }}>
                <span style={{ fontSize: '14px', color: '#0369A1' }}>Tổng báo giá</span>
                <span style={{ fontSize: '18px', fontWeight: '700', color: '#0369A1' }}>
                  {quotes.length}
                </span>
              </div>

              <div style={{
                display: 'flex',
                justifyContent: 'space-between',
                alignItems: 'center',
                padding: '12px',
                backgroundColor: '#D1FAE5',
                borderRadius: '8px'
              }}>
                <span style={{ fontSize: '14px', color: '#065F46' }}>Đang hoạt động</span>
                <span style={{ fontSize: '18px', fontWeight: '700', color: '#065F46' }}>
                  {quotes.filter(q => q.status === 'Active').length}
                </span>
              </div>

              <div style={{
                display: 'flex',
                justifyContent: 'space-between',
                alignItems: 'center',
                padding: '12px',
                backgroundColor: '#FEF7CD',
                borderRadius: '8px'
              }}>
                <span style={{ fontSize: '14px', color: '#92400E' }}>Đã Tạo Đơn Hàng</span>
                <span style={{ fontSize: '18px', fontWeight: '700', color: '#92400E' }}>
                  {quotes.filter(q => q.status === 'ConvertedToOrder').length}
                </span>
              </div>

              <div style={{
                display: 'flex',
                justifyContent: 'space-between',
                alignItems: 'center',
                padding: '12px',
                backgroundColor: '#FEE2E2',
                borderRadius: '8px'
              }}>
                <span style={{ fontSize: '14px', color: '#DC2626' }}>Đã Hủy</span>
                <span style={{ fontSize: '18px', fontWeight: '700', color: '#DC2626' }}>
                  {quotes.filter(q => q.status === 'Cancelled').length}
                </span>
              </div>
            </div>

            <button
              onClick={() => navigate('/sales/quote/new')}
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
                marginTop: '16px',
                fontSize: '14px'
              }}
            >
              <PlusIcon />
              Tạo báo giá mới
            </button>
          </div>
        </div>

        {/* Danh sách báo giá */}
        <div style={{
          backgroundColor: 'white',
          borderRadius: '12px',
          border: '1px solid #E2E8F0',
          overflow: 'visible' // Thay đổi để dropdown không bị cắt
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
                Danh sách báo giá
              </h2>
              <p style={{
                fontSize: '14px',
                color: '#64748B',
                margin: 0
              }}>
                {filteredQuotes.length} báo giá được tìm thấy
              </p>
            </div>
          </div>

          <div style={{ overflowX: 'auto' }}>
            <table style={{
              width: '100%',
              borderCollapse: 'collapse',
              minWidth: '1200px', // Adjusted min-width after adding Notes column
              tableLayout: 'fixed'
            }}>
              <colgroup>
                <col style={{ width: '5%' }} />
                <col style={{ width: '8%' }} />
                <col style={{ width: '12%' }} />
                <col style={{ width: '12%' }} />
                <col style={{ width: '8%' }} />
                <col style={{ width: '12%' }} />
                <col style={{ width: '10%' }} />
                <col style={{ width: '12%' }} />
                <col style={{ width: '21%' }} /> {/* Added width for Notes column */}
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
                    {/* Tiêu đề cho cột 3 chấm, có thể để trống hoặc icon */}
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
                    Mã báo giá
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
                    ID Xe
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
                    Số lượng
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
                    Ngày tạo
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
                    Ghi chú {/* Re-added "Ghi chú" column header */}
                  </th>
                </tr>
              </thead>
              <tbody>
                {filteredQuotes.length > 0 ? (
                  filteredQuotes.map((quote, index) => (
                    <tr
                      key={quote.id}
                      style={{
                        borderBottom: index < filteredQuotes.length - 1 ? '1px solid #F1F5F9' : 'none'
                      }}
                    >
                      {/* Cột cho dấu 3 chấm */}
                      <td style={{
                        padding: '16px 12px',
                        whiteSpace: 'nowrap',
                        fontSize: '14px',
                        // Đặt overflow: 'visible' để đảm bảo dropdown không bị cắt
                        overflow: 'visible', 
                        textOverflow: 'ellipsis',
                        position: 'relative' // Để dropdown có thể hiển thị đúng
                      }}>
                        {/* Hiện nút 3 chấm theo role và trạng thái */}
                        {(() => {
                          let showMenu = false;
                          // Admin luôn thấy
                          if (isAdmin) showMenu = true;
                          // Staff thấy nếu trạng thái là Active (để chỉnh sửa/hủy/tạo đơn hàng)
                          else if (isStaff) showMenu = quote.status === 'Active';
                          // Manager thấy nếu trạng thái là Active (để hủy)
                          else if (isManager) showMenu = quote.status === 'Active';
                          
                          return showMenu && (
                          <div ref={el => dropdownRefs.current[quote.id] = el} style={{ position: 'relative', display: 'inline-block' }}>
                            <button
                              onClick={(e) => {
                                e.stopPropagation(); // Ngăn chặn sự kiện click lan ra ngoài
                                // Tính vị trí để render menu bên phải nút
                                const rect = e.currentTarget.getBoundingClientRect();
                                if (activeDropdown === quote.id) {
                                  setActiveDropdown(null);
                                  setDropdownPos(null);
                                } else {
                                  setActiveDropdown(quote.id);
                                  setDropdownPos({ top: rect.bottom + window.scrollY, left: rect.right + window.scrollX });
                                }
                              }}
                              style={{
                                background: 'none',
                                border: 'none',
                                cursor: 'pointer',
                                padding: '4px',
                                borderRadius: '4px',
                                display: 'flex',
                                alignItems: 'center',
                                justifyContent: 'center',
                                color: '#64748B',
                                transition: 'background-color 0.2s'
                              }}
                              onMouseOver={(e) => e.currentTarget.style.backgroundColor = '#F3F4F6'}
                              onMouseOut={(e) => e.currentTarget.style.backgroundColor = 'transparent'}
                            >
                              <MoreVerticalIcon />
                            </button>
                          </div>
                          )
                        })()}
                      </td>
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
                          {quote.id}
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
                            {quote.customerId}
                          </div>
                          {/* You might want to fetch customer name here */}
                        </div>
                      </td>
                      <td style={{
                        padding: '16px 12px',
                        whiteSpace: 'nowrap',
                        overflow: 'hidden',
                        textOverflow: 'ellipsis'
                      }}>
                        <span style={{
                          color: '#0F172A',
                          fontSize: '14px',
                          overflow: 'hidden',
                          textOverflow: 'ellipsis',
                          whiteSpace: 'nowrap'
                        }}>
                          {quote.vehicleId}
                        </span>
                      </td>
                      <td style={{
                        padding: '16px 12px',
                        whiteSpace: 'nowrap',
                        overflow: 'hidden',
                        textOverflow: 'ellipsis'
                      }}>
                        <span style={{
                          color: '#0F172A',
                          fontSize: '14px',
                          overflow: 'hidden',
                          textOverflow: 'ellipsis',
                          whiteSpace: 'nowrap'
                        }}>
                          {quote.quantity}
                        </span>
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
                          overflow: 'hidden',
                          textOverflow: 'ellipsis',
                          whiteSpace: 'nowrap'
                        }}>
                          {formatCurrency(quote.totalBasePrice)} {/* Changed to totalBasePrice */}
                        </div>
                      </td>
                      <td style={{
                        padding: '16px 12px',
                        whiteSpace: 'nowrap',
                        overflow: 'hidden',
                        textOverflow: 'ellipsis'
                      }}>
                        <span style={{
                          fontSize: '14px',
                          color: quote.status === 'Active' ? '#0369A1' : (quote.status === 'ConvertedToOrder' ? '#F59E0B' : '#EF4444'), // Xanh dương cho Active, vàng cho đã tạo, đỏ cho hủy
                          fontWeight: '500',
                          overflow: 'hidden',
                          textOverflow: 'ellipsis',
                          whiteSpace: 'nowrap'
                        }}>
                          {getVietnameseStatus(quote.status)}
                        </span>
                      </td>
                      <td style={{
                        padding: '16px 12px',
                        whiteSpace: 'nowrap',
                        overflow: 'hidden',
                        textOverflow: 'ellipsis'
                      }}>
                        <span style={{
                          color: '#0F172A',
                          fontSize: '14px',
                          overflow: 'hidden',
                          textOverflow: 'ellipsis',
                          whiteSpace: 'nowrap'
                        }}>
                          {formatDate(quote.createdAt)}
                        </span>
                      </td>
                      <td style={{
                        padding: '16px 12px',
                        overflow: 'hidden',
                        textOverflow: 'ellipsis',
                        whiteSpace: 'nowrap' // Giữ nguyên một dòng, ẩn phần thừa
                      }}>
                        <span
                          title={quote.notes || 'N/A'}
                          aria-label={quote.notes || 'N/A'}
                          style={{
                            color: '#0F172A',
                            fontSize: '14px',
                            overflow: 'hidden',
                            textOverflow: 'ellipsis',
                            whiteSpace: 'nowrap',
                            cursor: 'help'
                          }}
                        >
                          {quote.notes || 'N/A'} {/* Re-added "Ghi chú" data cell */}
                        </span>
                      </td>
                    </tr>
                  ))
                ) : (
                  <tr>
                    <td colSpan="9" style={{ // Adjusted colSpan to 9
                      padding: '48px 24px',
                      textAlign: 'center'
                    }}>
                      <div style={{
                        color: '#94A3B8'
                      }}>
                        Không tìm thấy báo giá nào
                      </div>
                    </td>
                  </tr>
                )}
              </tbody>
            </table>
          </div>
          </div>
          {/* Portal-rendered dropdown menu so it's never clipped by table wrappers */}
          {activeDropdown && dropdownPos && createPortal(
            <div
              ref={portalRef}
              style={{
                position: 'absolute',
                top: dropdownPos.top + 8,
                left: dropdownPos.left + 8,
                backgroundColor: 'white',
                border: '1px solid #E2E8F0',
                borderRadius: '8px',
                boxShadow: '0 6px 12px rgba(0,0,0,0.12)',
                zIndex: 9999,
                minWidth: '180px',
                overflow: 'hidden'
              }}
            >
              {/* Render menu options based on role */}
              {activeQuote && (
                <>
                  {/* Common action: View Detail */}
                  <button
                    onClick={() => { handleViewQuoteDetail(activeDropdown); setActiveDropdown(null); setDropdownPos(null); }}
                    style={menuButtonStyle}
                  >
                    <EyeIcon />
                    <span>Xem chi tiết</span>
                  </button>

                  {/* Actions for Active status */}
                  {(activeQuote.status === 'Active') && (isStaff || isAdmin || isManager) && (
                    <>
                      {/* Removed Edit button */}
                      <button
                        onClick={() => { handleCreateOrderFromQuote(activeDropdown); setActiveDropdown(null); setDropdownPos(null); }}
                        style={menuButtonStyle}
                      >
                        <PlusIcon />
                        <span>Tạo đơn hàng</span>
                      </button>
                      <button
                        onClick={() => { handleCancelQuote(activeDropdown); setActiveDropdown(null); setDropdownPos(null); }}
                        style={{ ...menuButtonStyle, color: '#EF4444' }}
                      >
                        <XCircleIcon />
                        <span>Hủy báo giá</span>
                      </button>
                    </>
                  )}
                </>
              )}
            </div>,
            document.body
          )}
      </div>

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
          /* Đã bỏ overflow: hidden ở đây để không ảnh hưởng đến dropdown */
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

