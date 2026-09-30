/**
 * Trang Danh sách Đại lý - Phiên bản Tiếng Việt
 * Giao diện quản lý đại lý với đầy đủ tính năng tìm kiếm, lọc và thao tác
 */
import React, { useMemo, useState } from "react";
import {
  Box,
  Grid,
  Button,
  TextField,
  MenuItem,
  Chip,
  Card,
  CardContent,
  IconButton,
  Typography,
} from "@mui/material";
import {
  Search as SearchIcon,
  FilterList as FilterIcon,
  Add as AddIcon,
  Edit as EditIcon,
  Visibility as ViewIcon,
  MoreVert as MoreIcon,
} from "@mui/icons-material";
import { PageHeader, DataTable } from "../../components/common";
import authService from "../../services/authService";

const mockDealers = [
  {
    id: "d1",
    name: "Hanoi Motors",
    region: "Miền Bắc",
    contact: "0123 456 789",
    target: "1,000",
    debt: "200,000,000",
    status: "Đang hoạt động",
    address: "123 Hai Bà Trưng, Hà Nội",
    email: "contact@hanoimotors.vn",
    performance: "Xuất sắc",
    sales: "950/1,000",
  },
  {
    id: "d2",
    name: "Saigon Auto",
    region: "Miền Nam",
    contact: "0987 654 321",
    target: "800",
    debt: "50,000,000",
    status: "Đang hoạt động",
    address: "456 Nguyễn Huệ, TP.HCM",
    email: "info@saigonauto.vn",
    performance: "Tốt",
    sales: "720/800",
  },
  {
    id: "d3",
    name: "Central Cars",
    region: "Miền Trung",
    contact: "024 111 222",
    target: "500",
    debt: "0",
    status: "Ngừng hoạt động",
    address: "789 Lê Lợi, Đà Nẵng",
    email: "support@centralcars.vn",
    performance: "Trung bình",
    sales: "380/500",
  },
  {
    id: "d4",
    name: "Mekong Vehicles",
    region: "Miền Nam",
    contact: "029 333 444",
    target: "600",
    debt: "15,000,000",
    status: "Đang hoạt động",
    address: "321 Trần Hưng Đạo, Cần Thơ",
    email: "sales@mekongvehicles.vn",
    performance: "Tốt",
    sales: "550/600",
  },
];

