import React, { useState, useEffect } from 'react';
import { useParams, useNavigate } from 'react-router-dom';
import api from '../../services/api';

// Icons
const BackIcon = () => (
	<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
		<path d="M19 12H5M12 19l-7-7 7-7"/>
	</svg>
);

const PencilIcon = () => (
	<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" style={{verticalAlign: 'middle'}}>
		<path d="M12 20h9" />
		<path d="M16.5 3.5a2.121 2.121 0 0 1 3 3L7 19l-4 1 1-4L16.5 3.5z" />
	</svg>
);

const formatCurrency = (amount) => {
	if (amount === null || amount === undefined) return '0 ₫';
	const num = typeof amount === 'number' ? amount : parseFloat(amount) || 0;
	return new Intl.NumberFormat('vi-VN', { style: 'currency', currency: 'VND' }).format(num);
};

export default function OrderDetail() {
	const { orderId } = useParams();
	const navigate = useNavigate();

	const [loading, setLoading] = useState(true);
	const [error, setError] = useState(null);

	const [order, setOrder] = useState(null);
	const [customerInfo, setCustomerInfo] = useState(null);
	const [salespersonInfo, setSalespersonInfo] = useState(null);

	useEffect(() => {
		const fetchData = async () => {
			try {
				setLoading(true);
				const res = await api.get(`/orders/${orderId}`);
				if (!res) throw new Error('Không tìm thấy đơn hàng.');
				setOrder(res);

				if (res.customerId) {
					const c = await api.get(`/customers/${res.customerId}`).catch(() => null);
					setCustomerInfo(c);
				} else {
					setCustomerInfo(null);
				}

				// fetch salesperson info if order doesn't include a name
				if (res.salespersonId) {
					const sp = await api.get(`/users/${res.salespersonId}`).catch(() => null);
					setSalespersonInfo(sp);
				} else {
					setSalespersonInfo(null);
				}
			} catch (e) {
				console.error(e);
				setError(e.message || 'Lỗi khi lấy dữ liệu.');
			} finally {
				setLoading(false);
			}
		};
		fetchData();
	}, [orderId]);

	const parsePromotionAmount = (promo, unit, base) => {
		if (!promo && promo !== 0) return 0;
		try {
			let numPromo;
			if (unit === 'VND') {
				numPromo = parseFloat(String(promo).replace(/\./g, ''));
			} else {
				numPromo = parseFloat(String(promo));
			}
			if (isNaN(numPromo)) return 0;
			if (unit === '%' || unit === 'percentage') return base * (numPromo / 100);
			return numPromo || 0;
		} catch {
			return 0;
		}
	};

	if (loading) return <div style={{ padding: 24 }}>Đang tải dữ liệu...</div>;
	if (error) return <div style={{ padding: 24, color: 'red' }}>Lỗi: {error}</div>;
	if (!order) return <div style={{ padding: 24 }}>Không tìm thấy đơn hàng.</div>;

	// pick first item to show (follow OrderCreateFromQuote structure)
	const item = order.orderItems?.[0] || {};

	// support legacy promotion fields and new discount fields
	const promoValue = order.discountValue ?? order.promotion ?? null;
	const promoUnit = order.discountType === 'percentage' || order.promotionUnit === '%' ? '%' : (order.discountType === 'amount' ? 'VND' : (order.promotionUnit || ''));
	const promoNote = order.discountNote ?? order.promotionNotes ?? '';

	const baseTotal = order.totalPrice ?? ((item.unitPrice || 0) * (item.quantity || 1));
	const promoAmount = parsePromotionAmount(promoValue, promoUnit, baseTotal);
	const computedTotal = Math.max(0, Math.round(baseTotal - promoAmount));

	// delivery address fallback (cover multiple possible DB field names)
	const deliveryAddress = order.deliveryAddress ?? order.deliveryAddressLine1 ?? order.shippingAddress ?? order.customerAddress ?? order.delivery_address ?? '---';

	return (
		<div style={{ minHeight: '100vh', backgroundColor: '#F8FAFC', fontFamily: '-apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif' }}>
			<div style={{ maxWidth: '1200px', margin: '0 auto', padding: '24px' }}>
				{/* Header */}
				<div style={{ position: 'sticky', top: 0, zIndex: 10, backgroundColor: '#F8FAFC', paddingTop: '24px', paddingBottom: '16px', borderBottom: '1px solid #E2E8F0', display: 'flex', alignItems: 'center', justifyContent: 'space-between', marginBottom: '24px' }}>
					<div style={{ display: 'flex', alignItems: 'center', gap: '12px' }}>
						<button onClick={() => navigate('/sales')} style={{ display: 'flex', alignItems: 'center', gap: '8px', padding: '8px 16px', backgroundColor: 'white', border: '1px solid #D1D5DB', borderRadius: '8px', cursor: 'pointer', fontSize: '14px', fontWeight: '500', color: '#374151' }}>
							<BackIcon /> Quay lại
						</button>
						<h1 style={{ fontSize: '24px', fontWeight: '700', color: '#0F172A', margin: 0 }}>Chi tiết đơn hàng #{order.orderNumber || order.id}</h1>
					</div>
				</div>

				<div style={{ display: 'grid', gridTemplateColumns: '2fr 1fr', gap: '24px' }}>
					{/* Left Column */}
					<div style={{ minWidth: 0 }}>
						{/* Customer Information */}
						<div style={{ backgroundColor: 'white', borderRadius: '12px', padding: '24px', marginBottom: '24px', border: '1px solid #E2E8F0' }}>
							<h2 style={{ fontSize: '18px', fontWeight: '600', color: '#0F172A', marginBottom: '20px' }}>Thông tin khách hàng</h2>
							<div style={{ display: 'grid', gridTemplateColumns: 'repeat(2, 1fr)', gap: '16px' }}>
								<div><label>ID Khách hàng</label><input type="text" value={order.customerId ?? '---'} readOnly className="form-input-readonly" /></div>
								<div><label>Tên khách hàng</label><input type="text" value={customerInfo?.name || order.customerName || '---'} readOnly className="form-input-readonly" /></div>
								<div><label>Số điện thoại</label><input type="text" value={customerInfo?.phone || order.customerPhone || '---'} readOnly className="form-input-readonly" /></div>
								<div><label>Email</label><input type="text" value={customerInfo?.email || order.customerEmail || '---'} readOnly className="form-input-readonly" /></div>
								<div style={{gridColumn: 'span 2'}}><label>Địa chỉ</label><input type="text" value={customerInfo?.address || order.customerAddress || '---'} readOnly className="form-input-readonly" /></div>
							</div>
						</div>

						{/* Vehicle / Items (follow create layout: no color, no version) */}
						<div style={{ backgroundColor: 'white', borderRadius: '12px', padding: '24px', marginBottom: '24px', border: '1px solid #E2E8F0' }}>
							<h2 style={{ fontSize: '18px', fontWeight: '600', color: '#0F172A', marginBottom: '20px' }}>Chi tiết sản phẩm</h2>
							<div style={{ display: 'grid', gridTemplateColumns: 'repeat(2, 1fr)', gap: '16px' }}>
								<div><label>ID Xe</label><input type="text" value={item.vehicleId ?? '---'} readOnly className="form-input-readonly" /></div>
								<div><label>Số lượng</label><input type="text" value={item.quantity ?? '---'} readOnly className="form-input-readonly" /></div>
								<div><label>Đơn giá</label><input type="text" value={formatCurrency(item.unitPrice ?? 0)} readOnly className="form-input-readonly" /></div>
								<div style={{gridColumn: 'span 2'}}><label>Ghi chú</label><textarea value={item.notes || order.notes || 'Không có'} readOnly rows={2} className="form-input-readonly" style={{minHeight: '80px'}}/></div>
							</div>
						</div>

						{/* Payment & Promotion */}
						<div style={{ backgroundColor: 'white', borderRadius: '12px', padding: '24px', marginBottom: '24px', border: '1px solid #E2E8F0' }}>
							<h2 style={{ fontSize: '18px', fontWeight: '600', color: '#0F172A', marginBottom: '16px' }}>
								<span style={{ display: 'inline-flex', alignItems: 'center', gap: '8px' }}><PencilIcon /> Thông tin thanh toán</span>
							</h2>

							{/* Promotion (read-only) */}
							{promoValue ? (
								<div style={{ marginBottom: '20px', padding: '16px', backgroundColor: '#F8FAFC', borderRadius: '8px', border: '1px solid #E2E8F0' }}>
									<div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '12px', marginBottom: '12px' }}>
										<div>
											<label>Giá trị KM</label>
											<input type="text" value={promoUnit === '%' ? `${promoValue} %` : formatCurrency(promoValue)} readOnly className="form-input-readonly" />
										</div>
										<div>
											<label>Ghi chú KM</label>
											<input type="text" value={promoNote || '---'} readOnly className="form-input-readonly" />
										</div>
									</div>
								</div>
							) : null}

							<div style={{ marginBottom: '20px', display: 'grid', gridTemplateColumns: 'repeat(2, 1fr)', gap: '12px' }}>
								<div><label>Phương thức thanh toán</label><input value={order.paymentMethod || order.paymentType || '---'} readOnly className="form-input-readonly" /></div>
								<div><label>Loại thanh toán</label><input value={order.paymentType === 'Installment' ? 'Trả góp' : 'Trả thẳng'} readOnly className="form-input-readonly" /></div>
							</div>

							{order.paymentType === 'Installment' && (
								<div style={{ padding: '16px', backgroundColor: '#F8FAFC', borderRadius: '8px', border: '1px solid #E2E8F0', marginBottom: '20px' }}>
									<p style={{ margin: '0 0 16px 0', fontSize: '13px', fontWeight: '600', color: '#374151' }}>Điều kiện trả góp:</p>
									<div style={{ display: 'grid', gridTemplateColumns: 'repeat(2, 1fr)', gap: '12px' }}>
										<div style={{gridColumn: 'span 2'}}><label>Đặt cọc (VNĐ)</label><input value={order.downPaymentAmount ?? '---'} readOnly className="form-input-readonly" /></div>
										<div><label>Lãi suất (%/năm)</label><input value={order.interestRate ?? '---'} readOnly className="form-input-readonly" /></div>
										<div><label>Kỳ hạn (tháng)</label><input value={order.loanTerm ?? '---'} readOnly className="form-input-readonly" /></div>
									</div>
								</div>
							)}

							<div style={{ borderTop: '1px solid #E2E8F0', paddingTop: '20px', marginTop: '20px' }}>
								<div style={{ display: 'flex', justifyContent: 'flex-end', alignItems: 'center', gap: '16px' }}>
									<span style={{ fontSize: '16px', fontWeight: '600', color: '#0F172A' }}>Thành tiền</span>
									<span style={{ fontSize: '22px', fontWeight: '700', color: '#1D4ED8' }}>{formatCurrency(computedTotal)}</span>
								</div>
							</div>
						</div>
					</div>

					{/* Right Column */}
					<div style={{ minWidth: 0 }}>
						<div style={{ backgroundColor: 'white', borderRadius: '12px', padding: '24px', marginBottom: '24px', border: '1px solid #E2E8F0' }}>
							<h2 style={{ fontSize: '18px', fontWeight: '600', color: '#0F172A', marginBottom: '20px' }}>
								<span style={{ display: 'inline-flex', alignItems: 'center', gap: '8px' }}><PencilIcon /> Thông tin giao hàng & Ghi chú</span>
							</h2>
							<div style={{ marginBottom: '12px' }}><label>Ngày giao hàng mong muốn</label><input value={order.deliveryDate ? new Date(order.deliveryDate).toLocaleDateString('vi-VN') : '---'} readOnly className="form-input-readonly" /></div>
							<div style={{ marginBottom: '12px' }}><label>Ngày giao dự kiến</label><input value={order.estimatedDeliveryDate ? new Date(order.estimatedDeliveryDate).toLocaleDateString('vi-VN') : '---'} readOnly className="form-input-readonly" /></div>
							<div style={{ marginBottom: '12px' }}><label>Địa chỉ giao hàng</label><input value={deliveryAddress} readOnly className="form-input-readonly" /></div>
							<div><label>Ghi chú đơn hàng</label><textarea value={order.notes || '---'} readOnly rows={3} className="form-input-readonly" style={{minHeight: '80px'}}/></div>
						</div>

						<div style={{ backgroundColor: 'white', borderRadius: '12px', padding: '24px', border: '1px solid #E2E8F0', marginBottom: '16px' }}>
							<h2 style={{ fontSize: '18px', fontWeight: '600', color: '#0F172A', marginBottom: '20px' }}>Thông tin nhân viên</h2>
							<div style={{ marginBottom: '16px' }}><label>ID nhân viên</label><input value={order.salespersonId ?? '---'} readOnly className="form-input-readonly" /></div>
							<div style={{ marginBottom: '16px' }}><label>Tên nhân viên</label><input value={salespersonInfo?.fullName || order.salespersonName || '---'} readOnly className="form-input-readonly" /></div>
						</div>

						{/* Summary removed: Thành tiền shown on the left */}
					</div>
				</div>
			</div>

			<style>{`
				label { display: block; font-size: 14px; font-weight: 500; color: #374151; margin-bottom: 8px; }
				.form-input-readonly { width: 100%; padding: 10px 12px; border: 1px solid #CBD5E1; border-radius: 8px; font-size: 14px; height: 40px; box-sizing: border-box; color: #374151; background-color: #F1F5F9; }
				textarea.form-input-readonly { height: auto; resize: vertical; }
			`}</style>
		</div>
	);
}
