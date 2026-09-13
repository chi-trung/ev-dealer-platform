import { useState, useEffect } from 'react';
import { useNavigate } from 'react-router-dom';
import api from '../../services/api';
import { useAuth } from '../../context/AuthContext'; // Import useAuth

// Icons
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

export default function QuoteCreate() {
  const navigate = useNavigate();
  const { user } = useAuth(); // Get user from AuthContext
  const [loading, setLoading] = useState(false);
  const [vehicles, setVehicles] = useState([]); // State for real vehicle data
  const [fetchingVehicles, setFetchingVehicles] = useState(true);
  const [vehiclesError, setVehiclesError] = useState(null);

  const [customers, setCustomers] = useState([]); // State for real customer data
  const [fetchingCustomers, setFetchingCustomers] = useState(true);
  const [customersError, setCustomersError] = useState(null);
  const [selectedCustomerId, setSelectedCustomerId] = useState(''); // State for selected customer ID from dropdown
  const [customerSearchTerm, setCustomerSearchTerm] = useState(''); // New state for customer search

  // Customer info
  const [customerInfo, setCustomerInfo] = useState({
    id: '', // Store selected customer ID
    name: '',
    phone: '',
    email: '',
    address: '',
    idNumber: ''
  });
  
  // Quote item (singular, as per new requirement)
  const [quoteItem, setQuoteItem] = useState({
    vehicleId: '',
    vehicleName: '',
    quantity: 1,
    unitPrice: 0,
    discount: 0,
    stockQuantity: 0
  });
  
  // Payment info
  const [paymentInfo, setPaymentInfo] = useState({
    type: 'full', // 'full' or 'installment'
    downPaymentAmount: '', // Changed to empty string
    loanTerm: 12, // months
    interestRate: 8.5 // %
  });
  
  // Additional info
  const [additionalInfo, setAdditionalInfo] = useState({
    deliveryDate: '',
    notes: '',
    salesPersonId: '', // Will be set from user context
    salesPersonName: '', // Will be set from user context
    dealerId: '', // Added dealerId
    validUntil: ''
  });

  // Set salesperson info and dealerId from logged-in user
  useEffect(() => {
    if (user) {
      setAdditionalInfo(prev => ({
        ...prev,
        salesPersonId: user.id,
        salesPersonName: user.fullName || user.name,
        dealerId: user.dealerId // Assuming user object contains dealerId
      }));
    }
  }, [user]);


  // Fetch vehicles from VehicleService
  useEffect(() => {
    const fetchVehicles = async () => {
      try {
        setFetchingVehicles(true);
        const response = await api.get('/vehicles');
        setVehicles(response.items);
      } catch (err) {
        console.error('Error fetching vehicles:', err);
        setVehiclesError('Failed to load vehicle data.');
      } finally {
        setFetchingVehicles(false);
      }
    };
    fetchVehicles();
  }, []);

  // Fetch customers from CustomerService
  useEffect(() => {
    const fetchCustomers = async () => {
      try {
        setFetchingCustomers(true);
        const response = await api.get('/customers');
        setCustomers(response);
      } catch (err) {
        console.error('Error fetching customers:', err);
        setCustomersError('Failed to load customer data.');
      } finally {
        setFetchingCustomers(false);
      }
    };
    fetchCustomers();
  }, []);

  // Filter customers based on search term
  const filteredCustomers = customers.filter(customer => 
    customer.name.toLowerCase().includes(customerSearchTerm.toLowerCase()) ||
    customer.phone.includes(customerSearchTerm) ||
    customer.email.toLowerCase().includes(customerSearchTerm.toLowerCase())
  );

  // Handle customer selection from dropdown
  const handleCustomerSelectChange = (e) => {
    const customerId = e.target.value;
    setSelectedCustomerId(customerId);

    if (customerId) {
      const customer = customers.find(c => c.id === parseInt(customerId));
      if (customer) {
        setCustomerInfo({
          id: customer.id,
          name: customer.name,
          phone: customer.phone,
          email: customer.email,
          address: customer.address,
          idNumber: ''
        });
      }
    } else {
      setCustomerInfo({ id: '', name: '', phone: '', email: '', address: '', idNumber: '' });
    }
  };

  // Validation form
  const validateForm = () => {
    if (!customerInfo.id) {
      alert('Vui lòng chọn khách hàng.');
      return false;
    }
    
    if (!additionalInfo.salesPersonId) { 
      alert('Không tìm thấy ID nhân viên bán hàng. Vui lòng đăng nhập lại.');
      return false;
    }
    
    if (!additionalInfo.dealerId) { // Added validation for dealerId
      alert('Không tìm thấy ID đại lý. Vui lòng đăng nhập lại hoặc kiểm tra thông tin người dùng.');
      return false;
    }

    if (!quoteItem.vehicleId || quoteItem.quantity < 1) {
      alert('Vui lòng chọn xe và đảm bảo số lượng lớn hơn 0.');
      return false;
    }

    const selectedVehicle = vehicles.find(v => v.id === parseInt(quoteItem.vehicleId));
    if (selectedVehicle && quoteItem.quantity > selectedVehicle.stockQuantity) {
      alert(`Số lượng xe "${selectedVehicle.model}" vượt quá số lượng tồn kho (${selectedVehicle.stockQuantity}).`);
      return false;
    }

    const discountVal = parseFloat(quoteItem.discount) || 0;
    if (discountVal < 0) {
      alert('Giảm giá không được âm.');
      return false;
    }
    // Removed discount validation for > 15% as it's tied to PendingApproval status
    // if (discountVal > 15) {
    //   alert('Giảm giá tối đa là 15%. Vui lòng điều chỉnh phần giảm giá.');
    //   return false;
    // }

    if (paymentInfo.type === 'installment') {
      const total = calculateTotal();
      const downPayment = parseFloat(paymentInfo.downPaymentAmount) || 0;

      if (downPayment <= 0) {
          alert('Số tiền trả trước phải lớn hơn 0.');
          return false;
      }
      if (downPayment >= total) {
          alert('Số tiền trả trước phải nhỏ hơn tổng giá trị đơn hàng.');
          return false;
      }
      if (!paymentInfo.loanTerm || paymentInfo.loanTerm <= 0) {
        alert('Kỳ hạn vay không hợp lệ.');
        return false;
      }
      if (paymentInfo.interestRate === null || paymentInfo.interestRate < 0) {
        alert('Lãi suất không hợp lệ.');
        return false;
      }
    }

    return true;
  };

  const formatCurrency = (amount) => {
    return new Intl.NumberFormat('vi-VN', {
      style: 'currency',
      currency: 'VND',
    }).format(amount);
  };

  const calculateTotal = () => {
    const itemTotal = quoteItem.unitPrice * quoteItem.quantity;
    const percentDiscount = itemTotal * (quoteItem.discount / 100);
    let finalItemTotal = itemTotal - percentDiscount;
    return finalItemTotal < 0 ? 0 : finalItemTotal;
  };

  const calculateDownPayment = () => {
    return parseFloat(paymentInfo.downPaymentAmount) || 0;
  };

  const calculateLoanAmount = () => {
    const total = calculateTotal();
    const downPayment = calculateDownPayment();
    return total - downPayment;
  };

  const calculateMonthlyPayment = () => {
    if (paymentInfo.type === 'full') return 0;
    
    const loanAmount = calculateLoanAmount();
    const monthlyRate = paymentInfo.interestRate / 100 / 12;
    const numPayments = paymentInfo.loanTerm;
    
    if (monthlyRate === 0) return loanAmount / numPayments;
    if (numPayments === 0 || monthlyRate < 0 || loanAmount <= 0) return 0;
    
    return loanAmount * (monthlyRate * Math.pow(1 + monthlyRate, numPayments)) / 
           (Math.pow(1 + monthlyRate, numPayments) - 1);
  };

  const calculateInstallmentTotalPayment = () => {
    if (paymentInfo.type === 'full') {
      return calculateTotal();
    } else {
      const downPayment = calculateDownPayment();
      const monthlyPayment = calculateMonthlyPayment();
      const loanTerm = paymentInfo.loanTerm;
      return downPayment + (monthlyPayment * (loanTerm > 0 ? loanTerm : 0));
    }
  };

  const updateQuoteItem = (field, value) => {
    const newItem = { ...quoteItem, [field]: value };
    
    if (field === 'vehicleId') {
      const vehicle = vehicles.find(v => v.id === parseInt(value));
      if (vehicle) {
        newItem.vehicleName = vehicle.model;
        newItem.unitPrice = vehicle.price;
        newItem.stockQuantity = vehicle.stockQuantity;
        if (newItem.quantity > vehicle.stockQuantity || newItem.quantity === 0) {
          newItem.quantity = vehicle.stockQuantity > 0 ? 1 : 0;
        }
      } else {
        newItem.vehicleName = '';
        newItem.unitPrice = 0;
        newItem.stockQuantity = 0;
        newItem.quantity = 0;
      }
    }
    setQuoteItem(newItem);
  };

  const handleGenerateQuote = async () => {
    if (!validateForm()) return;
    setLoading(true);

    try {
      const total = calculateTotal();
      // const maxDiscount = parseFloat(quoteItem.discount) || 0; // No longer needed for status logic
      // const initialStatus = (maxDiscount > 5 && maxDiscount <= 15) ? 'PendingApproval' : 'Finalized'; // Removed approval logic

      const createQuotePayload = {
        customerId: customerInfo.id,
        dealerId: additionalInfo.dealerId, // Added dealerId to payload
        vehicleId: parseInt(quoteItem.vehicleId),
        colorId: quoteItem.colorVariantId, // Mapped colorVariantId to colorId
        quantity: quoteItem.quantity,
        unitPrice: quoteItem.unitPrice,
        totalPrice: total,
        notes: additionalInfo.notes, // UNCOMMENTED: Notes are now sent
        salespersonId: additionalInfo.salesPersonId,
        // salespersonName: additionalInfo.salesPersonName, // SalespersonName is not directly in Quote model
        status: 'Active', // Always set to Active as per new logic
        // paymentType: paymentInfo.type === 'full' ? 'Full' : 'Installment', // Payment info not directly in Quote model
        // downPaymentPercent: paymentInfo.type === 'installment' ? downPaymentPercent : null,
        // loanTerm: paymentInfo.type === 'installment' ? paymentInfo.loanTerm : null,
        // interestRate: paymentInfo.type === 'installment' ? paymentInfo.interestRate : null,
        // quoteItems: [{ // QuoteItems are not directly in Quote model
        //   vehicleId: parseInt(quoteItem.vehicleId),
        //   vehicleName: quoteItem.vehicleName,
        //   quantity: quoteItem.quantity,
        //   unitPrice: quoteItem.unitPrice,
        //   discountPercent: quoteItem.discount
        // }]
      };

      const quoteResponse = await api.post('/Quotes', createQuotePayload); // Changed API endpoint to /api/Quotes
      console.log('Quote created successfully with ID:', quoteResponse.id);

      alert('Báo giá đã được tạo thành công!');
      navigate('/sales/quotes');
    } catch (error) {
      console.error('Error creating quote:', error.response ? error.response.data : error.message);
      alert('Tạo báo giá thất bại. Vui lòng kiểm tra console để biết chi tiết.');
    } finally {
      setLoading(false);
    }
  };

  const handleDownloadQuote = async () => {
    if (!validateForm()) return;
    setLoading(true);

    try {
      const total = calculateTotal();
      const downPayment = calculateDownPayment();
      const downPaymentPercent = total > 0 ? (downPayment / total) * 100 : 0;

      const payload = {
        customerInfo: {
          id: customerInfo.id,
          name: customerInfo.name,
          phone: customerInfo.phone,
          email: customerInfo.email,
          address: customerInfo.address,
        },
        quoteItems: [{
          vehicleId: parseInt(quoteItem.vehicleId),
          vehicleName: quoteItem.vehicleName,
          quantity: quoteItem.quantity,
          unitPrice: quoteItem.unitPrice,
          discountPercent: quoteItem.discount,
          itemTotal: total
        }],
        paymentInfo: {
          type: paymentInfo.type,
          downPaymentPercent: downPaymentPercent,
          loanTerm: paymentInfo.loanTerm,
          interestRate: paymentInfo.interestRate,
        },
        additionalInfo: {
          deliveryDate: additionalInfo.deliveryDate,
          notes: additionalInfo.notes,
          salesPerson: additionalInfo.salesPersonName,
          validUntil: additionalInfo.validUntil,
        },
        totalCalculatedAmount: total,
        downPaymentCalculated: downPayment,
        loanAmountCalculated: calculateLoanAmount(), // Pass the correct loan amount
        monthlyPaymentCalculated: calculateMonthlyPayment(),
        installmentTotalPaymentCalculated: calculateInstallmentTotalPayment(),
      };

      // api's response interceptor unwraps to response.data, so this resolves to the blob itself
      const blob = await api.post(
        '/Sales/generate-quote-pdf', // Changed API endpoint to /api/Sales/generate-quote-pdf
        payload,
        // Server-side PDF rendering can take well over the shared 10s timeout
        // (this call previously ran on raw axios, which has no timeout at all).
        { responseType: 'blob', timeout: 120000 }
      );

      const file = new Blob([blob], { type: 'application/pdf' });
      const fileURL = URL.createObjectURL(file);
      const link = document.createElement('a');
      link.href = fileURL;
      link.setAttribute('download', `BaoGiaXeDien_${new Date().toISOString().slice(0,10)}.pdf`);
      document.body.appendChild(link);
      link.click();
      link.remove();
      URL.revokeObjectURL(fileURL);

      alert('Đã tạo và tải xuống báo giá PDF thành công!');
    } catch (error) {
      console.error('Error generating PDF:', error.response ? error.response.data : error.message);
      alert('Tạo báo giá PDF thất bại. Vui lòng kiểm tra console để biết chi tiết.');
    } finally {
      setLoading(false);
    }
  };

  if (fetchingVehicles || fetchingCustomers) {
    return (
      <div style={{ display: 'flex', justifyContent: 'center', alignItems: 'center', height: '100vh' }}>
        <div>Đang tải dữ liệu...</div>
      </div>
    );
  }

  if (vehiclesError || customersError) {
    return (
      <div style={{ display: 'flex', justifyContent: 'center', alignItems: 'center', height: '100vh', color: 'red' }}>
        <div>Lỗi tải dữ liệu: {vehiclesError || customersError}</div>
      </div>
    );
  }

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
                Tạo Báo Giá Mới
              </h1>
            </div>
          </div>
          
          <div style={{ display: 'flex', gap: '12px' }}>
            <button 
              onClick={handleDownloadQuote}
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
              onMouseOver={(e) => e.target.style.backgroundColor = '#F3F4F6'}
              onMouseOut={(e) => e.target.style.backgroundColor = 'white'}
            >
              <DownloadIcon />
              Lưu nháp
            </button>
            <button 
              onClick={handleGenerateQuote}
              disabled={loading}
              style={{
                display: 'flex',
                alignItems: 'center',
                gap: '8px',
                padding: '10px 20px',
                backgroundColor: loading ? '#9CA3AF' : '#3B82F6',
                color: 'white',
                border: 'none',
                borderRadius: '8px',
                cursor: loading ? 'not-allowed' : 'pointer',
                transition: 'all 0.2s',
                fontSize: '14px',
                fontWeight: '500'
              }}
            >
              {loading ? 'Đang xử lý...' : 'Tạo báo giá'}
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
                <div style={{ minWidth: 0, gridColumn: 'span 2' }}>
                  <label style={{ display: 'block', fontSize: '14px', fontWeight: '500', color: '#374151', marginBottom: '8px' }}>
                    Tìm kiếm khách hàng (Tên, SĐT, Email)
                  </label>
                  <input
                    type="text"
                    placeholder="Nhập tên, số điện thoại hoặc email..."
                    value={customerSearchTerm}
                    onChange={(e) => setCustomerSearchTerm(e.target.value)}
                    style={{
                      width: '100%',
                      padding: '10px 12px',
                      border: '1px solid #CBD5E1',
                      borderRadius: '8px',
                      fontSize: '14px',
                      backgroundColor: '#F8FAFC',
                      height: '40px',
                      boxSizing: 'border-box',
                      color: '#374151'
                    }}
                  />
                  <label style={{ display: 'block', fontSize: '14px', fontWeight: '500', color: '#374151', marginBottom: '8px' }}>
                    Chọn khách hàng <span style={{ color: 'red' }}>*</span>
                  </label>
                  <select
                    value={selectedCustomerId}
                    onChange={handleCustomerSelectChange}
                    style={{
                      width: '100%',
                      padding: '10px 12px',
                      border: '1px solid #CBD5E1',
                      borderRadius: '8px',
                      fontSize: '14px',
                      backgroundColor: '#F8FAFC',
                      height: '40px',
                      boxSizing: 'border-box',
                      color: '#374151'
                    }}
                  >
                    <option value="">-- Chọn khách hàng --</option>
                    {filteredCustomers.map(customer => (
                      <option key={customer.id} value={customer.id}>
                        {customer.name} ({customer.phone}) - {customer.email}
                      </option>
                    ))}
                  </select>
                </div>
                
                <div style={{ minWidth: 0 }}>
                  <label style={{ display: 'block', fontSize: '14px', fontWeight: '500', color: '#374151', marginBottom: '8px' }}>
                    Họ và tên
                  </label>
                  <input
                    type="text"
                    value={customerInfo.name}
                    readOnly
                    style={{
                      width: '100%',
                      padding: '10px 12px',
                      border: '1px solid #CBD5E1',
                      borderRadius: '8px',
                      fontSize: '14px',
                      backgroundColor: '#F1F5F9',
                      height: '40px',
                      boxSizing: 'border-box',
                      color: '#374151'
                    }}
                  />
                </div>
                
                <div style={{ minWidth: 0 }}>
                  <label style={{ display: 'block', fontSize: '14px', fontWeight: '500', color: '#374151', marginBottom: '8px' }}>
                    Số điện thoại
                  </label>
                  <input
                    type="tel"
                    value={customerInfo.phone}
                    readOnly
                    style={{
                      width: '100%',
                      padding: '10px 12px',
                      border: '1px solid #CBD5E1',
                      borderRadius: '8px',
                      fontSize: '14px',
                      backgroundColor: '#F1F5F9',
                      height: '40px',
                      boxSizing: 'border-box',
                      color: '#374151'
                    }}
                  />
                </div>
                
                <div style={{ minWidth: 0 }}>
                  <label style={{ display: 'block', fontSize: '14px', fontWeight: '500', color: '#374151', marginBottom: '8px' }}>
                    Email
                  </label>
                  <input
                    type="email"
                    value={customerInfo.email}
                    readOnly
                    style={{
                      width: '100%',
                      padding: '10px 12px',
                      border: '1px solid #CBD5E1',
                      borderRadius: '8px',
                      fontSize: '14px',
                      backgroundColor: '#F1F5F9',
                      height: '40px',
                      boxSizing: 'border-box',
                      color: '#374151'
                    }}
                  />
                </div>
                
                <div style={{ minWidth: 0 }}>
                  <label style={{ display: 'block', fontSize: '14px', fontWeight: '500', color: '#374151', marginBottom: '8px' }}>
                    Địa chỉ
                  </label>
                  <input
                    type="text"
                    value={customerInfo.address}
                    readOnly
                    style={{
                      width: '100%',
                      padding: '10px 12px',
                      border: '1px solid #CBD5E1',
                      borderRadius: '8px',
                      fontSize: '14px',
                      backgroundColor: '#F1F5F9',
                      height: '40px',
                      boxSizing: 'border-box',
                      color: '#374151'
                    }}
                  />
                </div>
              </div>
            </div>

            {/* Vehicle Selection */}
            <div style={{
              backgroundColor: 'white',
              borderRadius: '12px',
              padding: '24px',
              marginBottom: '24px',
              border: '1px solid #E2E8F0',
              boxShadow: '0 1px 3px 0 rgba(0, 0, 0, 0.05)'
            }}>
              <h2 style={{ fontSize: '18px', fontWeight: '600', color: '#0F172A', marginBottom: '20px' }}>
                Chi tiết xe <span style={{ color: 'red' }}>*</span>
              </h2>
              
              <div style={{
                padding: '20px',
                border: '1px solid #E2E8F0',
                borderRadius: '8px',
                backgroundColor: '#F8FAFC'
              }}>
                <div style={{
                  display: 'grid',
                  gridTemplateColumns: 'minmax(0, 2fr) minmax(0, 1fr) minmax(0, 1fr) minmax(0, 1fr)',
                  gap: '12px',
                  alignItems: 'end'
                }}>
                  <div style={{ minWidth: 0 }}>
                    <label style={{ display: 'block', fontSize: '14px', fontWeight: '500', color: '#374151', marginBottom: '8px' }}>
                      Chọn xe
                    </label>
                    <select
                      value={quoteItem.vehicleId}
                      onChange={(e) => updateQuoteItem('vehicleId', e.target.value)}
                      style={{
                        width: '100%',
                        padding: '10px 12px',
                        border: '1px solid #CBD5E1',
                        borderRadius: '8px',
                        fontSize: '14px',
                        backgroundColor: '#F8FAFC',
                        height: '40px',
                        boxSizing: 'border-box',
                        color: '#374151'
                      }}
                    >
                      <option value="">-- Chọn xe --</option>
                      {vehicles.map(vehicle => (
                        <option
                          key={vehicle.id}
                          value={vehicle.id}
                          disabled={vehicle.stockQuantity === 0}
                          style={{ color: vehicle.stockQuantity === 0 ? '#9CA3AF' : '#374151' }}
                        >
                          {vehicle.model} - {formatCurrency(vehicle.price)} ({vehicle.stockQuantity === 0 ? 'Hết hàng' : `Còn: ${vehicle.stockQuantity}`})
                        </option>
                      ))}
                    </select>
                  </div>

                  <div style={{ minWidth: 0 }}>
                    <label style={{ display: 'block', fontSize: '14px', fontWeight: '500', color: '#374151', marginBottom: '8px' }}>
                      Số lượng
                    </label>
                    <input
                      type="number"
                      min="1"
                      max={quoteItem.stockQuantity > 0 ? quoteItem.stockQuantity : 1}
                      value={quoteItem.quantity}
                      onChange={(e) => updateQuoteItem('quantity', parseInt(e.target.value) || 1)}
                      disabled={!quoteItem.vehicleId || quoteItem.stockQuantity === 0}
                      style={{
                        width: '100%',
                        padding: '10px 12px',
                        border: '1px solid #CBD5E1',
                        borderRadius: '8px',
                        fontSize: '14px',
                        backgroundColor: '#F8FAFC',
                        height: '40px',
                        boxSizing: 'border-box',
                        color: '#374151'
                      }}
                    />
                  </div>

                  <div style={{ minWidth: 0 }}>
                    <label style={{ display: 'block', fontSize: '14px', fontWeight: '500', color: '#374151', marginBottom: '8px' }}>
                      Đơn giá
                    </label>
                    <input
                      type="text"
                      value={formatCurrency(quoteItem.unitPrice)}
                      readOnly
                      style={{
                        width: '100%',
                        padding: '10px 12px',
                        border: '1px solid #CBD5E1',
                        borderRadius: '8px',
                        fontSize: '14px',
                        backgroundColor: '#F1F5F9',
                        height: '40px',
                        boxSizing: 'border-box',
                        color: '#374151'
                      }}
                    />
                  </div>
                  
                  <div style={{ minWidth: 0 }}>
                    <label style={{ display: 'block', fontSize: '14px', fontWeight: '500', color: '#374151', marginBottom: '8px' }}>
                      Giảm giá (%)
                    </label>
                    <input
                      type="number"
                      min="0"
                      max="100"
                      value={quoteItem.discount}
                      onChange={(e) => updateQuoteItem('discount', parseFloat(e.target.value) || 0)}
                      style={{
                        width: '100%',
                        padding: '10px 12px',
                        border: '1px solid #CBD5E1',
                        borderRadius: '8px',
                        fontSize: '14px',
                        backgroundColor: '#F8FAFC',
                        height: '40px',
                        boxSizing: 'border-box',
                        color: '#374151'
                      }}
                    />
                  </div>
                </div>
                
                {quoteItem.vehicleId && (
                  <div style={{ marginTop: '16px', paddingTop: '16px', borderTop: '1px solid #E2E8F0' }}>
                    <div style={{ display: 'flex', justifyContent: 'space-between', fontSize: '14px' }}>
                      <span style={{ color: '#64748B' }}>Thành tiền:</span>
                      <span style={{ fontWeight: '600', color: '#0F172A' }}>
                        {formatCurrency(calculateTotal())}
                      </span>
                    </div>
                  </div>
                )}
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
              
              <div style={{ marginBottom: '20px' }}>
                <label style={{ display: 'block', fontSize: '14px', fontWeight: '500', color: '#374151', marginBottom: '12px' }}>
                  Hình thức thanh toán
                </label>
                <div style={{ display: 'flex', gap: '20px' }}>
                  <label style={{ display: 'flex', alignItems: 'center', gap: '8px', cursor: 'pointer' }}>
                    <input
                      type="radio"
                      name="paymentType"
                      value="full"
                      checked={paymentInfo.type === 'full'}
                      onChange={(e) => setPaymentInfo({...paymentInfo, type: e.target.value})}
                    />
                    <span style={{ fontSize: '14px', color: '#374151' }}>Trả toàn bộ</span>
                  </label>
                  <label style={{ display: 'flex', alignItems: 'center', gap: '8px', cursor: 'pointer' }}>
                    <input
                      type="radio"
                      name="paymentType"
                      value="installment"
                      checked={paymentInfo.type === 'installment'}
                      onChange={(e) => setPaymentInfo({...paymentInfo, type: e.target.value})}
                    />
                    <span style={{ fontSize: '14px', color: '#374151' }}>Trả góp</span>
                  </label>
                </div>
              </div>
              
              {paymentInfo.type === 'installment' && (
                <div style={{ display: 'grid', gridTemplateColumns: 'repeat(3, 1fr)', gap: '16px' }}>
                  <div style={{ minWidth: 0 }}>
                    <label style={{ display: 'block', fontSize: '14px', fontWeight: '500', color: '#374151', marginBottom: '8px' }}>
                      Trả trước (VND) <span style={{ color: 'red' }}>*</span>
                    </label>
                    <input
                      type="text"
                      value={paymentInfo.downPaymentAmount}
                      onChange={(e) => {
                        const value = e.target.value;
                        // Allow only numbers and remove leading zeros
                        const sanitizedValue = value.replace(/[^0-9]/g, '').replace(/^0+/, '');
                        setPaymentInfo({...paymentInfo, downPaymentAmount: sanitizedValue });
                      }}
                      style={{
                        width: '100%',
                        padding: '10px 12px',
                        border: '1px solid #CBD5E1',
                        borderRadius: '8px',
                        fontSize: '14px',
                        backgroundColor: '#F8FAFC',
                        height: '40px',
                        boxSizing: 'border-box',
                        color: '#374151'
                      }}
                    />
                  </div>
                  
                  <div style={{ minWidth: 0 }}>
                    <label style={{ display: 'block', fontSize: '14px', fontWeight: '500', color: '#374151', marginBottom: '8px' }}>
                      Kỳ hạn (tháng)
                    </label>
                    <select
                      value={paymentInfo.loanTerm}
                      onChange={(e) => setPaymentInfo({...paymentInfo, loanTerm: parseInt(e.target.value)})}
                      style={{
                        width: '100%',
                        padding: '10px 12px',
                        border: '1px solid #CBD5E1',
                        borderRadius: '8px',
                        fontSize: '14px',
                        backgroundColor: '#F8FAFC',
                        height: '40px',
                        boxSizing: 'border-box',
                        color: '#374151'
                      }}
                    >
                      <option value={6}>6 tháng</option>
                      <option value={12}>12 tháng</option>
                      <option value={24}>24 tháng</option>
                      <option value={36}>36 tháng</option>
                      <option value={48}>48 tháng</option>
                      <option value={60}>60 tháng</option>
                    </select>
                  </div>
                  
                  <div style={{ minWidth: 0 }}>
                    <label style={{ display: 'block', fontSize: '14px', fontWeight: '500', color: '#374151', marginBottom: '8px' }}>
                      Lãi suất (%/năm)
                    </label>
                    <input
                      type="number"
                      step="0.1"
                      value={paymentInfo.interestRate}
                      onChange={(e) => setPaymentInfo({...paymentInfo, interestRate: parseFloat(e.target.value) || 0})}
                      style={{
                        width: '100%',
                        padding: '10px 12px',
                        border: '1px solid #CBD5E1',
                        borderRadius: '8px',
                        fontSize: '14px',
                        backgroundColor: '#F8FAFC',
                        height: '40px',
                        boxSizing: 'border-box',
                        color: '#374151'
                      }}
                    />
                  </div>
                </div>
              )}
            </div>
          </div>

          {/* Sidebar */}
          <div style={{ minWidth: 0 }}>
            {/* Sales Person Information */}
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
                <label style={{ display: 'block', fontSize: '14px', fontWeight: '500', color: '#374151', marginBottom: '8px' }}>
                  ID nhân viên
                </label>
                <input
                  type="text"
                  value={additionalInfo.salesPersonId}
                  readOnly
                  style={{
                    width: '100%',
                    padding: '10px 12px',
                    border: '1px solid #CBD5E1',
                    borderRadius: '8px',
                    fontSize: '14px',
                    backgroundColor: '#F1F5F9',
                    height: '40px',
                    boxSizing: 'border-box',
                    color: '#374151'
                  }}
                />
              </div>
              
              <div style={{ marginBottom: '16px' }}>
                <label style={{ display: 'block', fontSize: '14px', fontWeight: '500', color: '#374151', marginBottom: '8px' }}>
                  Tên nhân viên
                </label>
                <input
                  type="text"
                  value={additionalInfo.salesPersonName}
                  readOnly
                  style={{
                    width: '100%',
                    padding: '10px 12px',
                    border: '1px solid #CBD5E1',
                    borderRadius: '8px',
                    fontSize: '14px',
                    backgroundColor: '#F1F5F9',
                    height: '40px',
                    boxSizing: 'border-box',
                    color: '#374151'
                  }}
                />
              </div>

              <div style={{ marginBottom: '16px' }}>
                <label style={{ display: 'block', fontSize: '14px', fontWeight: '500', color: '#374151', marginBottom: '8px' }}>
                  ID Đại lý
                </label>
                <input
                  type="text"
                  value={additionalInfo.dealerId}
                  readOnly
                  style={{
                    width: '100%',
                    padding: '10px 12px',
                    border: '1px solid #CBD5E1',
                    borderRadius: '8px',
                    fontSize: '14px',
                    backgroundColor: '#F1F5F9',
                    height: '40px',
                    boxSizing: 'border-box',
                    color: '#374151'
                  }}
                />
              </div>
              
              <div>
                <label style={{ display: 'block', fontSize: '14px', fontWeight: '500', color: '#374151', marginBottom: '8px' }}>
                  Ghi chú
                </label>
                <textarea
                  value={additionalInfo.notes}
                  onChange={(e) => setAdditionalInfo({...additionalInfo, notes: e.target.value})}
                  rows={3}
                  style={{
                    width: '100%',
                    padding: '10px 12px',
                    border: '1px solid #CBD5E1',
                    borderRadius: '8px',
                    fontSize: '14px',
                    backgroundColor: '#F8FAFC',
                    resize: 'vertical',
                    boxSizing: 'border-box',
                    minHeight: '80px',
                    color: '#374151'
                  }}
                  placeholder="Thêm ghi chú cho báo giá..."
                />
              </div>
            </div>

            {/* Price Calculation */}
            <div style={{
              backgroundColor: 'white',
              borderRadius: '12px',
              padding: '24px',
              border: '1px solid #E2E8F0',
              boxShadow: '0 1px 3px 0 rgba(0, 0, 0, 0.05)'
            }}>
              <h2 style={{ fontSize: '18px', fontWeight: '600', color: '#0F172A', marginBottom: '20px' }}>
                Chi tiết giá
              </h2>
              
              <div style={{ marginBottom: '16px' }}>
                <div style={{ display: 'flex', justifyContent: 'space-between', fontSize: '14px', marginBottom: '12px' }}>
                  <span style={{ color: '#64748B' }}>Tổng cộng:</span>
                  <span style={{ color: '#0F172A', fontWeight: '500' }}>{formatCurrency(calculateTotal())}</span>
                </div>
                
                {paymentInfo.type === 'installment' && (
                  <>
                    <div style={{ display: 'flex', justifyContent: 'space-between', fontSize: '14px', marginBottom: '12px' }}>
                      <span style={{ color: '#64748B' }}>Trả trước:</span>
                      <span style={{ color: '#0F172A', fontWeight: '500' }}>{formatCurrency(calculateDownPayment())}</span>
                    </div>
                    <div style={{ display: 'flex', justifyContent: 'space-between', fontSize: '14px', marginBottom: '12px' }}>
                      <span style={{ color: '#64748B' }}>Số tiền vay:</span>
                      <span style={{ color: '#0F172A', fontWeight: '500' }}>{formatCurrency(calculateLoanAmount())}</span>
                    </div>
                    <div style={{ display: 'flex', justifyContent: 'space-between', fontSize: '14px', marginBottom: '12px' }}>
                      <span style={{ color: '#64748B' }}>Góp hàng tháng:</span>
                      <span style={{ color: '#0F172A', fontWeight: '500' }}>{formatCurrency(calculateMonthlyPayment())}</span>
                    </div>
                  </>
                )}
              </div>
              
              <div style={{ 
                paddingTop: '16px', 
                borderTop: '1px solid #E2E8F0',
                display: 'flex',
                justifyContent: 'space-between',
                alignItems: 'center'
              }}>
                <span style={{ fontSize: '16px', fontWeight: '600', color: '#0F172A' }}>Tổng thanh toán:</span>
                <span style={{ fontSize: '20px', fontWeight: '700', color: '#3B82F6' }}>
                  {formatCurrency(calculateInstallmentTotalPayment())}
                </span>
              </div>
            </div>
          </div>
        </div>
      </div>

      {/* Thêm CSS để đảm bảo tính ổn định */}
      <style>{`
        /* Cố định hoàn toàn kích thước select và input */
        select, input[type="text"], input[type="tel"], input[type="email"], input[type="number"], input[type="date"] {
          height: 40px !important;
          box-sizing: border-box !important;
          min-width: 100% !important;
          max-width: 100% !important;
          color: #374151; /* Ensure text color is visible */
        }
        
        select {
          overflow: hidden !important;
          text-overflow: ellipsis !important;
          white-space: nowrap !important;
        }
        
        /* Đảm bảo textarea có kích thước phù hợp */
        textarea {
          box-sizing: border-box !important;
          min-width: 100% !important;
          max-width: 100% !important;
          color: #374151; /* Ensure text color is visible */
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
        
        /* Đảm bảo radio buttons có style nhất quán */
        input[type="radio"] {
          margin: 0;
        }
      `}</style>
    </div>
  );
}