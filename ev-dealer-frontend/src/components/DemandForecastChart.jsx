import React, { useState, useEffect } from "react";
import {
  ComposedChart,
  Line,
  Area,
  XAxis,
  YAxis,
  CartesianGrid,
  Tooltip,
  Legend,
  ResponsiveContainer,
} from "recharts";
import {
  Card,
  CardContent,
  CardHeader,
  Alert,
  Skeleton,
} from "@mui/material";
import { reportService } from "../services/reportService";

// Both endpoints speak month keys in "YYYY-MM" form:
// - /reports/sales-summary rows carry an ISO `date` ("2025-12-05T00:00:00"),
//   sliced to "2025-12" (timezone-free, unlike Date parsing of the key);
// - /reports/demand-forecast points already publish `period` as "YYYY-MM".
const monthKey = (value) =>
  typeof value === "string" ? value.slice(0, 7) : "";

const monthLabel = (key) => {
  const [year, month] = key.split("-");
  return `${Number(month)}/${year}`; // "12/2025" — locale-independent
};

const DemandForecastChart = () => {
  const [chartData, setChartData] = useState([]);
  const [meta, setMeta] = useState({
    title: "AI-Powered Demand Forecast",
    description: "Doanh số thực tế theo tháng và dự báo tuyến tính 3 tháng tới",
  });
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(null);

  useEffect(() => {
    let cancelled = false;

    const fetchForecastData = async () => {
      try {
        setLoading(true);
        setError(null);

        // 1. Lấy đồng thời dữ liệu lịch sử và dữ liệu dự báo. Mỗi lời gọi
        // tự bắt lỗi: chỉ khi cả hai đều hỏng mới là lỗi của chart.
        const [summaryRes, forecastRes] = await Promise.all([
          reportService.getSalesSummary().catch((err) => {
            console.error("Error fetching sales summary:", err);
            return null;
          }),
          reportService.getDemandForecast().catch((err) => {
            console.error("Error fetching demand forecast:", err);
            return null;
          }),
        ]);

        if (!summaryRes && !forecastRes) {
          throw new Error("Không tải được dữ liệu dự báo nhu cầu.");
        }

        // 2. Hợp nhất dữ liệu theo tháng (Map keyed by "YYYY-MM", which
        // sorts chronologically as plain strings).
        const byMonth = new Map();
        const ensure = (key) => {
          if (!byMonth.has(key)) {
            byMonth.set(key, { key, label: monthLabel(key) });
          }
          return byMonth.get(key);
        };

        // Doanh số thực tế = tổng totalOrders trong tháng — cùng định nghĩa
        // "sales" mà ForecastingService của ReportingService hồi quy trên đó.
        const summaryRows = Array.isArray(summaryRes?.data)
          ? summaryRes.data
          : [];
        summaryRows.forEach((row) => {
          const key = monthKey(row?.date);
          if (!key) return;
          const point = ensure(key);
          point.actualSales =
            (point.actualSales ?? 0) + Number(row?.totalOrders ?? 0);
        });

        // Dự báo: forecastedValue + band [lower, upper] cho Area range.
        const forecastPoints = Array.isArray(forecastRes?.forecastData)
          ? forecastRes.forecastData
          : [];
        forecastPoints.forEach((item) => {
          const key = monthKey(item?.period);
          if (!key) return;
          const point = ensure(key);
          point.forecast = Number(item?.forecastedValue ?? 0);
          if (
            item?.confidenceLowerBound != null &&
            item?.confidenceUpperBound != null
          ) {
            point.confidenceRange = [
              Number(item.confidenceLowerBound),
              Number(item.confidenceUpperBound),
            ];
          }
        });

        if (cancelled) return;
        setChartData(
          Array.from(byMonth.values()).sort((a, b) =>
            a.key.localeCompare(b.key)
          )
        );
        setMeta({
          title: forecastRes?.title || "AI-Powered Demand Forecast",
          description:
            forecastRes?.description ||
            "Doanh số thực tế theo tháng và dự báo tuyến tính 3 tháng tới",
        });
      } catch (err) {
        if (!cancelled) {
          setError(err?.message || "Không tải được dữ liệu dự báo nhu cầu.");
          console.error(err);
        }
      } finally {
        if (!cancelled) setLoading(false);
      }
    };

    fetchForecastData();
    return () => {
      cancelled = true;
    };
  }, []);

  if (loading) {
    return (
      <Card>
        <CardHeader
          title="AI-Powered Demand Forecast"
          subheader="Đang tải dữ liệu dự báo..."
        />
        <CardContent>
          <Skeleton variant="rectangular" height={300} sx={{ borderRadius: 1 }} />
        </CardContent>
      </Card>
    );
  }

  if (error) {
    return (
      <Card>
        <CardHeader title="AI-Powered Demand Forecast" />
        <CardContent>
          <Alert severity="error">{error}</Alert>
        </CardContent>
      </Card>
    );
  }

  if (chartData.length === 0) {
    return (
      <Card>
        <CardHeader title={meta.title} subheader={meta.description} />
        <CardContent>
          <Alert severity="info">
            Chưa có dữ liệu doanh số để vẽ dự báo.
          </Alert>
        </CardContent>
      </Card>
    );
  }

  return (
    <Card>
      <CardHeader title={meta.title} subheader={meta.description} />
      <CardContent>
        <ResponsiveContainer width="100%" height={300}>
          <ComposedChart
            data={chartData}
            margin={{ top: 5, right: 20, left: -10, bottom: 5 }}
          >
            <CartesianGrid strokeDasharray="3 3" />
            <XAxis dataKey="label" />
            <YAxis allowDecimals={false} />
            <Tooltip />
            <Legend />
            <Area
              type="monotone"
              dataKey="confidenceRange"
              stroke="none"
              fill="#82ca9d"
              fillOpacity={0.2} // Vùng màu xanh mờ cho khoảng tin cậy
              name="Khoảng tin cậy"
            />
            <Line
              type="monotone"
              dataKey="actualSales"
              stroke="#8884d8" // Màu tím cho đường thực tế
              name="Doanh số thực tế"
            />
            <Line
              type="monotone"
              dataKey="forecast"
              stroke="#82ca9d" // Màu xanh cho đường dự báo
              strokeDasharray="5 5"
              name="Dự báo"
            />
          </ComposedChart>
        </ResponsiveContainer>
      </CardContent>
    </Card>
  );
};

export default DemandForecastChart;
