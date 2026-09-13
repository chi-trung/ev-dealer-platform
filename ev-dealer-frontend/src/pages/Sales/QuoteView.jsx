import React, { useState, useEffect } from 'react';
import { useParams, useNavigate } from 'react-router-dom';
import api from '../../services/api'; // Import api instance

// Reuse icons from QuoteCreate style
const BackIcon = () => (
  <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
    <path d="M19 12H5M12 19l-7-7 7-7"/>
  </svg>
);

const DownloadIcon = () => (
  <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
    <path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4"/>
    <polyline points="7 10 12 15 17 10"/>
    <line x1="12" y1="15" x2="12" y2="3"/>
  </svg>
);

export default function QuoteView() {
  const { id } = useParams();
  const navigate = useNavigate();
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(null);
  const [quote, setQuote] = useState(null);
  const [customer, setCustomer] = useState(null);
  const [vehicle, setVehicle] = useState(null);
  const [salesperson, setSalesperson] = useState(null); // State for salesperson info

  useEffect(() => {
    const fetchData = async () => {
      setLoading(true);
      setError(null);
      try {
        // Fetch quote details from SalesService
        const quoteData = await api.get(`/Quotes/${id}`); // api.get already returns response.data

        if (!quoteData || !quoteData.customerId) {
            throw new Error("Invalid quote data received from API.");
        }

        setQuote(quoteData);

        // Fetch dependent data in parallel
        const [customerData, vehicleData, salespersonData] = await Promise.all([
            api.get(`/customers/${quoteData.customerId}`).catch(() => ({ id: quoteData.customerId, name: 'N/A' })),
            api.get(`/vehicles/${quoteData.vehicleId}`).catch(() => ({ id: quoteData.vehicleId, model: 'N/A' })),
            api.get(`/users/${quoteData.salespersonId}`).catch(() => ({ id: quoteData.salespersonId, fullName: 'N/A' })) // Fetch salesperson from UserService
        ]);
        
        setCustomer(customerData); // api.get already returns response.data
        setVehicle(vehicleData); // api.get already returns response.data
        setSalesperson(salespersonData); // api.get already returns response.data

      } catch (err) {
        console.error('Error fetching data:', err);
        setError('Không thể tải thông tin báo giá.');
      } finally {
        setLoading(false);
      }
    };
    fetchData();
  }, [id]);

  const formatCurrency = (amount) => {
    return new Intl.NumberFormat('vi-VN', { style: 'currency', currency: 'VND' }).format(amount || 0);
  };
  
  const formatDate = (dateString) => {
    if (!dateString) return 'N/A';
    const date = new Date(dateString);
    return date.toLocaleDateString('vi-VN');
  };


  const calculateTotals = () => {
    if (!quote) return { total: 0, downPayment: 0, monthly: 0, installmentTotal: 0 };
    // Use totalBasePrice from Quote model
    const total = quote.totalBasePrice || 0; 
    
    // --- Payment fields from Quote model ---
    // These fields are currently NOT in Quote model, need to add them if installment is supported
    // For now, defaulting to 0 or 'Full' if not present in quote object
    const paymentType = quote.paymentType || 'Full'; // Assuming Quote model has this
    const depositAmount = quote.depositAmount || 0; // Assuming Quote model has this
    const loanTermMonths = quote.loanTermMonths || 0; // Assuming Quote model has this
    const interestRateYearly = quote.interestRateYearly || 0; // Assuming Quote model has this

    // Calculate down payment based on depositAmount
    const down = depositAmount; 
    const loanAmount = total - down;
    let monthly = 0;
    
    if (paymentType === 'Installment' && loanTermMonths > 0 && interestRateYearly > 0) {
      const monthlyRate = (interestRateYearly / 100) / 12;
      const n = loanTermMonths;
      if (monthlyRate === 0) monthly = loanAmount / n;
      else monthly = loanAmount * (monthlyRate * Math.pow(1 + monthlyRate, n)) / (Math.pow(1 + monthlyRate, n) - 1);
    }
    const installmentTotal = paymentType === 'Installment' ? (down + monthly * (loanTermMonths > 0 ? loanTermMonths : 0)) : total;
    
    return { 
      total, 
      down, 
      monthly, 
      installmentTotal, 
      paymentType,
      depositAmount,
      loanTermMonths,
      interestRateYearly
    };
  };

  const handleDownloadPdf = async () => {
    if (!quote) return;
    setLoading(true);
    try {
      const totals = calculateTotals(); // Recalculate totals for PDF payload

      const payload = {
        customerInfo: {
          id: customer?.id || quote.customerId,
          name: customer?.name || '',
          phone: customer?.phone || '',
          email: customer?.email || '',
          address: customer?.address || ''
        },
        quoteItems: [
          {
            vehicleId: quote.vehicleId,
            vehicleName: vehicle?.model || '',
            quantity: quote.quantity,
            unitPrice: quote.basePrice, // Use basePrice from Quote model
            discountPercent: 0, // Discount is not directly in Quote model, need to adjust if needed
            itemTotal: quote.totalBasePrice // Use totalBasePrice
          }
        ],
        paymentInfo: {
          type: totals.paymentType,
          downPaymentPercent: totals.depositAmount > 0 && totals.total > 0 ? (totals.depositAmount / totals.total) * 100 : 0,
          loanTerm: totals.loanTermMonths,
          interestRate: totals.interestRateYearly
        },
        additionalInfo: {
          deliveryDate: '', // Not in Quote model
          notes: quote.notes || '', // Use notes from Quote model
          salesPerson: salesperson?.fullName || salesperson?.name || 'N/A', // Use fetched salesperson name
          validUntil: '' // Not in Quote model
        },
        totalCalculatedAmount: totals.total,
        downPaymentCalculated: totals.down,
        loanAmountCalculated: totals.total - totals.down,
        monthlyPaymentCalculated: totals.monthly,
        installmentTotalPaymentCalculated: totals.installmentTotal
      };

      // Interceptor unwraps response.data, so this resolves to the blob itself
      // (timeout raised above the shared 10s: server-side PDF rendering is slow,
      // and this call previously ran on raw axios with no timeout at all)
      const blobData = await api.post('/Sales/generate-quote-pdf', payload, { responseType: 'blob', timeout: 120000 });
      const file = new Blob([blobData], { type: 'application/pdf' });
      const fileURL = URL.createObjectURL(file);
      const link = document.createElement('a');
      link.href = fileURL;
      link.setAttribute('download', `BaoGiaXeDien_${quote.id || 'quote'}.pdf`);
      document.body.appendChild(link);
      link.click();
      link.remove();
      URL.revokeObjectURL(fileURL);
    } catch (err) {
      console.error('Error generating PDF:', err);
      alert('Tạo PDF thất bại.');
    } finally {
      setLoading(false);
    }
  };

  if (loading) return (<div style={{ display: 'flex', justifyContent: 'center', alignItems: 'center', height: '100vh' }}>Đang tải dữ liệu...</div>);
  if (error) return (<div style={{ padding: 24, color: 'red' }}>{error}</div>);

  const totals = calculateTotals();

  return (
    <div style={{ 
      minHeight: '100vh', 
      backgroundColor: '#F8FAFC',
      fontFamily: '-apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif'
    }}>
      <div style={{ maxWidth: '1200px', margin: '0 auto', padding: '24px' }}>
        {/* Header */}
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
              onClick={() => navigate(-1)}
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
            >
              <BackIcon />
              Quay lại
            </button>
            <div>
              <h1 style={{ fontSize: '24px', fontWeight: '700', color: '#0F172A', margin: 0 }}>
                Chi tiết Báo giá #{quote?.id}
              </h1>
            </div>
          </div>
          <div style={{ display: 'flex', gap: '12px' }}>
            <button 
              onClick={handleDownloadPdf}
              style={{
                display: 'flex',
                alignItems: 'center',
                gap: '8px',
                padding: '10px 20px',
                backgroundColor: 'white',
                color: '#374151',
                border: '1px solid #D1D5DB',
                borderRadius: '8px',
                cursor: 'pointer',
                transition: 'all 0.2s',
                fontSize: '14px',
                fontWeight: '500'
              }}
            >
              <DownloadIcon />
              Tải PDF
            </button>
          </div>
        </div>

        <div style={{ display: 'grid', gridTemplateColumns: '2fr 1fr', gap: '24px' }}>
          {/* Main Content */}
          <div style={{ minWidth: 0 }}>
            {/* Customer Information */}
            <div style={{
              backgroundColor: 'white',
              borderRadius: '12px',
              padding: '24px',
              marginBottom: '24px',
              border: '1px solid #E2E8F0',
              boxShadow: '0 1px 3px 0 rgba(0, 0, 0, 0.05)'
            }}>
              <h2 style={{ fontSize: '18px', fontWeight: '600', color: '#0F172A', marginBottom: '20px' }}>
                Thông tin khách hàng
              </h2>

              <div style={{ display: 'grid', gridTemplateColumns: 'repeat(2, 1fr)', gap: '16px' }}>
                <div>
                  <label style={{ display: 'block', fontSize: '14px', fontWeight: '500', color: '#374151', marginBottom: '8px' }}>
                    Họ và tên
                  </label>
                  <input value={customer?.name || ''} readOnly style={{ width: '100%', padding: '10px 12px', border: '1px solid #CBD5E1', borderRadius: '8px', fontSize: '14px', backgroundColor: '#F1F5F9', height: '40px', boxSizing: 'border-box', color: '#374151' }} />
                </div>

                <div>
                  <label style={{ display: 'block', fontSize: '14px', fontWeight: '500', color: '#374151', marginBottom: '8px' }}>
                    Số điện thoại
                  </label>
                  <input value={customer?.phone || ''} readOnly style={{ width: '100%', padding: '10px 12px', border: '1px solid #CBD5E1', borderRadius: '8px', fontSize: '14px', backgroundColor: '#F1F5F9', height: '40px', boxSizing: 'border-box', color: '#374151' }} />
                </div>

                <div>
                  <label style={{ display: 'block', fontSize: '14px', fontWeight: '500', color: '#374151', marginBottom: '8px' }}>
                    Email
                  </label>
                  <input value={customer?.email || ''} readOnly style={{ width: '100%', padding: '10px 12px', border: '1px solid #CBD5E1', borderRadius: '8px', fontSize: '14px', backgroundColor: '#F1F5F9', height: '40px', boxSizing: 'border-box', color: '#374151' }} />
                </div>

                <div>
                  <label style={{ display: 'block', fontSize: '14px', fontWeight: '500', color: '#374151', marginBottom: '8px' }}>
                    Địa chỉ
                  </label>
                  <input value={customer?.address || ''} readOnly style={{ width: '100%', padding: '10px 12px', border: '1px solid #CBD5E1', borderRadius: '8px', fontSize: '14px', backgroundColor: '#F1F5F9', height: '40px', boxSizing: 'border-box', color: '#374151' }} />
                </div>
              </div>
            </div>

            {/* Vehicle Selection (read-only presentation) */}
            <div style={{
              backgroundColor: 'white',
              borderRadius: '12px',
              padding: '24px',
              marginBottom: '24px',
              border: '1px solid #E2E8F0',
              boxShadow: '0 1px 3px 0 rgba(0, 0, 0, 0.05)'
            }}>
              <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '20px' }}>
                <h2 style={{ fontSize: '18px', fontWeight: '600', color: '#0F172A', margin: 0 }}>
                  Chi tiết xe
                </h2>
              </div>

              <div style={{ padding: '20px', border: '1px solid #E2E8F0', borderRadius: '8px', backgroundColor: '#F8FAFC' }}>
                <div style={{ display: 'grid', gridTemplateColumns: '2fr 1fr 1fr 1fr', gap: '12px', alignItems: 'end' }}>
                  <div>
                    <label style={{ display: 'block', fontSize: '14px', fontWeight: '500', color: '#374151', marginBottom: '8px' }}>Xe</label>
                    <input value={vehicle?.model || quote?.vehicleId || ''} readOnly style={{ width: '100%', padding: '10px 12px', border: '1px solid #CBD5E1', borderRadius: '8px', fontSize: '14px', backgroundColor: '#F1F5F9', height: '40px', boxSizing: 'border-box', color: '#374151' }} />
                  </div>

                  <div>
                    <label style={{ display: 'block', fontSize: '14px', fontWeight: '500', color: '#374151', marginBottom: '8px' }}>Số lượng</label>
                    <input value={quote?.quantity || 0} readOnly style={{ width: '100%', padding: '10px 12px', border: '1px solid #CBD5E1', borderRadius: '8px', fontSize: '14px', backgroundColor: '#F1F5F9', height: '40px', boxSizing: 'border-box', color: '#374151' }} />
                  </div>

                  <div>
                    <label style={{ display: 'block', fontSize: '14px', fontWeight: '500', color: '#374151', marginBottom: '8px' }}>Đơn giá</label>
                    <input value={formatCurrency(quote?.basePrice)} readOnly style={{ width: '100%', padding: '10px 12px', border: '1px solid #CBD5E1', borderRadius: '8px', fontSize: '14px', backgroundColor: '#F1F5F9', height: '40px', boxSizing: 'border-box', color: '#374151' }} />
                  </div>

                  <div>
                    <label style={{ display: 'block', fontSize: '14px', fontWeight: '500', color: '#374151', marginBottom: '8px' }}>Giảm giá (%)</label>
                    <input value={quote?.discountPercent ?? 0} readOnly style={{ width: '100%', padding: '10px 12px', border: '1px solid #CBD5E1', borderRadius: '8px', fontSize: '14px', backgroundColor: '#F1F5F9', height: '40px', boxSizing: 'border-box', color: '#374151' }} />
                  </div>
                </div>

                <div style={{ marginTop: '12px', borderTop: '1px solid #E2E8F0', paddingTop: '12px', display: 'flex', justifyContent: 'space-between' }}>
                  <div style={{ color: '#64748B' }}>Thành tiền</div>
                  <div style={{ fontWeight: 700 }}>{formatCurrency(quote?.totalBasePrice)}</div>
                </div>
              </div>
            </div>

            {/* Payment Information */}
            <div style={{
              backgroundColor: 'white',
              borderRadius: '12px',
              padding: '24px',
              marginBottom: '24px',
              border: '1px solid #E2E8F0',
              boxShadow: '0 1px 3px 0 rgba(0, 0, 0, 0.05)'
            }}>
              <h2 style={{ fontSize: '18px', fontWeight: '600', color: '#0F172A', marginBottom: '20px' }}>
                Thông tin thanh toán
              </h2>

              <div style={{ display: 'grid', gridTemplateColumns: 'repeat(3, 1fr)', gap: '16px' }}>
                  <div style={{ minWidth: 0 }}>
                    <label style={{ display: 'block', fontSize: '14px', fontWeight: '500', color: '#374151', marginBottom: '8px' }}>Hình thức thanh toán</label>
                    <input value={totals.paymentType === 'Installment' ? 'Trả góp' : 'Trả thẳng'} readOnly style={{ width: '100%', padding: '10px 12px', border: '1px solid #CBD5E1', borderRadius: '8px', fontSize: '14px', backgroundColor: '#F1F5F9', height: '40px', boxSizing: 'border-box', color: '#374151' }} />
                  </div>
                  {totals.paymentType === 'Installment' && (
                    <>
                        <div style={{ minWidth: 0 }}>
                            <label style={{ display: 'block', fontSize: '14px', fontWeight: '500', color: '#374151', marginBottom: '8px' }}>Tiền trả trước</label>
                            <input value={formatCurrency(totals.down)} readOnly style={{ width: '100%', padding: '10px 12px', border: '1px solid #CBD5E1', borderRadius: '8px', fontSize: '14px', backgroundColor: '#F1F5F9', height: '40px', boxSizing: 'border-box', color: '#374151' }} />
                        </div>
                        <div style={{ minWidth: 0 }}>
                            <label style={{ display: 'block', fontSize: '14px', fontWeight: '500', color: '#374151', marginBottom: '8px' }}>Kỳ hạn (tháng)</label>
                            <input value={totals.loanTermMonths ?? ''} readOnly style={{ width: '100%', padding: '10px 12px', border: '1px solid #CBD5E1', borderRadius: '8px', fontSize: '14px', backgroundColor: '#F1F5F9', height: '40px', boxSizing: 'border-box', color: '#374151' }} />
                        </div>
                        <div style={{ minWidth: 0 }}>
                            <label style={{ display: 'block', fontSize: '14px', fontWeight: '500', color: '#374151', marginBottom: '8px' }}>Lãi suất (%/năm)</label>
                            <input value={totals.interestRateYearly ?? ''} readOnly style={{ width: '100%', padding: '10px 12px', border: '1px solid #CBD5E1', borderRadius: '8px', fontSize: '14px', backgroundColor: '#F1F5F9', height: '40px', boxSizing: 'border-box', color: '#374151' }} />
                        </div>
                    </>
                  )}
              </div>
            </div>
          </div>

          {/* Sidebar */}
          <div style={{ minWidth: 0 }}>
            <div style={{
              backgroundColor: 'white',
              borderRadius: '12px',
              padding: '24px',
              marginBottom: '24px',
              border: '1px solid #E2E8F0',
              boxShadow: '0 1px 3px 0 rgba(0, 0, 0, 0.05)'
            }}>
              <h2 style={{ fontSize: '18px', fontWeight: '600', color: '#0F172A', marginBottom: '20px' }}>
                Thông tin nhân viên
              </h2>
              <div style={{ marginBottom: '16px' }}>
                <label style={{ display: 'block', fontSize: '14px', fontWeight: '500', color: '#374151', marginBottom: '8px' }}>ID nhân viên</label>
                <input type="text" value={quote?.salespersonId || ''} readOnly style={{ width: '100%', padding: '10px 12px', border: '1px solid #CBD5E1', borderRadius: '8px', fontSize: '14px', backgroundColor: '#F1F5F9', height: '40px', boxSizing: 'border-box', color: '#374151' }} />
              </div>
              <div style={{ marginBottom: '16px' }}>
                <label style={{ display: 'block', fontSize: '14px', fontWeight: '500', color: '#374151', marginBottom: '8px' }}>Tên nhân viên</label>
                <input type="text" value={salesperson?.fullName || salesperson?.name || 'N/A'} readOnly style={{ width: '100%', padding: '10px 12px', border: '1px solid #CBD5E1', borderRadius: '8px', fontSize: '14px', backgroundColor: '#F1F5F9', height: '40px', boxSizing: 'border-box', color: '#374151' }} />
              </div>
              <div style={{ marginBottom: '16px' }}>
                <label style={{ display: 'block', fontSize: '14px', fontWeight: '500', color: '#374151', marginBottom: '8px' }}>Ngày tạo</label>
                <input type="text" value={formatDate(quote?.createdAt)} readOnly style={{ width: '100%', padding: '10px 12px', border: '1px solid #CBD5E1', borderRadius: '8px', fontSize: '14px', backgroundColor: '#F1F5F9', height: '40px', boxSizing: 'border-box', color: '#374151' }} />
              </div>
              <div>
                <label style={{ display: 'block', fontSize: '14px', fontWeight: '500', color: '#374151', marginBottom: '8px' }}>Ghi chú</label>
                <textarea value={quote?.notes || ''} readOnly rows={3} style={{ width: '100%', padding: '10px 12px', border: '1px solid #CBD5E1', borderRadius: '8px', fontSize: '14px', backgroundColor: '#F8FAFC', resize: 'vertical', boxSizing: 'border-box', minHeight: '80px', color: '#374151' }} />
              </div>
            </div>

            <div style={{
              backgroundColor: 'white',
              borderRadius: '12px',
              padding: '24px',
              border: '1px solid #E2E8F0',
              boxShadow: '0 1px 3px 0 rgba(0, 0, 0, 0.05)'
            }}>
              <h2 style={{ fontSize: '18px', fontWeight: '600', color: '#0F172A', marginBottom: '20px' }}>Chi tiết giá</h2>
              <div style={{ marginBottom: '16px' }}>
                <div style={{ display: 'flex', justifyContent: 'space-between', fontSize: '14px', marginBottom: '12px' }}>
                  <span style={{ color: '#64748B' }}>Tổng cộng:</span>
                  <span style={{ color: '#0F172A', fontWeight: '500' }}>{formatCurrency(totals.total)}</span>
                </div>
                {totals.paymentType === 'Installment' && (
                  <>
                    <div style={{ display: 'flex', justifyContent: 'space-between', fontSize: '14px', marginBottom: '12px' }}>
                      <span style={{ color: '#64748B' }}>Trả trước ({totals.depositAmount > 0 && totals.total > 0 ? (totals.depositAmount / totals.total * 100).toFixed(2) : 0}%):</span>
                      <span style={{ color: '#0F172A', fontWeight: '500' }}>{formatCurrency(totals.down)}</span>
                    </div>
                    <div style={{ display: 'flex', justifyContent: 'space-between', fontSize: '14px', marginBottom: '12px' }}>
                      <span style={{ color: '#64748B' }}>Số tiền vay:</span>
                      <span style={{ color: '#0F172A', fontWeight: '500' }}>{formatCurrency(totals.total - totals.down)}</span>
                    </div>
                    <div style={{ display: 'flex', justifyContent: 'space-between', fontSize: '14px', marginBottom: '12px' }}>
                      <span style={{ color: '#64748B' }}>Góp hàng tháng:</span>
                      <span style={{ color: '#0F172A', fontWeight: '500' }}>{formatCurrency(totals.monthly)}</span>
                    </div>
                  </>
                )}
              </div>
              <div style={{ paddingTop: '16px', borderTop: '1px solid #E2E8F0', display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                <span style={{ fontSize: '16px', fontWeight: '600', color: '#0F172A' }}>Tổng thanh toán:</span>
                <span style={{ fontSize: '20px', fontWeight: '700', color: '#3B82F6' }}>{formatCurrency(totals.installmentTotal)}</span>
              </div>
            </div>
          </div>
        </div>
      </div>

      <style>{`
        select, input[type="text"], input[type="tel"], input[type="email"], input[type="number"], input[type="date"] {
          height: 40px !important;
          box-sizing: border-box !important;
          min-width: 100% !important;
          max-width: 100% !important;
          color: #374151;
        }
        textarea { box-sizing: border-box !important; min-width: 100% !important; max-width: 100% !important; color: #374151; }
      `}</style>
    </div>
  );
}