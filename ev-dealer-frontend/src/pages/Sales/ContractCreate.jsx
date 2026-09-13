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

const SaveIcon = () => (
  <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
    <path d="M19 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h11l5 5v11a2 2 0 0 1-2 2z"></path>
    <polyline points="17 21 17 13 7 13 7 21"></polyline>
    <polyline points="7 3 7 8 15 8"></polyline>
  </svg>
);

export default function ContractCreate() {
  const { orderId } = useParams();
  const navigate = useNavigate();
  const { user } = useAuth();
  
  const [order, setOrder] = useState(null);
  const [customer, setCustomer] = useState(null);
  const [vehicle, setVehicle] = useState(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(null);

  // Contract fields
  const [contractDate, setContractDate] = useState(new Date().toISOString().slice(0, 10));
  const [terms, setTerms] = useState('');
  const [depositReceived, setDepositReceived] = useState(false);

  const formatCurrency = (amount) => {
    return new Intl.NumberFormat('vi-VN', { style: 'currency', currency: 'VND' }).format(amount);
  };

  useEffect(() => {
    const fetchOrderDetails = async () => {
      try {
        setLoading(true);
        // 1. Fetch Order
        const fetchedOrder = await api.get(`/Orders/${orderId}`); // api interceptor unwraps response.data
        setOrder(fetchedOrder);

        // 2. Fetch Customer and Vehicle in parallel
        const [customer, vehicle] = await Promise.all([
          api.get(`/customers/${fetchedOrder.customerId}`),
          api.get(`/vehicles/${fetchedOrder.vehicleId}`)
        ]);
        setCustomer(customer);
        setVehicle(vehicle);

      } catch (err) {
        console.error('Error fetching contract data:', err);
        setError('Không thể tải dữ liệu để tạo hợp đồng.');
      } finally {
        setLoading(false);
      }
    };

    fetchOrderDetails();
  }, [orderId]);

  const handleCreateContract = async () => {
    if (!contractDate || !terms) {
      alert('Vui lòng điền đầy đủ các trường bắt buộc.');
      return;
    }

    const payload = {
      orderId: parseInt(orderId),
      customerId: order.customerId,
      salespersonId: String(user.id), // Convert salespersonId to string
      contractDate: contractDate,
      termsAndConditions: terms,
      depositAmountReceived: depositReceived,
      // The backend will set the status and other fields
    };

    console.log("Sending contract payload:", payload);

    try {
      setLoading(true);
      // *** FIX: Call API Gateway (port 5036) instead of direct service port ***
      await api.post('/Contracts', payload);
      alert('Hợp đồng đã được tạo thành công và đang chờ duyệt!');
      navigate('/sales'); // Navigate back to sales list
    } catch (err) {
      console.error('Error creating contract:', err);
      alert(`Tạo hợp đồng thất bại. Lỗi: ${err.response?.data?.message || err.message}`);
    } finally {
      setLoading(false);
    }
  };

  if (loading) return <div className="text-center py-10">Đang tải dữ liệu...</div>;
  if (error) return <div className="text-center py-10 text-red-500">Lỗi: {error}</div>;
  if (!order || !customer || !vehicle) return <div className="text-center py-10">Không tìm thấy thông tin.</div>;

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
              Tạo Hợp Đồng cho Đơn Hàng #{orderId}
            </h1>
          </div>
          <button onClick={handleCreateContract} disabled={loading} style={{ display: 'flex', alignItems: 'center', gap: '8px', padding: '10px 20px', backgroundColor: loading ? '#9CA3AF' : '#2563EB', color: 'white', border: 'none', borderRadius: '8px', cursor: loading ? 'not-allowed' : 'pointer', fontSize: '14px', fontWeight: '500' }}>
            <SaveIcon /> {loading ? 'Đang lưu...' : 'Lưu Hợp Đồng'}
          </button>
        </div>

        <div style={{ backgroundColor: 'white', borderRadius: '12px', padding: '32px', border: '1px solid #E2E8F0', boxShadow: '0 1px 3px 0 rgba(0, 0, 0, 0.05)' }}>
          {/* Contract Header */}
          <div style={{ textAlign: 'center', marginBottom: '32px' }}>
            <h2 style={{ fontSize: '28px', fontWeight: 'bold', color: '#1E293B' }}>HỢP ĐỒNG MUA BÁN XE</h2>
            <p style={{ fontSize: '14px', color: '#64748B' }}>Số: ....../HĐMB-2024</p>
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
            <textarea
              value={terms}
              onChange={(e) => setTerms(e.target.value)}
              placeholder="Nhập các điều khoản và điều kiện của hợp đồng..."
              rows={8}
              style={{ width: '100%', padding: '12px', border: '1px solid #CBD5E1', borderRadius: '8px', fontSize: '14px' }}
            />
          </div>
          
          {/* Other Fields */}
          <div style={{ display: 'flex', gap: '24px', marginBottom: '32px' }}>
            <div style={{ flex: 1 }}>
              <label style={{ display: 'block', fontSize: '14px', fontWeight: '500', color: '#374151', marginBottom: '8px' }}>Ngày lập hợp đồng</label>
              <input
                type="date"
                value={contractDate}
                onChange={(e) => setContractDate(e.target.value)}
                style={{ width: '100%', padding: '10px 12px', border: '1px solid #CBD5E1', borderRadius: '8px', fontSize: '14px' }}
              />
            </div>
            <div style={{ flex: 1, display: 'flex', alignItems: 'center', paddingTop: '24px' }}>
              <input
                type="checkbox"
                id="depositReceived"
                checked={depositReceived}
                onChange={(e) => setDepositReceived(e.target.checked)}
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
