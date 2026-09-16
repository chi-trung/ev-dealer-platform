import React, { useState, useEffect, useMemo } from "react";
import {
  Box,
  Grid,
  Paper,
  TextField,
  Button,
  Typography,
  Divider,
  Stack,
  Container,
  Card,
  CardContent,
  useTheme,
  Fade,
  Skeleton,
  Alert,
  CircularProgress,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableRow,
  TableContainer,
  Chip,
  LinearProgress,
} from "@mui/material";
import {
  Assessment as ChartIcon,
  FileDownload as DownloadIcon,
  TrendingUp as TrendingUpIcon,
  AttachMoney as MoneyIcon,
  Store as StoreIcon,
  Percent as PercentIcon,
  Refresh as RefreshIcon,
} from "@mui/icons-material";
import {
  BarChart,
  Bar,
  XAxis,
  YAxis,
  CartesianGrid,
  Tooltip,
  Legend,
  ResponsiveContainer,
  PieChart,
  Pie,
  Cell,
  LineChart,
  Line,
} from "recharts";
import { reportService } from "../../services/reportService"; // Giữ nguyên import
import authService from "../../services/authService";
import DemandForecastChart from "../../components/DemandForecastChart";

const COLORS = ["#3B82F6", "#10B981", "#F59E0B", "#06B6D4", "#8B5CF6"];

// Simple metric card - matching Sales page style
const MetricCard = ({ title, value, subtitle, icon, color }) => {
  const colorMap = {
    primary: "#3B82F6",
    success: "#10B981",
    warning: "#F59E0B",
    info: "#06B6D4",
  };

  const iconColor = colorMap[color] || colorMap.primary;

  return (
    <Card
      sx={{
        height: "100%",
        backgroundColor: "white",
        border: "1px solid #E2E8F0",
        borderRadius: "12px",
        boxShadow: "0 1px 3px 0 rgba(0, 0, 0, 0.05)",
        transition: "all 0.2s ease",
        "&:hover": {
          boxShadow: "0 4px 6px 0 rgba(0, 0, 0, 0.1)",
        },
      }}
    >
      <CardContent sx={{ p: 3 }}>
        <Box
          sx={{
            display: "flex",
            alignItems: "center",
            justifyContent: "space-between",
            mb: 2,
          }}
        >
          <Box
            sx={{
              p: 1.5,
              borderRadius: "8px",
              backgroundColor: `${iconColor}15`,
              color: iconColor,
              display: "flex",
              alignItems: "center",
              justifyContent: "center",
            }}
          >
            {icon}
          </Box>
        </Box>
        <Typography
          variant="h4"
          sx={{
            fontWeight: 700,
            mb: 0.5,
            color: "#0F172A",
            fontSize: { xs: "1.75rem", md: "2rem" },
          }}
        >
          {value}
        </Typography>
        <Typography
          variant="h6"
          sx={{
            fontWeight: 600,
            mb: 0.5,
            color: "#0F172A",
            fontSize: "1rem",
          }}
        >
          {title}
        </Typography>
        <Typography
          variant="body2"
          sx={{
            color: "#64748B",
            fontSize: "0.875rem",
          }}
        >
          {subtitle}
        </Typography>
      </CardContent>
    </Card>
  );
};

