import React, { useState, useEffect } from 'react';
import { useParams, useNavigate } from 'react-router-dom';
import api from '../../services/api';
import { useAuth } from '../../context/AuthContext';

// Icons
const BackIcon = () => (
  <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
    <path d="M19 12H5M12 19l-7-7 7-7"/>
  </svg>
);

const CheckCircleIcon = () => (
    <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
        <path d="M22 11.08V12a10 10 0 1 1-5.93-9.14" />
        <polyline points="22 4 12 14.01 9 11.01" />
    </svg>
);

const XCircleIcon = () => (
    <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
        <circle cx="12" cy="12" r="10" />
        <line x1="15" y1="9" x2="9" y2="15" />
        <line x1="9" y1="9" x2="15" y2="15" />
    </svg>
);


export default function ContractDetail() {
  const { contractId } = useParams();
  const navigate = useNavigate();
  const { user, isManager, isAdmin } = useAuth();
  
  const [contract, setContract] = useState(null);
  const [order, setOrder] = useState(null);
  const [customer, setCustomer] = useState(null);
  const [vehicle, setVehicle] = useState(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(null);

  const formatCurrency = (amount) => {
    return new Intl.NumberFormat('vi-VN', { style: 'currency', currency: 'VND' }).format(amount || 0);
  };

  useEffect(() => {
    const fetchContractDetails = async () => {
      try {
        setLoading(true);
        // 1. Fetch Contract
        const fetchedContract = await api.get(`/Contracts/${contractId}`);
        setContract(fetchedContract);

        // 2. Fetch Order
        const fetchedOrder = await api.get(`/Orders/${fetchedContract.orderId}`);
        setOrder(fetchedOrder);

        // 3. Fetch Customer and Vehicle in parallel
        const [customerRes, vehicleRes] = await Promise.all([
          api.get(`/customers/${fetchedOrder.customerId}`),
          api.get(`/vehicles/${fetchedOrder.vehicleId}`)
        ]);
        setCustomer(customerRes);
        setVehicle(vehicleRes);

      } catch (err) {
        console.error('Error fetching contract data:', err);
        setError('Không thể tải dữ liệu hợp đồng.');
      } finally {
        setLoading(false);
      }
    };

    fetchContractDetails();
  }, [contractId]);

  const handleUpdateStatus = async (status) => {
    const actionText = status === 'Approved' ? 'Duyệt' : 'Từ chối';
    if (!window.confirm(`Bạn có chắc chắn muốn ${actionText.toLowerCase()} hợp đồng này?`)) return;

    try {
        setLoading(true);
        await api.put(`/Contracts/${contractId}/status`, { status });
        alert(`Hợp đồng đã được ${actionText.toLowerCase()} thành công!`);
        const orderIdentifier = order?.orderId ?? order?.OrderId ?? contract?.orderId;
        if (status === 'Rejected' && orderIdentifier) {
            navigate('/sales', { state: { removedOrderId: orderIdentifier } });
        } else {
            navigate('/sales');
        }
    } catch (err) {
        console.error(`Error updating contract status:`, err);
        alert(`Lỗi khi ${actionText.toLowerCase()} hợp đồng. ${err.response?.data?.message || err.message}`);
    } finally {
        setLoading(false);
    }
  };


  if (loading) return <div className="text-center py-10">Đang tải dữ liệu...</div>;
  if (error) return <div className="text-center py-10 text-red-500">Lỗi: {error}</div>;
  if (!contract || !order || !customer || !vehicle) return <div className="text-center py-10">Không tìm thấy thông tin.</div>;

  const canApprove = (isManager || isAdmin);

  return (
    <div style={{ minHeight: '100vh', backgroundColor: '#F8FAFC', fontFamily: '-apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif' }}>
      <div style={{ maxWidth: '1000px', margin: '0 auto', padding: '24px' }}>
        {/* Header */}
        <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', marginBottom: '24px' }}>
          <div style={{ display: 'flex', alignItems: 'center', gap: '12px' }}>
            <button onClick={() => navigate('/sales')} style={{ display: 'flex', alignItems: 'center', gap: '8px', padding: '8px 16px', backgroundColor: 'white', border: '1px solid #D1D5DB', borderRadius: '8px', cursor: 'pointer', fontSize: '14px', fontWeight: '500', color: '#374151' }}>
              <BackIcon /> Quay lại
            </button>
            <h1 style={{ fontSize: '24px', fontWeight: '700', color: '#0F172A', margin: 0 }}>
              Chi Tiết Hợp Đồng #{contract.contractNumber}
            </h1>
          </div>
          {canApprove && contract.status === 'PendingApproval' && (
            <div style={{ display: 'flex', gap: '12px' }}>
                <button onClick={() => handleUpdateStatus('Approved')} disabled={loading} style={{ display: 'flex', alignItems: 'center', gap: '8px', padding: '10px 20px', backgroundColor: loading ? '#9CA3AF' : '#10B981', color: 'white', border: 'none', borderRadius: '8px', cursor: loading ? 'not-allowed' : 'pointer', fontSize: '14px', fontWeight: '500' }}>
                    <CheckCircleIcon /> {loading ? 'Đang xử lý...' : 'Duyệt'}
                </button>
                <button onClick={() => handleUpdateStatus('Rejected')} disabled={loading} style={{ display: 'flex', alignItems: 'center', gap: '8px', padding: '10px 20px', backgroundColor: loading ? '#9CA3AF' : '#EF4444', color: 'white', border: 'none', borderRadius: '8px', cursor: loading ? 'not-allowed' : 'pointer', fontSize: '14px', fontWeight: '500' }}>
                    <XCircleIcon /> {loading ? 'Đang xử lý...' : 'Từ chối'}
                </button>
            </div>
          )}
        </div>

        <div style={{ backgroundColor: 'white', borderRadius: '12px', padding: '32px', border: '1px solid #E2E8F0', boxShadow: '0 1px 3px 0 rgba(0, 0, 0, 0.05)' }}>
          {/* Contract Header */}
          <div style={{ textAlign: 'center', marginBottom: '32px' }}>
            <h2 style={{ fontSize: '28px', fontWeight: 'bold', color: '#1E293B' }}>HỢP ĐỒNG MUA BÁN XE</h2>
            <p style={{ fontSize: '14px', color: '#64748B' }}>Số: {contract.contractNumber}</p>
             <p style={{ fontSize: '14px', color: '#64748B' }}>Trạng thái: <span style={{fontWeight: 'bold'}}>{contract.status}</span></p>
          </div>

          {/* Parties */}
          <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '32px', marginBottom: '32px' }}>
            <div>
              <h3 style={{ fontSize: '16px', fontWeight: '600', borderBottom: '2px solid #3B82F6', paddingBottom: '8px', marginBottom: '16px' }}>BÊN BÁN (BÊN A)</h3>
              <p><strong>Tên công ty:</strong> CÔNG TY TNHH EV-DEALER</p>
              <p><strong>Địa chỉ:</strong> 123 Đường ABC, Quận 1, TP. HCM</p>
              <p><strong>Đại diện:</strong> {user?.fullName || '...'}</p>
              <p><strong>Chức vụ:</strong> Nhân viên kinh doanh</p>
            </div>
            <div>
              <h3 style={{ fontSize: '16px', fontWeight: '600', borderBottom: '2px solid #3B82F6', paddingBottom: '8px', marginBottom: '16px' }}>BÊN MUA (BÊN B)</h3>
              <p><strong>Tên khách hàng:</strong> {customer.name}</p>
              <p><strong>Địa chỉ:</strong> {customer.address}</p>
              <p><strong>Số điện thoại:</strong> {customer.phone}</p>
              <p><strong>Email:</strong> {customer.email}</p>
            </div>
          </div>

          {/* Vehicle Details */}
          <div style={{ marginBottom: '32px' }}>
            <h3 style={{ fontSize: '18px', fontWeight: '600', marginBottom: '16px' }}>Điều 1: Đối tượng của hợp đồng</h3>
            <table style={{ width: '100%', borderCollapse: 'collapse', border: '1px solid #E2E8F0' }}>
              <tbody>
                <tr style={{ borderBottom: '1px solid #E2E8F0' }}>
                  <td style={{ padding: '12px', backgroundColor: '#F8FAFC', fontWeight: '500' }}>Loại xe</td>
                  <td style={{ padding: '12px' }}>{vehicle.model}</td>
                </tr>
                <tr>
                  <td style={{ padding: '12px', backgroundColor: '#F8FAFC', fontWeight: '500' }}>Số lượng</td>
                  <td style={{ padding: '12px' }}>{order.quantity}</td>
                </tr>
              </tbody>
            </table>
          </div>

          {/* Payment Details */}
          <div style={{ marginBottom: '32px' }}>
            <h3 style={{ fontSize: '18px', fontWeight: '600', marginBottom: '16px' }}>Điều 2: Giá trị hợp đồng và phương thức thanh toán</h3>
            <p><strong>Tổng giá trị hợp đồng (đã bao gồm VAT):</strong> <span style={{ fontWeight: 'bold', fontSize: '18px', color: '#1D4ED8' }}>{formatCurrency(order.subTotal)}</span></p>
            <p><strong>Hình thức thanh toán:</strong> {order.paymentType === 'Installment' ? 'Trả góp' : 'Trả thẳng'}</p>
            {order.paymentType === 'Installment' && (
              <div style={{ paddingLeft: '20px', marginTop: '10px' }}>
                <p><strong>Số tiền trả trước:</strong> {formatCurrency(order.depositAmount)}</p>
                <p><strong>Lãi suất:</strong> {order.interestRateYearly}%/năm</p>
                <p><strong>Kỳ hạn vay:</strong> {order.loanTermMonths} tháng</p>
              </div>
            )}
          </div>

          {/* Terms and Conditions */}
          <div style={{ marginBottom: '32px' }}>
            <h3 style={{ fontSize: '18px', fontWeight: '600', marginBottom: '16px' }}>Điều 3: Điều khoản và điều kiện</h3>
            <div style={{ width: '100%', padding: '12px', border: '1px solid #CBD5E1', borderRadius: '8px', fontSize: '14px', backgroundColor: '#F9FAFB' }}>
                {contract.notes || 'Không có điều khoản bổ sung.'}
            </div>
          </div>
          
          {/* Other Fields */}
          <div style={{ display: 'flex', gap: '24px', marginBottom: '32px' }}>
            <div style={{ flex: 1 }}>
              <label style={{ display: 'block', fontSize: '14px', fontWeight: '500', color: '#374151', marginBottom: '8px' }}>Ngày lập hợp đồng</label>
              <input
                type="date"
                value={contract.signedDate ? new Date(contract.signedDate).toISOString().slice(0, 10) : ''}
                readOnly
                style={{ width: '100%', padding: '10px 12px', border: '1px solid #CBD5E1', borderRadius: '8px', fontSize: '14px', backgroundColor: '#F9FAFB' }}
              />
            </div>
            <div style={{ flex: 1, display: 'flex', alignItems: 'center', paddingTop: '24px' }}>
              <input
                type="checkbox"
                id="depositReceived"
                checked={contract.paymentStatus === 'Partial'}
                readOnly
                style={{ width: '16px', height: '16px', marginRight: '8px' }}
              />
              <label htmlFor="depositReceived" style={{ fontSize: '14px', fontWeight: '500', color: '#374151' }}>
                Đã nhận tiền cọc
              </label>
            </div>
          </div>

          {/* Signatures */}
          <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '32px', marginTop: '48px', paddingTop: '32px', borderTop: '1px dashed #CBD5E1' }}>
            <div style={{ textAlign: 'center' }}>
              <h4 style={{ fontWeight: 'bold', marginBottom: '80px' }}>ĐẠI DIỆN BÊN A</h4>
              <p style={{ fontWeight: '600' }}>{user?.fullName || '...'}</p>
            </div>
            <div style={{ textAlign: 'center' }}>
              <h4 style={{ fontWeight: 'bold', marginBottom: '80px' }}>ĐẠI DIỆN BÊN B</h4>
              <p style={{ fontWeight: '600' }}>{customer.name}</p>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}
