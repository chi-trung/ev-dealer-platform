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

const PencilIcon = () => (
  <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" style={{verticalAlign: 'middle'}}>
    <path d="M12 20h9" />
    <path d="M16.5 3.5a2.121 2.121 0 0 1 3 3L7 19l-4 1 1-4L16.5 3.5z" />
  </svg>
);

const TrashIcon = () => (
    <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
        <polyline points="3 6 5 6 21 6"></polyline>
        <path d="M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6m3 0V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2"></path>
        <line x1="10" y1="11" x2="10" y2="17"></line>
        <line x1="14" y1="11" x2="14" y2="17"></line>
    </svg>
);

// New Icon for Promotion
const GiftIcon = () => (
  <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
    <polyline points="20 12 20 22 4 22 4 12"></polyline>
    <rect x="2" y="7" width="20" height="5"></rect>
    <line x1="12" y1="22" x2="12" y2="7"></line>
    <path d="M12 7H7.5a2.5 2.5 0 0 1 0-5C11 2 12 7 12 7z"></path>
    <path d="M12 7h4.5a2.5 2.5 0 0 0 0-5C13 2 12 7 12 7z"></path>
  </svg>
);

export default function OrderCreateFromQuote() {
  const { quoteId } = useParams();
  const navigate = useNavigate();
  const { user } = useAuth(); // Logged-in user info
  const [quote, setQuote] = useState(null);
  const [customerInfo, setCustomerInfo] = useState(null);
  const [vehicleInfo, setVehicleInfo] = useState(null); // State for vehicle details
  // Salesperson info will now come from the logged-in user, not the quote
  const [salespersonId, setSalespersonId] = useState('');
  const [salespersonName, setSalespersonName] = useState('');
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(null);

  // Order fields
  const [paymentMethod, setPaymentMethod] = useState('');
  const [deliveryDate, setDeliveryDate] = useState('');
  const [estimatedDeliveryDate, setEstimatedDeliveryDate] = useState('');
  const [deliveryAddress, setDeliveryAddress] = useState('');
  const [notes, setNotes] = useState(''); // This is for Order Notes, will be empty by default
  const [paymentType, setPaymentType] = useState('');
  
  // Editable installment fields - initialized from quote
  const [downPaymentAmount, setDownPaymentAmount] = useState('');
  const [interestRate, setInterestRate] = useState(0);
  const [loanTerm, setLoanTerm] = useState('12');

  // Promotion states
  const [showPromotionPopup, setShowPromotionPopup] = useState(false);
  const [promotions, setPromotions] = useState([]);
  const [selectedPromotion, setSelectedPromotion] = useState(null); // Stores the selected promotion object
  const [promotionLoading, setPromotionLoading] = useState(false);
  const [promotionError, setPromotionError] = useState(null);

  const formatVndInput = (value) => {
    if (!value) return '';
    const numberValue = String(value).replace(/\D/g, '');
    return numberValue.replace(/\B(?=(\d{3})+(?!\d))/g, '.');
  };

  const parseVndInput = (value) => {
    if (typeof value !== 'string' || !value) return 0;
    return parseFloat(value.replace(/\./g, ''));
  };

  const formatCurrency = (amount) => {
    return new Intl.NumberFormat('vi-VN', { style: 'currency', currency: 'VND' }).format(amount);
  };
  
  // Function to fetch promotions
  const fetchPromotions = async () => {
    setPromotionLoading(true);
    setPromotionError(null);
    try {
      const response = await api.get('/Promotions');
      const now = new Date();
      // FIX: Handle cases where API returns an object with array in $values
      // (api's response interceptor already unwraps one .data level)
      const promotionData = Array.isArray(response) ? response : response?.$values || [];
      
      // Filter promotions that are currently active
      const activePromotions = promotionData.filter(promo => {
        const startDate = new Date(promo.startDate);
        const endDate = new Date(promo.endDate);
        return now >= startDate && now <= endDate;
      });
      setPromotions(activePromotions);
    } catch (err) {
      console.error('Error fetching promotions:', err);
      setPromotionError('Không thể tải danh sách khuyến mãi.');
    } finally {
      setPromotionLoading(false);
    }
  };

  useEffect(() => {
    const fetchQuoteAndDependencies = async () => {
      try {
        setLoading(true);
        setError(null);

        // 1. Fetch Quote Details
        const quoteResponse = await api.get(`/Quotes/${quoteId}`);
        const fetchedQuote = quoteResponse;
        setQuote(fetchedQuote);

        // 2. Fetch Customer, Vehicle Details in parallel
        // api resolves to the payload directly, so the .catch fallbacks return the payload shape too (no { data: ... } wrapper)
        const [customerRes, vehicleRes] = await Promise.all([
          api.get(`/customers/${fetchedQuote.customerId}`).catch(err => { console.error("Error fetching customer:", err); return { name: 'N/A', phone: 'N/A', email: 'N/A', address: 'N/A' }; }),
          api.get(`/vehicles/${fetchedQuote.vehicleId}`).catch(err => { console.error("Error fetching vehicle:", err); return { model: 'N/A' }; }),
        ]);

        setCustomerInfo(customerRes);
        setVehicleInfo(vehicleRes);
        
        // Set salesperson info from logged-in user (as per user's explicit request)
        if (user) {
          setSalespersonId(user.id);
          setSalespersonName(user.fullName || user.name || 'N/A');
        } else {
          setSalespersonId('N/A');
          setSalespersonName('N/A');
        }

        // 3. Pre-fill form fields from fetched quote
        setPaymentType(fetchedQuote.paymentType || 'Full'); // Assuming paymentType is in Quote model
        setDownPaymentAmount(formatVndInput(String(fetchedQuote.depositAmount || ''))); // Assuming depositAmount is in Quote model
        setInterestRate(fetchedQuote.interestRateYearly || 0); // Assuming interestRateYearly is in Quote model
        setLoanTerm(String(fetchedQuote.loanTermMonths || '12')); // Assuming loanTermMonths is in Quote model
        setNotes(''); // Order notes should be empty by default for new input

      } catch (err) {
        console.error('Error fetching data for order creation:', err);
        setError('Không thể tải chi tiết báo giá để tạo đơn hàng.');
      } finally {
        setLoading(false);
      }
    };

    fetchQuoteAndDependencies();
    fetchPromotions(); // Fetch promotions when component mounts
  }, [quoteId, user]); // Add user to dependency array to react to user changes

  const handleDownPaymentChange = (e) => {
    setDownPaymentAmount(formatVndInput(e.target.value));
  };

  const handleSelectPromotion = (promotion) => {
    setSelectedPromotion(promotion);
    console.log("Selected Promotion object:", promotion); // Log the entire promotion object
  };

  const handleApplyPromotion = () => {
    setShowPromotionPopup(false);
  };

  const handleRemovePromotion = () => {
    setSelectedPromotion(null);
  };

  const baseTotal = quote?.totalBasePrice ?? ((quote?.basePrice || 0) * (quote?.quantity || 1)); // Use totalBasePrice
  let promoAmount = 0;
  if (selectedPromotion) {
    console.log("Selected Promotion:", selectedPromotion);
    console.log("baseTotal:", baseTotal);
    
    const promoValue = parseFloat(selectedPromotion.discountValue); 
    const discountType = selectedPromotion.discountType;

    console.log("Debug: selectedPromotion.discountType =", discountType);
    console.log("Debug: promoValue =", promoValue);

    if (isNaN(promoValue)) {
      console.error("promoValue is NaN. Check selectedPromotion object for correct value property:", selectedPromotion);
    }
    if (isNaN(baseTotal)) {
      console.error("baseTotal is NaN. Check quote data:", quote);
    }

    if (discountType === 'Percent') { 
      promoAmount = baseTotal * (promoValue / 100);
      console.log("Debug: Calculated Percent promoAmount =", promoAmount);
    } else if (discountType === 'Amount') { 
      promoAmount = promoValue;
      console.log("Debug: Calculated Amount promoAmount =", promoAmount);
    } else {
      console.log("Debug: Unrecognized discountType =", discountType);
    }
    console.log("Final promoAmount before computedTotal:", promoAmount);
  }
  const computedTotal = Math.max(0, Math.round(baseTotal - promoAmount));
  console.log("computedTotal:", computedTotal);


  const handleCreateOrder = async () => {
    // --- Validation ---
    if (!paymentMethod || !deliveryDate || !estimatedDeliveryDate) {
      alert('Vui lòng điền đầy đủ các trường bắt buộc (*).');
      return;
    }
    if (paymentType === 'Installment' && parseVndInput(downPaymentAmount) <= 0) {
        alert('Vui lòng nhập số tiền đặt cọc hợp lệ.');
        return;
    }
    if (computedTotal <= 0) {
        alert('Tổng số tiền phải lớn hơn 0 sau khi áp dụng khuyến mãi.');
        return;
    }

    setLoading(true);

    // --- Payload Preparation ---
    const getNumericValue = (value, isCurrency = false, allowZero = false) => {
        if (value === null || value === undefined || value === '') return null;
        const parsed = isCurrency ? parseVndInput(value) : parseFloat(value);
        if (isNaN(parsed)) return null;
        if (!allowZero && parsed === 0) return null;
        return parsed;
    };

    const discountAmountValue = selectedPromotion?.discountType === 'Amount' ? parseFloat(selectedPromotion.discountValue) : null;
    const discountPercentValue = selectedPromotion?.discountType === 'Percent' ? parseFloat(selectedPromotion.discountValue) : null;

    const orderPayload = {
      quoteId: parseInt(quoteId),
      customerId: quote.customerId,
      customerEmail: customerInfo?.email,
      customerName: customerInfo?.name,
      dealerId: quote.dealerId,
      salespersonId: salespersonId,
      paymentMethod: paymentMethod,
      deliveryDate: deliveryDate,
      estimatedDeliveryDate: estimatedDeliveryDate,
      notes: notes,
      paymentType: paymentType,
      depositAmount: paymentType === 'Installment' ? getNumericValue(downPaymentAmount, true) : null,
      interestRateYearly: paymentType === 'Installment' ? getNumericValue(interestRate, false, true) : null,
      loanTermMonths: paymentType === 'Installment' ? parseInt(loanTerm) : null,
      vehicleId: quote.vehicleId,
      vehicleVariantId: quote.vehicleVariantId,
      colorId: quote.colorId,
      quantity: quote.quantity,
      unitPrice: quote.basePrice,
      totalAmount: computedTotal,
      promotionId: selectedPromotion?.promotionId || null,
      discountAmount: !isNaN(discountAmountValue) ? discountAmountValue : null,
      discountPercent: !isNaN(discountPercentValue) ? discountPercentValue : null,
      discountNote: selectedPromotion?.description || null,
    };

    // --- API Call ---
    // The backend endpoint /api/Orders/complete is responsible for BOTH creating the order
    // AND updating the quote status. We only need to make this one call.
    try {
      console.log("Sending order payload to backend:", orderPayload);
      await api.post('/Orders/complete', orderPayload);
      
      // If the call succeeds, we assume the backend has done its job.
      alert("Đơn hàng đã được tạo thành công!");
      navigate('/sales/quotes'); // Navigate to quote list to see the result.

    } catch (err) {
      // If this fails, it's a backend issue.
      let errorMessage = 'Tạo đơn hàng thất bại. Vui lòng thử lại.';
      if (err.response) {
        const serverError = err.response.data;
        if (serverError?.message) {
          errorMessage = serverError.message;
        } else if (serverError?.title) {
          errorMessage = serverError.title;
        } else if (serverError?.errors) {
          const validationErrors = Object.entries(serverError.errors)
            .map(([key, value]) => `${key}: ${value.join(', ')}`)
            .join('\n');
          errorMessage = `Lỗi xác thực:\n${validationErrors}`;
        } else if (typeof serverError === 'string') {
          errorMessage = serverError;
        } else {
          errorMessage = JSON.stringify(serverError);
        }
      } else if (err.message) {
        errorMessage = err.message;
      }
      
      console.error('Error creating order:', err, 'Payload:', orderPayload);
      alert(`Tạo đơn hàng thất bại. Lỗi: ${errorMessage}`);
    } finally {
      setLoading(false);
    }
  };

  // Moved baseTotal and computedTotal calculation before handleCreateOrder
  // to ensure computedTotal is available for validation and payload.

  if (loading) return <div style={{ display: 'flex', justifyContent: 'center', alignItems: 'center', height: '100vh' }}>Đang tải dữ liệu...</div>;
  if (error) return <div style={{color: 'red', padding: '24px'}}>Lỗi: {error}</div>;
  if (!quote) return <div style={{ padding: '24px' }}>Không tìm thấy báo giá.</div>;

  return (
    <div style={{ minHeight: '100vh', backgroundColor: '#F8FAFC', fontFamily: '-apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif' }}>
      <div style={{ maxWidth: '1200px', margin: '0 auto', padding: '24px' }}>
        {/* Header */}
        <div style={{ position: 'sticky', top: 0, zIndex: 10, backgroundColor: '#F8FAFC', paddingTop: '24px', paddingBottom: '16px', borderBottom: '1px solid #E2E8F0', display: 'flex', alignItems: 'center', justifyContent: 'space-between', marginBottom: '24px' }}>
          <div style={{ display: 'flex', alignItems: 'center', gap: '12px' }}>
            <button onClick={() => navigate('/sales/quotes')} style={{ display: 'flex', alignItems: 'center', gap: '8px', padding: '8px 16px', backgroundColor: 'white', border: '1px solid #D1D5DB', borderRadius: '8px', cursor: 'pointer', fontSize: '14px', fontWeight: '500', color: '#374151' }}>
              <BackIcon /> Quay lại
            </button>
            <h1 style={{ fontSize: '24px', fontWeight: '700', color: '#0F172A', margin: 0 }}>
              Tạo Đơn Hàng từ Báo Giá #{quoteId}
            </h1>
          </div>
          <button onClick={handleCreateOrder} disabled={loading} style={{ display: 'flex', alignItems: 'center', gap: '8px', padding: '10px 20px', backgroundColor: loading ? '#9CA3AF' : '#3B82F6', color: 'white', border: 'none', borderRadius: '8px', cursor: loading ? 'not-allowed' : 'pointer', fontSize: '14px', fontWeight: '500' }}>
            <SaveIcon /> {loading ? 'Đang tạo...' : 'Tạo Đơn Hàng'}
          </button>
        </div>

        <div style={{ display: 'grid', gridTemplateColumns: '2fr 1fr', gap: '24px' }}>
          {/* Left Column */}
          <div style={{ minWidth: 0 }}>
            {/* Customer Information */}
            <div style={{ backgroundColor: 'white', borderRadius: '12px', padding: '24px', marginBottom: '24px', border: '1px solid #E2E8F0', boxShadow: '0 1px 3px 0 rgba(0, 0, 0, 0.05)' }}>
              <h2 style={{ fontSize: '18px', fontWeight: '600', color: '#0F172A', marginBottom: '20px' }}>Thông tin khách hàng</h2>
              <div style={{ display: 'grid', gridTemplateColumns: 'repeat(2, 1fr)', gap: '16px' }}>
                <div><label>ID Khách hàng</label><input type="text" value={quote.customerId} readOnly className="form-input-readonly" /></div>
                <div><label>Tên khách hàng</label><input type="text" value={customerInfo?.name || 'N/A'} readOnly className="form-input-readonly" /></div>
                <div><label>Số điện thoại</label><input type="text" value={customerInfo?.phone || 'N/A'} readOnly className="form-input-readonly" /></div>
                <div><label>Email</label><input type="text" value={customerInfo?.email || 'N/A'} readOnly className="form-input-readonly" /></div>
                <div style={{gridColumn: 'span 2'}}><label>Địa chỉ</label><input type="text" value={customerInfo?.address || 'N/A'} readOnly className="form-input-readonly" /></div>
              </div>
            </div>

            {/* Vehicle Information */}
            <div style={{ backgroundColor: 'white', borderRadius: '12px', padding: '24px', marginBottom: '24px', border: '1px solid #E2E8F0', boxShadow: '0 1px 3px 0 rgba(0, 0, 0, 0.05)' }}>
              <h2 style={{ fontSize: '18px', fontWeight: '600', color: '#0F172A', marginBottom: '20px' }}>Chi tiết xe</h2>
              <div style={{ display: 'grid', gridTemplateColumns: 'repeat(2, 1fr)', gap: '16px' }}>
                <div><label>ID Xe</label><input type="text" value={quote.vehicleId} readOnly className="form-input-readonly" /></div>
                <div><label>Tên xe</label><input type="text" value={vehicleInfo?.model || 'N/A'} readOnly className="form-input-readonly" /></div>
                <div><label>Số lượng</label><input type="text" value={quote.quantity} readOnly className="form-input-readonly" /></div>
                <div><label>Đơn giá</label><input type="text" value={formatCurrency(quote.basePrice)} readOnly className="form-input-readonly" /></div>
                <div style={{gridColumn: 'span 2'}}><label>Ghi chú báo giá</label><textarea value={quote.notes || 'Không có'} readOnly rows={2} className="form-input-readonly" style={{minHeight: '80px'}}/></div>
              </div>
            </div>

            {/* Order Details - Left Column Part */}
            <div style={{ backgroundColor: 'white', borderRadius: '12px', padding: '24px', marginBottom: '24px', border: '1px solid #E2E8F0', boxShadow: '0 1px 3px 0 rgba(0, 0, 0, 0.05)' }}>
              <h2 style={{ fontSize: '18px', fontWeight: '600', color: '#0F172A', marginBottom: '20px' }}>
                <span style={{ display: 'inline-flex', alignItems: 'center', gap: '8px' }}><PencilIcon /> Chi tiết thanh toán</span>
              </h2>

              {/* Payment Type - Changed label */}
              <div style={{ marginBottom: '20px' }}>
                <label>Hình thức thanh toán</label>
                <select value={paymentType} onChange={(e) => setPaymentType(e.target.value)} className="form-input">
                  <option value="Full">Trả thẳng</option>
                  <option value="Installment">Trả góp</option>
                </select>
              </div>

              {/* Payment Method - Moved from right column */}
              <div style={{ marginBottom: '20px' }}><label>Phương thức thanh toán <span style={{color: 'red'}}>*</span></label><select value={paymentMethod} onChange={(e) => setPaymentMethod(e.target.value)} className="form-input"><option value="">-- Chọn --</option><option value="Cash">Tiền mặt</option><option value="Bank transfer">Chuyển khoản</option></select></div>


              {/* Promotion Section */}
              <div style={{ marginBottom: '20px', padding: '16px', backgroundColor: '#F8FAFC', borderRadius: '8px', border: '1px solid #E2E8F0', position: 'relative' }}>
                <h3 style={{ fontSize: '16px', fontWeight: '600', color: '#0F172A', marginTop: '0', marginBottom: '12px' }}>Khuyến mãi</h3>
                {selectedPromotion ? (
                  <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', padding: '8px 0' }}>
                    <div>
                      <p style={{ margin: '0', fontWeight: '500', color: '#3B82F6' }}>{selectedPromotion.name}</p>
                      <p style={{ margin: '4px 0 0 0', fontSize: '13px', color: '#64748B' }}>
                        Giảm giá: {selectedPromotion.discountType === 'Percent' ? `${parseFloat(selectedPromotion.discountValue)}%` : formatCurrency(parseFloat(selectedPromotion.discountValue))}
                      </p>
                    </div>
                    <button onClick={handleRemovePromotion} style={{ background: 'none', border: 'none', cursor: 'pointer', color: '#EF4444', padding: '4px' }} title="Xóa khuyến mãi">
                      <TrashIcon />
                    </button>
                  </div>
                ) : (
                  <button onClick={() => setShowPromotionPopup(true)} style={{ display: 'inline-flex', alignItems: 'center', gap: '8px', padding: '8px 16px', backgroundColor: '#E0E7FF', color: '#3B82F6', border: '1px solid #C7D2FE', borderRadius: '6px', cursor: 'pointer', fontSize: '13px', fontWeight: '500' }}>
                    <GiftIcon /> Chọn khuyến mãi
                  </button>
                )}
              </div>

              {/* Installment Conditions */}
              {paymentType === 'Installment' && (
                <div style={{ padding: '16px', backgroundColor: '#F8FAFC', borderRadius: '8px', border: '1px solid #E2E8F0', marginBottom: '20px' }}>
                  <p style={{ margin: '0 0 16px 0', fontSize: '13px', fontWeight: '600', color: '#374151' }}>Điều kiện trả góp:</p>
                  <div style={{ display: 'grid', gridTemplateColumns: 'repeat(2, 1fr)', gap: '12px' }}>
                    <div style={{gridColumn: 'span 2'}}><label>Đặt cọc (VNĐ) <span style={{color: 'red'}}>*</span></label><input type="text" value={downPaymentAmount} onChange={handleDownPaymentChange} className="form-input" placeholder="Nhập số tiền..."/></div>
                    <div><label>Lãi suất (%/năm)</label><input type="number" value={interestRate} onChange={e => setInterestRate(e.target.value)} className="form-input" /></div>
                    <div>
                        <label>Kỳ hạn (tháng)</label>
                        <select value={loanTerm} onChange={e => setLoanTerm(e.target.value)} className="form-input">
                            <option value="3">3 tháng</option>
                            <option value="6">6 tháng</option>
                            <option value="12">12 tháng</option>
                            <option value="24">24 tháng</option>
                            <option value="36">36 tháng</option>
                        </select>
                    </div>
                  </div>
                </div>
              )}
              
              {/* Total Amount */}
              <div style={{ borderTop: '1px solid #E2E8F0', paddingTop: '20px', marginTop: '20px' }}>
                <div style={{ display: 'flex', justifyContent: 'flex-end', alignItems: 'center', gap: '16px' }}>
                  <span style={{ fontSize: '16px', fontWeight: '600', color: '#0F172A' }}>
                    Thành tiền:
                    {paymentType === 'Installment' && <span style={{ fontSize: '12px', fontWeight: '500', color: '#64748B', display: 'block' }}> (CHƯA BAO GỒM LÃI SUẤT)</span>}
                  </span>
                  <span style={{ fontSize: '22px', fontWeight: '700', color: '#1D4ED8' }}>{formatCurrency(computedTotal)}</span>
                </div>
                {paymentType === 'Installment' && parseVndInput(downPaymentAmount) > 0 && (
                    <div style={{ textAlign: 'right', marginTop: '8px', fontSize: '14px', color: '#475569' }}>
                        Đặt cọc: <span style={{ fontWeight: '600' }}>{formatCurrency(parseVndInput(downPaymentAmount))}</span>
                    </div>
                )}
              </div>
            </div>
          </div>

          {/* Right Column */}
          <div style={{ minWidth: 0 }}>
            {/* Order Delivery & Notes */}
            <div style={{ backgroundColor: 'white', borderRadius: '12px', padding: '24px', marginBottom: '24px', border: '1px solid #E2E8F0', boxShadow: '0 1px 3px 0 rgba(0, 0, 0, 0.05)' }}>
               <h2 style={{ fontSize: '18px', fontWeight: '600', color: '#0F172A', marginBottom: '20px' }}>
                <span style={{ display: 'inline-flex', alignItems: 'center', gap: '8px' }}><PencilIcon /> Thông tin giao hàng & Ghi chú</span>
              </h2>
              {/* Payment Method was here, now moved to left column */}
              <div style={{ marginBottom: '20px' }}><label>Ngày giao hàng mong muốn <span style={{color: 'red'}}>*</span></label><input type="date" value={deliveryDate} onChange={(e) => setDeliveryDate(e.target.value)} className="form-input" /></div>
              <div style={{ marginBottom: '20px' }}><label>Ngày giao hàng dự kiến <span style={{color: 'red'}}>*</span></label><input type="date" value={estimatedDeliveryDate} onChange={(e) => setEstimatedDeliveryDate(e.target.value)} className="form-input" /></div>
              <div style={{ marginBottom: '20px' }}><label>Địa chỉ giao hàng</label><input type="text" value={deliveryAddress} onChange={(e) => setDeliveryAddress(e.target.value)} placeholder="Nhập địa chỉ..." className="form-input" /></div>
              <div><label>Ghi chú đơn hàng</label><textarea value={notes} onChange={(e) => setNotes(e.target.value)} placeholder="Nhập ghi chú..." rows={3} className="form-input" style={{minHeight: '80px'}}/></div>
            </div>

            {/* Salesperson Information */}
            <div style={{ backgroundColor: 'white', borderRadius: '12px', padding: '24px', border: '1px solid #E2E8F0', boxShadow: '0 1px 3px 0 rgba(0, 0, 0, 0.05)' }}>
              <h2 style={{ fontSize: '18px', fontWeight: '600', color: '#0F172A', marginBottom: '20px' }}>Thông tin nhân viên</h2>
              <div style={{ marginBottom: '16px' }}><label>ID nhân viên</label><input type="text" value={salespersonId || 'N/A'} readOnly className="form-input-readonly" /></div>
              <div style={{ marginBottom: '16px' }}><label>Tên nhân viên</label><input type="text" value={salespersonName || 'N/A'} readOnly className="form-input-readonly" /></div>
              {/* Removed Email field as requested */}
            </div>
          </div>
        </div>
      </div>

      {/* Promotion Selection Popup */}
      {showPromotionPopup && (
        <div style={{
          position: 'fixed', top: 0, left: 0, right: 0, bottom: 0,
          backgroundColor: 'rgba(0, 0, 0, 0.5)', display: 'flex',
          justifyContent: 'center', alignItems: 'center', zIndex: 100
        }}>
          <div style={{
            backgroundColor: 'white', borderRadius: '12px', padding: '24px',
            width: '90%', maxWidth: '600px', maxHeight: '80vh', overflowY: 'auto',
            boxShadow: '0 10px 25px rgba(0, 0, 0, 0.1)'
          }}>
            <h2 style={{ fontSize: '20px', fontWeight: '700', color: '#0F172A', marginBottom: '20px' }}>Chọn Khuyến Mãi</h2>
            {promotionLoading && <p>Đang tải khuyến mãi...</p>}
            {promotionError && <p style={{ color: 'red' }}>Lỗi: {promotionError}</p>}
            {!promotionLoading && promotions.length === 0 && <p>Không có khuyến mãi khả dụng.</p>}
            
            <div style={{ maxHeight: '400px', overflowY: 'auto', marginBottom: '20px' }}>
              {promotions.map((promo) => {
                // Use discountValue and discountType from the promo object
                const promoValue = parseFloat(promo.discountValue); 
                const equivalentAmount = promo.discountType === 'Percent' ? (baseTotal * (promoValue / 100)) : promoValue;
                return (
                <div key={promo.promotionId} style={{ // Added key prop here
                  display: 'flex', alignItems: 'center', padding: '12px',
                  border: selectedPromotion?.promotionId === promo.promotionId ? '2px solid #3B82F6' : '1px solid #E2E8F0', // Thicker blue border for selected
                  borderRadius: '8px', marginBottom: '12px',
                  backgroundColor: selectedPromotion?.promotionId === promo.promotionId ? '#E0E7FF' : 'white', // Slightly more prominent background
                  cursor: 'pointer'
                }} onClick={() => handleSelectPromotion(promo)}>
                  <input
                    type="radio"
                    name="promotion"
                    value={promo.promotionId}
                    checked={selectedPromotion?.promotionId === promo.promotionId}
                    onChange={() => handleSelectPromotion(promo)} // This ensures clicking the radio button also selects
                    style={{ marginRight: '12px', transform: 'scale(1.2)', accentColor: '#3B82F6', cursor: 'pointer' }} // Use accentColor for modern radio button styling
                  />
                  <div>
                    <p style={{ margin: '0', fontWeight: '600', color: '#0F172A' }}>{promo.name}</p>
                    <p style={{ margin: '4px 0 0 0', fontSize: '14px', color: '#475569' }}>
                      Giảm giá: {promo.discountType === 'Percent' ? `${promoValue}% (tương đương ${formatCurrency(equivalentAmount)})` : formatCurrency(promoValue)}
                    </p>
                    <p style={{ margin: '4px 0 0 0', fontSize: '12px', color: '#64748B' }}>
                      Áp dụng từ {new Date(promo.startDate).toLocaleDateString()} đến {new Date(promo.endDate).toLocaleDateString()}
                    </p>
                    {promo.description && <p style={{ margin: '4px 0 0 0', fontSize: '12px', color: '#64748B', fontStyle: 'italic' }}>{promo.description}</p>}
                  </div>
                </div>
              );})}
            </div>

            <div style={{ display: 'flex', justifyContent: 'flex-end', gap: '12px' }}>
              <button onClick={() => setShowPromotionPopup(false)} style={{ padding: '10px 20px', backgroundColor: '#E2E8F0', color: '#374151', border: 'none', borderRadius: '8px', cursor: 'pointer', fontSize: '14px', fontWeight: '500' }}>
                Hủy
              </button>
              <button onClick={handleApplyPromotion} disabled={!selectedPromotion} style={{ padding: '10px 20px', backgroundColor: selectedPromotion ? '#3B82F6' : '#9CA3AF', color: 'white', border: 'none', borderRadius: '8px', cursor: selectedPromotion ? 'pointer' : 'not-allowed', fontSize: '14px', fontWeight: '500' }}>
                Áp dụng
              </button>
            </div>
          </div>
        </div>
      )}

      <style>{`
        label { display: block; font-size: 14px; font-weight: 500; color: #374151; margin-bottom: 8px; }
        .form-input, .form-input-readonly {
          width: 100%;
          padding: 10px 12px;
          border: 1px solid #CBD5E1;
          border-radius: 8px;
          font-size: 14px;
          height: 40px;
          box-sizing: border-box;
          color: #374151;
        }
        .form-input { background-color: #FFFFFF; }
        .form-input-readonly { background-color: #F1F5F9; }
        textarea.form-input, textarea.form-input-readonly { height: auto; resize: vertical; }
      `}</style>
    </div>
  );
}