const Reports = () => {
  const [reportType, setReportType] = useState("sales");
  const [fromDate, setFromDate] = useState("");
  const [toDate, setToDate] = useState("");
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(null);
  const [regionalData, setRegionalData] = useState([]);
  const [proportionData, setProportionData] = useState([]);
  const [summary, setSummary] = useState(null);
  // eslint-disable-next-line no-unused-vars
  const [topVehicles, setTopVehicles] = useState([]);
  const [exporting, setExporting] = useState(false);
  const [staffPerformance, setStaffPerformance] = useState([]);
  const [customerDebt, setCustomerDebt] = useState([]);
  const [manufacturerDebt, setManufacturerDebt] = useState([]);
  const [dealerSales, setDealerSales] = useState([]);
  const [inventoryTrends, setInventoryTrends] = useState([]);

  // derived values for donut chart (sales share)
  const pieData = useMemo(
    () => proportionData.map((d) => ({ name: d.region, value: d.sales })),
    [proportionData]
  );

  const fetchReportData = async () => {
    try {
      setLoading(true);
      setError(null);

      const params = {
        from: fromDate || undefined,
        to: toDate || undefined,
        type: reportType,
      };

      const [
        summaryRes,
        regionsRes,
        proportionRes,
        topRes,
        staffRes,
        debtRes,
        dealerSalesRes,
        inventoryTrendRes,
      ] = await Promise.all([
        reportService.getSummary(params).catch((e) => {
          console.error("Error fetching summary:", e);
          return null;
        }),
        reportService.getSalesByRegion(params).catch((e) => {
          console.error("Error fetching sales by region:", e);
          return [];
        }),
        reportService.getSalesProportion(params).catch((e) => {
          console.error("Error fetching sales proportion:", e);
          return [];
        }),
        reportService.getTopVehicles({ ...params, limit: 5 }).catch((e) => {
          console.error("Error fetching top vehicles:", e);
          return [];
        }),
        reportService.getSalesByStaff(params).catch((e) => {
          console.error("Error fetching staff performance:", e);
          return [];
        }),
        reportService.getDebtReport(params).catch((e) => {
          console.error("Error fetching debt report:", e);
          return { customers: [], manufacturers: [] };
        }),
        reportService.getSalesByDealer(params).catch((e) => {
          console.error("Error fetching sales by dealer:", e);
          return [];
        }),
        reportService.getInventoryTrends(params).catch((e) => {
          console.error("Error fetching inventory trends:", e);
          return [];
        }),
      ]);

      if (summaryRes) setSummary(summaryRes);
      if (regionsRes && Array.isArray(regionsRes)) {
        const normalizedRegions = regionsRes.map((item) => ({
          ...item,
          revenueBn: Number(item.revenue || 0) / 1_000_000_000,
        }));
        setRegionalData(normalizedRegions);
      }
      if (proportionRes && Array.isArray(proportionRes)) {
        setProportionData(proportionRes);
      }
      if (topRes && Array.isArray(topRes)) {
        setTopVehicles(topRes);
      }
      if (staffRes && Array.isArray(staffRes)) {
        const mappedStaff = staffRes.map((staff) => {
          const totalQuotes = staff.totalQuotes ?? 0;
          const totalOrders = staff.totalOrders ?? 0;
          const totalContracts = staff.totalContracts ?? 0;
          const totalDeals =
            staff.totalDeals ??
            totalQuotes + totalOrders + totalContracts;

          return {
            id: staff.salespersonId || staff.id,
            name:
              staff.salespersonName ||
              staff.name ||
              `Nhân viên ${staff.salespersonId || "N/A"}`,
            totalQuotes,
            totalOrders,
            totalContracts,
            totalDeals,
            totalSales: staff.totalVehiclesSold || staff.totalSales || 0,
            revenue: staff.totalRevenue || staff.revenue || 0,
            conversionRate: staff.conversionRate ?? 0,
          };
        });
        setStaffPerformance(mappedStaff);
      }
      if (debtRes) {
        // Map từ backend format DealerDebtReportDto
        // sang frontend format {customers: [], manufacturers: []}
        if (debtRes.data) {
          // Format từ ReportService: {success: true, data: DealerDebtReportDto}
          const debtData = debtRes.data;
          
          // Map customer debt từ DebtFromCustomerDto sang format frontend
          const customerDebtList = (debtData.debtFromCustomerDetails || []).map((item) => ({
            id: item.orderId || item.customerId,
            name: item.customerName || `Khách hàng ${item.customerId}`,
            outstanding: item.remainingDebt || item.totalAmount - (item.paidAmount || 0),
            dueInDays: item.loanTermMonths ? Math.max(0, item.loanTermMonths * 30 - Math.floor((Date.now() - new Date(item.orderDate).getTime()) / (1000 * 60 * 60 * 24))) : null,
            lastPaymentDate: item.orderDate || null,
            status: item.status,
          }));
          
          // Map manufacturer debt từ DebtToManufacturerDto sang format frontend
          const manufacturerDebtList = (debtData.debtToManufacturerDetails || []).map((item) => ({
            id: item.orderId,
            name: item.orderNumber || `Đơn hàng ${item.orderId}`,
            outstanding: item.remainingDebt || item.orderAmount - (item.paidAmount || 0),
            dueInDays: item.orderDate ? Math.max(0, 30 - Math.floor((Date.now() - new Date(item.orderDate).getTime()) / (1000 * 60 * 60 * 24))) : null,
            lastPaymentDate: item.orderDate || null,
            status: item.status,
          }));
          
          setCustomerDebt(customerDebtList);
          setManufacturerDebt(manufacturerDebtList);
        } else {
          // Format trực tiếp (fallback)
          setCustomerDebt(debtRes.customers || []);
          setManufacturerDebt(debtRes.manufacturers || []);
        }
      }
      if (dealerSalesRes) {
        // Map từ backend format {success: true, data: DealerSalesReportDto}
        // hoặc array trực tiếp
        if (dealerSalesRes.data) {
          const salesData = dealerSalesRes.data;
          // Nếu là DealerSalesReportDto, chuyển đổi sang format array
          if (salesData.dealerName) {
            // Single dealer report - tạo một entry
            setDealerSales([{
              dealerId: salesData.dealerId,
              dealerName: salesData.dealerName,
              region: "N/A", // Backend chưa có region trong DTO này
              totalSales: salesData.totalVehiclesSold || 0,
              revenue: salesData.totalRevenue || 0,
              target: 0, // Backend chưa có target
            }]);
          } else {
            // Array format
            setDealerSales(Array.isArray(salesData) ? salesData : []);
          }
        } else {
          setDealerSales(Array.isArray(dealerSalesRes) ? dealerSalesRes : []);
        }
      }
      if (inventoryTrendRes) {
        // Map từ backend format {success: true, data: InventoryAnalysisDto}
        if (inventoryTrendRes.data) {
          const inventoryData = inventoryTrendRes.data;
          // Backend trả về InventoryAnalysisDto với inventoryTurnover và slowMovingInventory
          // Frontend expect format cho LineChart: [{month, inventory, sold}]
          // Hiện tại backend chưa có format này, nên ta sẽ map từ inventoryTurnover
          const turnoverData = inventoryData.inventoryTurnover || [];
          // Tạo dữ liệu giả lập cho chart (backend cần cải thiện để trả về format này)
          const chartData = turnoverData.length > 0 
            ? turnoverData.map((item, index) => ({
                month: `Tháng ${index + 1}`,
                inventory: item.currentStock || 0,
                sold: item.averageMonthlySales || 0,
              }))
            : [];
          setInventoryTrends(chartData);
        } else {
          setInventoryTrends(Array.isArray(inventoryTrendRes) ? inventoryTrendRes : []);
        }
      }
    } catch (err) {
      console.error("Error fetching report data:", err);
      setError("Không thể tải dữ liệu báo cáo. Vui lòng thử lại sau.");
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    fetchReportData();
    // eslint-disable-next-line
  }, []); // Load data on mount

  const handleGenerateReport = async () => {
    await fetchReportData();
  };

  const handleExport = async (typeOverride) => {
    const exportType = typeOverride || reportType || "sales";
    const payload = {
      type: exportType,
      format: "csv",
    };
    if (fromDate) payload.from = fromDate;
    if (toDate) payload.to = toDate;

    setExporting(true);
    try {
      const blob = await reportService.exportReport(payload);
      // create a download link
      const url = window.URL.createObjectURL(blob);
      const a = document.createElement("a");
      a.href = url;
      const timestamp = new Date().toISOString().slice(0, 19).replace(/[:T]/g, "");
      a.download = `report_${exportType}_${timestamp}.csv`;
      document.body.appendChild(a);
      a.click();
      a.remove();
      window.URL.revokeObjectURL(url);
    } catch (err) {
      console.error("Export failed:", err);
    } finally {
      setExporting(false);
    }
  };

  const theme = useTheme();
  const userRole = (authService.getCurrentUser()?.role || "").toLowerCase();
  const canViewManufacturerInsights = ["evmstaff", "admin"].includes(userRole);
  const formatCurrency = (value) =>
    `${Number(value || 0).toLocaleString("vi-VN")} VNĐ`;
  const formatPercent = (value) =>
    `${Number(value || 0).toFixed(1)}%`;
  const formatDate = (value) =>
    value ? new Date(value).toLocaleDateString("vi-VN") : "—";
  const getDueStatus = (days) => {
    if (days == null) return { label: "N/A", color: "default" };
    if (days <= 7) return { label: "Khẩn cấp", color: "error" };
    if (days <= 21) return { label: "Sắp đến hạn", color: "warning" };
    return { label: "Ổn định", color: "success" };
  };

  return (
    <Box
      sx={{
        minHeight: "100vh",
        backgroundColor: "#F8FAFC",
        fontFamily: '-apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif',
        py: 4,
      }}
    >
      <Container maxWidth="xl">
        {/* Page Header - Centered */}
        <Box sx={{ mb: 4, textAlign: "center" }}>
          <Typography
            variant="h3"
            sx={{
              fontWeight: 700,
              mb: 1,
              color: "#0F172A",
              fontSize: { xs: "1.5rem", md: "2rem" },
            }}
          >
            Báo cáo & Phân tích
          </Typography>
          <Typography
            variant="body1"
            sx={{ color: "#64748B", fontSize: "1rem" }}
          >
            Tạo và xuất báo cáo chi tiết về doanh số, tồn kho và hiệu suất
          </Typography>
        </Box>

        {/* Filters - Simple Design - Centered */}
        <Box sx={{ display: "flex", justifyContent: "center", mb: 4 }}>
          <Paper
            sx={{
              p: 3,
              borderRadius: "12px",
              backgroundColor: "white",
              border: "1px solid #E2E8F0",
              boxShadow: "0 1px 3px 0 rgba(0, 0, 0, 0.05)",
              width: "100%",
              maxWidth: "1200px",
            }}
          >
            <Grid
              container
              spacing={2}
              alignItems="center"
              justifyContent="center"
            >
              <Grid item xs={6} md={3} lg={2}>
                <TextField
                  type="date"
                  fullWidth
                  label="Từ ngày"
                  value={fromDate}
                  onChange={(e) => setFromDate(e.target.value)}
                  InputLabelProps={{
                    shrink: true,
                  }}
                  sx={{
                    "& .MuiOutlinedInput-root": {
                      borderRadius: "8px",
                      backgroundColor: "white",
                      "&:hover": {
                        backgroundColor: "#F8FAFC",
                      },
                      "&.Mui-focused": {
                        backgroundColor: "white",
                      },
                    },
                    "& .MuiInputLabel-root": {
                      fontWeight: 500,
                      color: "#64748B",
                    },
                  }}
                />
              </Grid>

              <Grid item xs={6} md={3} lg={2}>
                <TextField
                  type="date"
                  fullWidth
                  label="Đến ngày"
                  value={toDate}
                  onChange={(e) => setToDate(e.target.value)}
                  InputLabelProps={{
                    shrink: true,
                  }}
                  sx={{
                    "& .MuiOutlinedInput-root": {
                      borderRadius: "8px",
                      backgroundColor: "white",
                      "&:hover": {
                        backgroundColor: "#F8FAFC",
                      },
                      "&.Mui-focused": {
                        backgroundColor: "white",
                      },
                    },
                    "& .MuiInputLabel-root": {
                      fontWeight: 500,
                      color: "#64748B",
                    },
                  }}
                />
              </Grid>

              <Grid item xs={12} md={2} lg={3}>
                <Stack direction="row" spacing={2} justifyContent="flex-end">
                  <Button
                    variant="contained"
                    onClick={handleGenerateReport}
                    disabled={loading}
                    startIcon={loading ? <CircularProgress size={20} /> : <RefreshIcon />}
                    sx={{
                      px: 3,
                      py: 1.5,
                      backgroundColor: "#3B82F6",
                      color: "white",
                      fontWeight: 600,
                      textTransform: "none",
                      borderRadius: "8px",
                      "&:hover": {
                        backgroundColor: "#2563EB",
                      },
                      "&:disabled": {
                        backgroundColor: "#D1D5DB",
                        color: "#9CA3AF",
                      },
                      transition: "all 0.2s ease",
                    }}
                  >
                    {loading ? "Đang tải..." : "Tạo báo cáo"}
                  </Button>
                  <Button
                    variant="outlined"
                    startIcon={<DownloadIcon />}
                    disabled={exporting}
                    onClick={() => handleExport()}
                    sx={{
                      px: 3,
                      py: 1.5,
                      borderColor: "#D1D5DB",
                      color: exporting ? "#9CA3AF" : "#374151",
                      backgroundColor: "white",
                      fontWeight: 600,
                      textTransform: "none",
                      borderRadius: "8px",
                      "&:hover": {
                        borderColor: "#9CA3AF",
                        backgroundColor: "#F3F4F6",
                      },
                      "&:disabled": {
                        borderColor: "#E5E7EB",
                        backgroundColor: "#F9FAFB",
                      },
                      transition: "all 0.2s ease",
                    }}
                  >
                    {exporting ? "Đang xuất..." : "Xuất CSV"}
                  </Button>
                </Stack>
              </Grid>
            </Grid>
          </Paper>
        </Box>

        {/* Error Message */}
        {error && (
          <Fade in={true}>
            <Box sx={{ mb: 3 }}>
              <Alert
                severity="error"
                sx={{
                  borderRadius: "8px",
                  border: "1px solid #FEE2E2",
                }}
              >
                {error}
              </Alert>
            </Box>
          </Fade>
        )}

        {/* Summary Cards - Centered */}
        <Box sx={{ display: "flex", justifyContent: "center", mb: 4 }}>
          <Grid container spacing={3} sx={{ maxWidth: "1200px" }}>
            <Grid item xs={12} sm={6} md={3}>
              {loading ? (
                <Skeleton variant="rectangular" height={160} sx={{ borderRadius: 3 }} />
              ) : (
                <MetricCard
                  title="Tổng doanh số"
                  value={
                    summary?.totalSales
                      ? summary.totalSales.toLocaleString("vi-VN")
                      : "0"
                  }
                  subtitle="Số lượng xe"
                  icon={<TrendingUpIcon sx={{ fontSize: 28 }} />}
                  color="primary"
                />
              )}
            </Grid>
            <Grid item xs={12} sm={6} md={3}>
              {loading ? (
                <Skeleton variant="rectangular" height={160} sx={{ borderRadius: 3 }} />
              ) : (
                <MetricCard
                  title="Doanh thu"
                  value={
                    summary?.totalRevenue
                      ? `${(summary.totalRevenue / 1000000000).toFixed(1)}B VNĐ`
                      : "0B VNĐ"
                  }
                  subtitle="Tổng doanh thu"
                  icon={<MoneyIcon sx={{ fontSize: 28 }} />}
                  color="success"
                />
              )}
            </Grid>
            <Grid item xs={12} sm={6} md={3}>
              {loading ? (
                <Skeleton variant="rectangular" height={160} sx={{ borderRadius: 3 }} />
              ) : (
                <MetricCard
                  title="Đại lý hoạt động"
                  value={
                    summary
                      ? `${summary.activeDealers || 0}/${summary.totalDealers || 0}`
                      : "0/0"
                  }
                  subtitle="Số lượng đại lý"
                  icon={<StoreIcon sx={{ fontSize: 28 }} />}
                  color="warning"
                />
              )}
            </Grid>
            <Grid item xs={12} sm={6} md={3}>
              {loading ? (
                <Skeleton variant="rectangular" height={160} sx={{ borderRadius: 3 }} />
              ) : (
                <MetricCard
                  title="Tỷ lệ chuyển đổi"
                  value={
                    summary?.conversionRate
                      ? `${(summary.conversionRate * 100).toFixed(1)}%`
                      : "0%"
                  }
                  subtitle="Trung bình"
                  icon={<PercentIcon sx={{ fontSize: 28 }} />}
                  color="info"
                />
              )}
            </Grid>
          </Grid>
        </Box>

        {/* Charts - Equal Size and Centered */}
        <Box sx={{ display: "flex", justifyContent: "center", mb: 4 }}>
          <Grid container spacing={3} sx={{ maxWidth: "1200px" }}>
            {/* Bar Chart */}
            <Grid item xs={12} lg={6}>
              <Fade in={!loading} timeout={1000}>
                <Paper
                  sx={{
                    p: 4,
                    borderRadius: "12px",
                    backgroundColor: "white",
                    border: "1px solid #E2E8F0",
                    boxShadow: "0 1px 3px 0 rgba(0, 0, 0, 0.05)",
                    height: "100%",
                    display: "flex",
                    flexDirection: "column",
                    minHeight: "500px",
                  }}
                >
                  <Box sx={{ mb: 3 }}>
                    <Typography
                      variant="h5"
                      sx={{
                        fontWeight: 600,
                        mb: 0.5,
                        color: "#0F172A",
                        fontSize: "18px",
                        textAlign: "center",
                      }}
                    >
                      Doanh số theo khu vực
                    </Typography>
                    <Typography
                      variant="body2"
                      sx={{ color: "#64748B", fontSize: "14px", textAlign: "center" }}
                    >
                      Phân tích doanh số và doanh thu theo từng khu vực
                    </Typography>
                  </Box>
                  <Box sx={{ flex: 1, display: "flex", alignItems: "center", justifyContent: "center", width: "100%" }}>
                    {loading ? (
                      <Box sx={{ display: "flex", alignItems: "center", justifyContent: "center", height: "100%", width: "100%" }}>
                        <CircularProgress />
                      </Box>
                    ) : regionalData.length > 0 ? (
                      // FIX: Thêm width 100% cho container để tránh co dãn sai
                      <Box sx={{ width: "100%", height: 420 }}>
                        <ResponsiveContainer width="100%" height="100%">
                          <BarChart
                            data={regionalData}
                            // FIX: Tăng margin để nhãn không bị cắt
                            margin={{ top: 20, right: 30, left: 20, bottom: 20 }}
                          >
                            <CartesianGrid strokeDasharray="3 3" stroke="#E2E8F0" vertical={false} />
                            <XAxis
                              dataKey="region"
                              tick={{ fill: "#64748B", fontSize: 11 }}
                              axisLine={{ stroke: "#E2E8F0" }}
                              tickLine={false}
                              tickMargin={10}
                              interval={0} 
                            />
                            <YAxis
                              yAxisId="orders"
                              // FIX: Đặt width cố định để trục không bị nhảy khi số lớn
                              width={60}
                              tick={{ fill: "#64748B", fontSize: 12 }}
                              axisLine={false}
                              tickLine={false}
                              label={{
                                value: "Số lượng đơn",
                                angle: -90,
                                position: "insideLeft",
                                fill: "#94A3B8",
                                fontSize: 12,
                                offset: 0, // FIX: Chỉnh offset
                                style: { textAnchor: 'middle' }
                              }}
                            />
                            <YAxis
                              yAxisId="revenue"
                              orientation="right"
                              // FIX: Đặt width cố định
                              width={60}
                              tickFormatter={(value) => `${value.toFixed(1)}B`}
                              tick={{ fill: "#64748B", fontSize: 12 }}
                              axisLine={false}
                              tickLine={false}
                              label={{
                                value: "Doanh thu (tỉ VNĐ)",
                                angle: 90,
                                position: "insideRight",
                                fill: "#94A3B8",
                                fontSize: 12,
                                offset: 0,
                                style: { textAnchor: 'middle' }
                              }}
                            />
                            <Tooltip
                              cursor={{ fill: 'transparent' }}
                              contentStyle={{
                                borderRadius: 8,
                                border: "none",
                                boxShadow: "0 4px 20px rgba(0,0,0,0.15)",
                              }}
                              formatter={(value, name) => {
                                if (name?.includes("Doanh thu")) {
                                  return [`${Number(value).toFixed(1)}B VNĐ`, name];
                                }
                                return [value, name];
                              }}
                            />
                            <Legend
                              wrapperStyle={{ paddingTop: 20 }}
                              iconType="circle"
                            />
                            <Bar
                              yAxisId="orders"
                              dataKey="sales"
                              name="Số lượng (đơn)"
                              fill="#3B82F6"
                              radius={[4, 4, 0, 0]}
                              barSize={32} // FIX: Kích thước cột cố định để đẹp hơn
                              animationDuration={1500}
                              animationEasing="ease-out"
                            />
                            <Bar
                              yAxisId="revenue"
                              dataKey="revenueBn"
                              name="Doanh thu (tỉ VNĐ)"
                              fill="#10B981"
                              radius={[4, 4, 0, 0]}
                              barSize={32} // FIX: Kích thước cột cố định để đẹp hơn
                            />
                          </BarChart>
                        </ResponsiveContainer>
                      </Box>
                    ) : (
                      <Box
                        sx={{
                          display: "flex",
                          flexDirection: "column",
                          alignItems: "center",
                          justifyContent: "center",
                          height: "350px",
                          width: "100%",
                          color: "#64748B",
                          textAlign: "center",
                        }}
                      >
                        <ChartIcon sx={{ fontSize: 64, mb: 2, opacity: 0.3, color: "#64748B" }} />
                        <Typography variant="h6" sx={{ color: "#0F172A", mb: 1, fontWeight: 600 }}>
                          Không có dữ liệu
                        </Typography>
                        <Typography variant="body2" sx={{ color: "#64748B" }}>
                          Vui lòng tạo báo cáo để xem dữ liệu
                        </Typography>
                      </Box>
                    )}
                  </Box>
                </Paper>
              </Fade>
            </Grid>

            {/* Donut Chart */}
            <Grid item xs={12} lg={6}>
              <Fade in={!loading} timeout={1200}>
                <Paper
                  sx={{
                    p: 4,
                    borderRadius: "12px",
                    backgroundColor: "white",
                    border: "1px solid #E2E8F0",
                    boxShadow: "0 1px 3px 0 rgba(0, 0, 0, 0.05)",
                    height: "100%",
                    display: "flex",
                    flexDirection: "column",
                    minHeight: "500px",
                  }}
                >
                  <Box sx={{ mb: 3 }}>
                    <Typography
                      variant="h5"
                      sx={{
                        fontWeight: 600,
                        mb: 0.5,
                        color: "#0F172A",
                        fontSize: "18px",
                        textAlign: "center",
                      }}
                    >
                      Tỷ trọng doanh số
                    </Typography>
                    <Typography
                      variant="body2"
                      sx={{ color: "#64748B", fontSize: "14px", textAlign: "center" }}
                    >
                      Phân bổ doanh số theo khu vực
                    </Typography>
                  </Box>
                  <Box sx={{ flex: 1, display: "flex", flexDirection: "column", width: "100%" }}>
                    <Box sx={{ flex: 1, display: "flex", alignItems: "center", justifyContent: "center", width: "100%", minHeight: 300 }}>
                      {loading ? (
                        <Box sx={{ display: "flex", alignItems: "center", justifyContent: "center", height: "100%", width: "100%" }}>
                          <CircularProgress />
                        </Box>
                      ) : pieData.length > 0 ? (
                        <ResponsiveContainer width="100%" height={300}>
                          <PieChart>
                            <Pie
                              data={pieData}
                              dataKey="value"
                              nameKey="name"
                              cx="50%"
                              cy="50%"
                              innerRadius={80} 
                              outerRadius={110} 
                              paddingAngle={5}
                              label={false}
                              labelLine={false}
                            >
                              {pieData.map((entry, index) => (
                                <Cell
                                  key={`cell-${index}`}
                                  fill={COLORS[index % COLORS.length]}
                                />
                              ))}
                            </Pie>
                            <Tooltip
                              contentStyle={{
                                borderRadius: 8,
                                border: "none",
                                boxShadow: "0 4px 20px rgba(0,0,0,0.15)",
                              }}
                            />
                          </PieChart>
                        </ResponsiveContainer>
                      ) : (
                        <Box
                          sx={{
                            display: "flex",
                            flexDirection: "column",
                            alignItems: "center",
                            justifyContent: "center",
                            height: "350px",
                            width: "100%",
                            color: "#64748B",
                            textAlign: "center",
                          }}
                        >
                          <ChartIcon sx={{ fontSize: 64, mb: 2, opacity: 0.3, color: "#64748B" }} />
                          <Typography variant="h6" sx={{ color: "#0F172A", mb: 1, fontWeight: 600 }}>
                            Không có dữ liệu
                          </Typography>
                          <Typography variant="body2" sx={{ color: "#64748B" }}>
                            Vui lòng tạo báo cáo để xem dữ liệu
                          </Typography>
                        </Box>
                      )}
                    </Box>

                    <Divider sx={{ my: 3 }} />

                    <Box
                      sx={{
                        display: "flex",
                        flexDirection: "column",
                        gap: 2,
                      }}
                    >
                      {proportionData.length > 0
                        ? proportionData.map((d, i) => (
                          <Box
                            key={d.region}
                            sx={{
                              display: "flex",
                              alignItems: "center",
                              gap: 2,
                              p: 1.5,
                              borderRadius: 2,
                              background: `${COLORS[i % COLORS.length]}15`,
                              transition: "all 0.2s ease",
                              "&:hover": {
                                background: `${COLORS[i % COLORS.length]}20`,
                              },
                            }}
                          >
                            <Box
                              sx={{
                                width: 16,
                                height: 16,
                                borderRadius: "50%",
                                bgcolor: COLORS[i % COLORS.length],
                                boxShadow: `0 2px 4px ${COLORS[i % COLORS.length]}40`,
                              }}
                            />
                            <Box sx={{ flex: 1 }}>
                              <Typography
                                variant="body2"
                                sx={{ fontWeight: 600, mb: 0.25 }}
                              >
                                {d.region}
                              </Typography>
                              <Typography
                                variant="caption"
                                sx={{ color: "text.secondary" }}
                              >
                                {d.sales} xe • {(d.revenue / 1000000000).toFixed(1)}B VND
                              </Typography>
                            </Box>
                            <Typography
                              variant="body2"
                              sx={{
                                fontWeight: 700,
                                color: COLORS[i % COLORS.length],
                              }}
                            >
                              {d.salesPercentage?.toFixed(1) || 0}%
                            </Typography>
                          </Box>
                        ))
                        : !loading && (
                          <Typography
                            variant="body2"
                            color="text.secondary"
                            sx={{ textAlign: "center", py: 2 }}
                          >
                            Không có dữ liệu
                          </Typography>
                        )}
                    </Box>
                  </Box>
                </Paper>
              </Fade>
            </Grid>
          </Grid>
        </Box>

        {/* Export Buttons - Centered */}
        <Box
          sx={{
            display: "flex",
            justifyContent: "center",
            gap: 2,
            flexWrap: "wrap",
          }}
        >
          <Button
            variant="contained"
            startIcon={<DownloadIcon />}
            onClick={() => handleExport("sales")}
            disabled={exporting}
            sx={{
              px: 4,
              py: 1.5,
              backgroundColor: "#10B981",
              color: "white",
              fontWeight: 600,
              textTransform: "none",
              borderRadius: "8px",
              "&:hover": {
                backgroundColor: "#059669",
              },
              "&:disabled": {
                backgroundColor: "#D1D5DB",
                color: "#9CA3AF",
              },
              transition: "all 0.2s ease",
            }}
          >
            {exporting ? "Đang xuất..." : "Xuất Sales CSV"}
          </Button>
          <Button
            variant="contained"
            startIcon={<DownloadIcon />}
            onClick={() => handleExport("inventory")}
            disabled={exporting}
            sx={{
              px: 4,
              py: 1.5,
              backgroundColor: "#F59E0B",
              color: "white",
              fontWeight: 600,
              textTransform: "none",
              borderRadius: "8px",
              "&:hover": {
                backgroundColor: "#D97706",
              },
              "&:disabled": {
                backgroundColor: "#D1D5DB",
                color: "#9CA3AF",
              },
              transition: "all 0.2s ease",
            }}
          >
            {exporting ? "Đang xuất..." : "Xuất Inventory CSV"}
          </Button>
        </Box>
      </Container>

      {/* Demand Forecast — self-fetching chart (sales-summary + demand-forecast) */}
      <Container maxWidth="xl" sx={{ mt: 4 }}>
        <DemandForecastChart />
      </Container>

      {/* Sales by Staff */}
      <Container maxWidth="xl" sx={{ mt: 4 }}>
        <Paper
          sx={{
            p: 4,
            borderRadius: "12px",
            backgroundColor: "white",
            border: "1px solid #E2E8F0",
            boxShadow: "0 1px 3px 0 rgba(0, 0, 0, 0.05)",
          }}
        >
          <Box sx={{ mb: 3, textAlign: "center" }}>
            <Typography variant="h5" sx={{ fontWeight: 700, color: "#0F172A" }}>
              Doanh số theo nhân viên bán hàng
            </Typography>
            <Typography variant="body2" sx={{ color: "#64748B" }}>
              Theo dõi hiệu suất theo từng nhân viên, số giao dịch và tỷ lệ chuyển đổi
            </Typography>
          </Box>
          {loading ? (
            <Skeleton variant="rectangular" height={260} sx={{ borderRadius: 2 }} />
          ) : staffPerformance.length > 0 ? (
            <TableContainer>
              <Table>
                <TableHead>
                  <TableRow>
                    <TableCell>Nhân viên</TableCell>
                    <TableCell align="right">Giao dịch</TableCell>
                    <TableCell align="right">Xe bán</TableCell>
                    <TableCell align="right">Doanh thu</TableCell>
                    <TableCell align="right">Tỷ lệ chuyển đổi</TableCell>
                  </TableRow>
                </TableHead>
                <TableBody>
                  {staffPerformance.map((staff) => (
                    <TableRow
                      key={staff.id || staff.name}
                      sx={{
                        "&:hover": { backgroundColor: "#F8FAFC" },
                      }}
                    >
                      <TableCell>
                        <Typography sx={{ fontWeight: 600 }}>{staff.name}</Typography>
                        <Typography variant="body2" sx={{ color: "#64748B", mt: 0.5 }}>
                          {`${staff.totalQuotes ?? 0} báo giá • ${staff.totalOrders ?? 0} đơn hàng • ${staff.totalContracts ?? 0} hợp đồng`}
                        </Typography>
                      </TableCell>
                      <TableCell align="right">{staff.totalDeals ?? (staff.totalQuotes ?? 0) + (staff.totalOrders ?? 0) + (staff.totalContracts ?? 0)}</TableCell>
                      <TableCell align="right">{staff.totalSales ?? staff.totalVehiclesSold ?? 0}</TableCell>
                      <TableCell align="right">{formatCurrency(staff.revenue)}</TableCell>
                      <TableCell align="right">{formatPercent((staff.conversionRate || 0) * 100)}</TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </TableContainer>
          ) : (
            <Typography variant="body2" sx={{ color: "#64748B", textAlign: "center" }}>
              Không có dữ liệu nhân viên
            </Typography>
          )}
        </Paper>
      </Container>

      {/* Debt Report */}
      <Container maxWidth="xl" sx={{ mt: 4 }}>
        <Box sx={{ mb: 3, textAlign: "center" }}>
          <Typography variant="h5" sx={{ fontWeight: 700, color: "#0F172A" }}>
            Báo cáo công nợ
          </Typography>
          <Typography variant="body2" sx={{ color: "#64748B" }}>
            Theo dõi công nợ khách hàng và hãng xe để chủ động kế hoạch thu hồi, thanh toán
          </Typography>
        </Box>
        <Grid container spacing={3} justifyContent="center">
          <Grid item xs={12} md={6} lg={5} sx={{ display: "flex", justifyContent: "center" }}>
            <Paper
              sx={{
                p: 3,
                borderRadius: "12px",
                backgroundColor: "white",
                border: "1px solid #E2E8F0",
                height: "100%",
                width: "100%",
                maxWidth: 520,
              }}
            >
              <Box sx={{ display: "flex", alignItems: "center", gap: 2, mb: 2 }}>
                <Typography variant="h6" sx={{ fontWeight: 700 }}>
                  Công nợ khách hàng
                </Typography>
                <Chip
                  size="small"
                  label="Cần thu"
                  color="success"
                  sx={{ fontWeight: 600 }}
                />
              </Box>
              {loading ? (
                <Skeleton variant="rectangular" height={220} sx={{ borderRadius: 2 }} />
              ) : customerDebt.length > 0 ? (
                <TableContainer>
                  <Table size="small">
                    <TableHead>
                      <TableRow>
                        <TableCell>Khách hàng</TableCell>
                        <TableCell align="right">Công nợ</TableCell>
                        <TableCell align="center">Tình trạng</TableCell>
                        <TableCell align="right">Thanh toán gần nhất</TableCell>
                      </TableRow>
                    </TableHead>
                    <TableBody>
                      {customerDebt.map((item) => {
                        const status = getDueStatus(item.dueInDays);
                        return (
                          <TableRow key={item.id || item.name}>
                            <TableCell>
                              <Typography sx={{ fontWeight: 600 }}>{item.name}</Typography>
                            </TableCell>
                            <TableCell align="right">{formatCurrency(item.outstanding)}</TableCell>
                            <TableCell align="center">
                              <Chip
                                size="small"
                                label={status.label}
                                color={status.color === "default" ? "default" : status.color}
                                sx={{
                                  fontWeight: 600,
                                }}
                              />
                            </TableCell>
                            <TableCell align="right">{formatDate(item.lastPaymentDate)}</TableCell>
                          </TableRow>
                        );
                      })}
                    </TableBody>
                  </Table>
                </TableContainer>
              ) : (
                <Typography variant="body2" sx={{ color: "#64748B" }}>
                  Không có dữ liệu công nợ khách hàng
                </Typography>
              )}
            </Paper>
          </Grid>
          <Grid item xs={12} md={6} lg={5} sx={{ display: "flex", justifyContent: "center" }}>
            <Paper
              sx={{
                p: 3,
                borderRadius: "12px",
                backgroundColor: "white",
                border: "1px solid #E2E8F0",
                height: "100%",
                width: "100%",
                maxWidth: 520,
              }}
            >
              <Box sx={{ display: "flex", alignItems: "center", gap: 2, mb: 2 }}>
                <Typography variant="h6" sx={{ fontWeight: 700 }}>
                  Công nợ hãng xe
                </Typography>
                <Chip
                  size="small"
                  label="Cần trả"
                  color="warning"
                  sx={{ fontWeight: 600 }}
                />
              </Box>
              {loading ? (
                <Skeleton variant="rectangular" height={220} sx={{ borderRadius: 2 }} />
              ) : manufacturerDebt.length > 0 ? (
                <TableContainer>
                  <Table size="small">
                    <TableHead>
                      <TableRow>
                        <TableCell>Hãng xe</TableCell>
                        <TableCell align="right">Công nợ</TableCell>
                        <TableCell align="center">Tình trạng</TableCell>
                        <TableCell align="right">Thanh toán gần nhất</TableCell>
                      </TableRow>
                    </TableHead>
                    <TableBody>
                      {manufacturerDebt.map((item) => {
                        const status = getDueStatus(item.dueInDays);
                        return (
                          <TableRow key={item.id || item.name}>
                            <TableCell>
                              <Typography sx={{ fontWeight: 600 }}>{item.name}</Typography>
                            </TableCell>
                            <TableCell align="right">{formatCurrency(item.outstanding)}</TableCell>
                            <TableCell align="center">
                              <Chip
                                size="small"
                                label={status.label}
                                color={status.color === "default" ? "default" : status.color}
                                sx={{ fontWeight: 600 }}
                              />
                            </TableCell>
                            <TableCell align="right">{formatDate(item.lastPaymentDate)}</TableCell>
                          </TableRow>
                        );
                      })}
                    </TableBody>
                  </Table>
                </TableContainer>
              ) : (
                <Typography variant="body2" sx={{ color: "#64748B" }}>
                  Không có dữ liệu công nợ hãng xe
                </Typography>
              )}
            </Paper>
          </Grid>
        </Grid>
      </Container>

      {/* Manufacturer Insights */}
      {canViewManufacturerInsights && (
        <Container maxWidth="xl" sx={{ mt: 4 }}>
          <Grid container spacing={3} justifyContent="center">
            <Grid item xs={12} md={6} sx={{ display: "flex", justifyContent: "center" }}>
              <Paper
                sx={{
                  p: 3,
                  borderRadius: "12px",
                  backgroundColor: "white",
                  border: "1px solid #E2E8F0",
                  height: "100%",
                  width: "100%",
                  maxWidth: 520,
                }}
              >
                <Typography variant="h6" sx={{ fontWeight: 700, mb: 2 }}>
                  Doanh số theo đại lý
                </Typography>
                {loading ? (
                  <Skeleton variant="rectangular" height={280} sx={{ borderRadius: 2 }} />
                ) : dealerSales.length > 0 ? (
                  <TableContainer>
                    <Table size="small">
                      <TableHead>
                        <TableRow>
                          <TableCell>Đại lý</TableCell>
                          <TableCell align="right">Khu vực</TableCell>
                          <TableCell align="right">Xe bán</TableCell>
                          <TableCell align="right">Doanh thu</TableCell>
                          <TableCell align="right">Tiến độ</TableCell>
                        </TableRow>
                      </TableHead>
                      <TableBody>
                        {dealerSales.map((dealer) => {
                          const progress =
                            dealer.target > 0 ? Math.min((dealer.totalSales / dealer.target) * 100, 120) : 0;
                          return (
                            <TableRow key={dealer.dealerId || dealer.dealerName}>
                              <TableCell>
                                <Typography sx={{ fontWeight: 600 }}>{dealer.dealerName}</Typography>
                              </TableCell>
                              <TableCell align="right">{dealer.region}</TableCell>
                              <TableCell align="right">{dealer.totalSales}</TableCell>
                              <TableCell align="right">{formatCurrency(dealer.revenue)}</TableCell>
                              <TableCell align="right" sx={{ minWidth: 120 }}>
                                <LinearProgress
                                  variant="determinate"
                                  value={Math.min(progress, 100)}
                                  sx={{
                                    height: 8,
                                    borderRadius: 5,
                                    mb: 0.5,
                                  }}
                                />
                                <Typography variant="caption" color="text.secondary">
                                  {progress.toFixed(0)}% mục tiêu
                                </Typography>
                              </TableCell>
                            </TableRow>
                          );
                        })}
                      </TableBody>
                    </Table>
                  </TableContainer>
                ) : (
                  <Typography variant="body2" sx={{ color: "#64748B" }}>
                    Không có dữ liệu đại lý
                  </Typography>
                )}
              </Paper>
            </Grid>

            <Grid item xs={12} md={6} sx={{ display: "flex", justifyContent: "center" }}>
              <Paper
                sx={{
                  p: 3,
                  borderRadius: "12px",
                  backgroundColor: "white",
                  border: "1px solid #E2E8F0",
                  height: "100%",
                  width: "100%",
                  maxWidth: 520,
                }}
              >
                <Typography variant="h6" sx={{ fontWeight: 700, mb: 2 }}>
                  Tồn kho & tốc độ tiêu thụ
                </Typography>
                {loading ? (
                  <Skeleton variant="rectangular" height={280} sx={{ borderRadius: 2 }} />
                ) : inventoryTrends.length > 0 ? (
                  <ResponsiveContainer width="100%" height={260}>
                    <LineChart data={inventoryTrends} margin={{ top: 20, right: 30, left: 10, bottom: 0 }}>
                      <CartesianGrid strokeDasharray="3 3" stroke="#E2E8F0" />
                      <XAxis dataKey="month" tick={{ fill: "#64748B" }} />
                      <YAxis tick={{ fill: "#64748B" }} />
                      <Tooltip />
                      <Legend />
                      <Line type="monotone" dataKey="inventory" stroke="#3B82F6" strokeWidth={2} name="Tồn kho" />
                      <Line type="monotone" dataKey="sold" stroke="#10B981" strokeWidth={2} name="Xe bán" />
                    </LineChart>
                  </ResponsiveContainer>
                ) : (
                  <Typography variant="body2" sx={{ color: "#64748B" }}>
                    Không có dữ liệu tồn kho
                  </Typography>
                )}
              </Paper>
            </Grid>
          </Grid>

          <Paper
            sx={{
              mt: 3,
              p: 4,
              borderRadius: "12px",
              backgroundColor: "white",
              border: "1px solid #E2E8F0",
              maxWidth: 1080,
              mx: "auto",
            }}
          >
            <Box sx={{ textAlign: "center", mb: 3 }}>
              <Typography variant="h6" sx={{ fontWeight: 700 }}>
                Dự báo nhu cầu (AI)
              </Typography>
              <Chip
                label="Tính năng đang phát triển"
                color="warning"
                size="small"
                sx={{ mt: 1, fontWeight: 600 }}
              />
              <Typography variant="body2" sx={{ color: "#64748B", mt: 1 }}>
                Dữ liệu dựa trên xu hướng bán hàng và tín hiệu thị trường
              </Typography>
            </Box>
            <Typography variant="body2" sx={{ color: "#64748B", textAlign: "center" }}>
              Tính năng đang phát triển – dữ liệu sẽ được cập nhật khi hoàn tất.
            </Typography>
          </Paper>
        </Container>
      )}
    </Box>
  );
};

export default Reports;