const DealerList = () => {
  const user = authService.getCurrentUser();
  const role = user?.role ? String(user.role).toLowerCase() : "customer";

  const [query, setQuery] = useState("");
  const [region, setRegion] = useState("Tất cả");
  const [statusFilter, setStatusFilter] = useState("Tất cả");

  const getStatusColor = (status) => {
    switch (status) {
      case "Đang hoạt động":
        return "success";
      case "Ngừng hoạt động":
        return "default";
      default:
        return "primary";
    }
  };

  const getPerformanceColor = (performance) => {
    switch (performance) {
      case "Xuất sắc":
        return "success";
      case "Tốt":
        return "info";
      case "Trung bình":
        return "warning";
      case "Kém":
        return "error";
      default:
        return "default";
    }
  };

  const columns = useMemo(
    () => [
      {
        field: "name",
        headerName: "Tên đại lý",
        width: 200,
        renderCell: (params) => (
          <Box>
            <Typography variant="subtitle1" fontWeight="bold">
              {params.value}
            </Typography>
            <Typography variant="caption" color="text.secondary">
              {params.row.email}
            </Typography>
          </Box>
        ),
      },
      {
        field: "region",
        headerName: "Khu vực",
        width: 120,
        renderCell: (params) => (
          <Chip label={params.value} size="small" variant="outlined" />
        ),
      },
      {
        field: "contact",
        headerName: "Liên hệ",
        width: 140,
      },
      {
        field: "target",
        headerName: "Mục tiêu",
        width: 120,
        renderCell: (params) => (
          <Box>
            <Typography variant="body2" fontWeight="medium">
              {params.row.sales}
            </Typography>
            <Typography variant="caption" color="text.secondary">
              Mục tiêu: {params.value}
            </Typography>
          </Box>
        ),
      },
      {
        field: "debt",
        headerName: "Công nợ",
        width: 140,
        renderCell: (params) => (
          <Typography
            variant="body2"
            color={params.value !== "0" ? "error" : "success.main"}
            fontWeight="medium"
          >
            {params.value} VND
          </Typography>
        ),
      },
      {
        field: "status",
        headerName: "Trạng thái",
        width: 120,
        renderCell: (params) => (
          <Chip
            label={params.value}
            color={getStatusColor(params.value)}
            size="small"
          />
        ),
      },
      {
        field: "performance",
        headerName: "Hiệu suất",
        width: 130,
        renderCell: (params) => (
          <Chip
            label={params.value}
            color={getPerformanceColor(params.value)}
            size="small"
            variant="outlined"
          />
        ),
      },
      {
        field: "actions",
        headerName: "Thao tác",
        width: 120,
        renderCell: () => (
          <Box sx={{ display: "flex", gap: 1 }}>
            <IconButton size="small" color="primary" title="Xem chi tiết">
              <ViewIcon fontSize="small" />
            </IconButton>
            {role === "admin" && (
              <IconButton size="small" color="secondary" title="Chỉnh sửa">
                <EditIcon fontSize="small" />
              </IconButton>
            )}
          </Box>
        ),
      },
    ],
    [role]
  );

  const filtered = mockDealers.filter((d) => {
    const matchesQuery =
      !query ||
      d.name.toLowerCase().includes(query.toLowerCase()) ||
      d.email.toLowerCase().includes(query.toLowerCase());
    const matchesRegion = region === "Tất cả" || d.region === region;
    const matchesStatus =
      statusFilter === "Tất cả" || d.status === statusFilter;
    return matchesQuery && matchesRegion && matchesStatus;
  });

  return (
    <Box sx={{ p: { xs: 2, md: 3 } }}>
      <PageHeader
        title="Quản lý Đại lý"
        subtitle="Theo dõi và quản lý hiệu suất mạng lưới đại lý của bạn"
        stats={[
          { label: "Tổng số đại lý", value: mockDealers.length },
          {
            label: "Đang hoạt động",
            value: mockDealers.filter((d) => d.status === "Đang hoạt động")
              .length,
          },
          {
            label: "Khu vực",
            value: [...new Set(mockDealers.map((d) => d.region))].length,
          },
        ]}
        actions={
          role === "admin"
            ? [
                {
                  label: "Thêm đại lý mới",
                  icon: <AddIcon />,
                  onClick: () => alert("Mở modal thêm đại lý mới"),
                  variant: "contained",
                },
              ]
            : []
        }
      />

      <Card sx={{ mb: 3, boxShadow: 2 }}>
        <CardContent>
          <Grid container spacing={2} alignItems="center">
            <Grid item xs={12} md={4}>
              <TextField
                fullWidth
                placeholder="Tìm kiếm đại lý theo tên hoặc email..."
                value={query}
                onChange={(e) => setQuery(e.target.value)}
                InputProps={{
                  startAdornment: (
                    <SearchIcon sx={{ mr: 1, color: "text.secondary" }} />
                  ),
                }}
              />
            </Grid>
            <Grid item xs={6} md={2}>
              <TextField
                select
                fullWidth
                value={region}
                onChange={(e) => setRegion(e.target.value)}
                InputProps={{
                  startAdornment: (
                    <FilterIcon sx={{ mr: 1, color: "text.secondary" }} />
                  ),
                }}
              >
                <MenuItem value="Tất cả">Tất cả khu vực</MenuItem>
                <MenuItem value="Miền Bắc">Miền Bắc</MenuItem>
                <MenuItem value="Miền Nam">Miền Nam</MenuItem>
                <MenuItem value="Miền Trung">Miền Trung</MenuItem>
              </TextField>
            </Grid>
            <Grid item xs={6} md={2}>
              <TextField
                select
                fullWidth
                value={statusFilter}
                onChange={(e) => setStatusFilter(e.target.value)}
              >
                <MenuItem value="Tất cả">Tất cả trạng thái</MenuItem>
                <MenuItem value="Đang hoạt động">Đang hoạt động</MenuItem>
                <MenuItem value="Ngừng hoạt động">Ngừng hoạt động</MenuItem>
              </TextField>
            </Grid>
            <Grid item xs={12} md={4} sx={{ textAlign: { md: "right" } }}>
              <Button
                variant="outlined"
                startIcon={<FilterIcon />}
                sx={{ mr: 1 }}
              >
                Bộ lọc nâng cao
              </Button>
              <Button variant="outlined">Xuất file</Button>
            </Grid>
          </Grid>
        </CardContent>
      </Card>

      <DataTable
        columns={columns}
        data={filtered}
        title="Danh sách Đại lý"
        enableSelection={role === "admin"}
        enablePagination
        pageSize={5}
        rowsPerPageOptions={[5, 10, 25]}
      />
    </Box>
  );
};

export default DealerList;
