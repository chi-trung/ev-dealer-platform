import React, { useState, useEffect } from "react";
import { useParams } from "react-router-dom";
import {
  Container,
  Typography,
  Box,
  Button,
  CircularProgress,
  Alert,
  Paper,
  List,
  ListItem,
  ListItemText,
  ListItemIcon,
  Chip,
} from "@mui/material";
import {
  Subject as SubjectIcon,
  Description as DescriptionIcon,
  Person as PersonIcon,
  Category as CategoryIcon,
  CheckCircle as CheckCircleIcon,
  AccessTime as AccessTimeIcon,
} from "@mui/icons-material";
import { PageHeader } from "../../components/common";
import { complaintService } from "../../services/complaintService";
import { format } from "date-fns";

const ComplaintDetailPage = () => {
  const { id } = useParams();
  const [complaint, setComplaint] = useState(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(null);

  useEffect(() => {
    const fetchComplaintData = async () => {
      try {
        setLoading(true);
        const data = await complaintService.getComplaintById(id);
        setComplaint(data);
        setError(null);
      } catch (err) {
        setError("Không thể tải chi tiết khiếu nại.");
        console.error("Lỗi khi tải chi tiết khiếu nại:", err);
      } finally {
        setLoading(false);
      }
    };

    fetchComplaintData();
  }, [id]);

  const breadcrumbs = [
    { label: "Trang chủ", href: "/dashboard" },
    { label: "Khiếu nại", href: "/complaints" },
    { label: "Chi tiết khiếu nại", href: `/complaints/${id}` },
  ];

  // TODO: trang chỉnh sửa khiếu nại chưa tồn tại (route /complaints/:id/edit).

  if (loading) {
    return (
      <Box
        sx={{
          display: "flex",
          justifyContent: "center",
          alignItems: "center",
          height: "80vh",
        }}
      >
        <CircularProgress />
      </Box>
    );
  }

  if (error) {
    return (
      <Container maxWidth="xl" sx={{ mt: 4 }}>
        <Alert severity="error">{error}</Alert>
      </Container>
    );
  }

  if (!complaint) {
    return (
      <Container maxWidth="xl" sx={{ mt: 4 }}>
        <Alert severity="warning">Không tìm thấy dữ liệu khiếu nại.</Alert>
      </Container>
    );
  }

  const getStatusColor = (status) => {
    switch (status) {
      case "Open":
        return "info";
      case "In Progress":
        return "warning";
      case "Resolved":
        return "success";
      case "Closed":
        return "default";
      default:
        return "default";
    }
  };

  return (
    <Box sx={{ display: "flex", flexDirection: "column", minHeight: "100vh" }}>
      <PageHeader
        title={`Khiếu nại: ${complaint.title || 'N/A'}`} // Changed from subject to title
        subtitle="Thông tin chi tiết về khiếu nại của khách hàng"
        breadcrumbs={breadcrumbs}
      />

      <Container maxWidth="md" sx={{ flex: 1, mt: 3, mb: 4 }}>
        <Paper sx={{ p: 4, borderRadius: 3, elevation: 3 }}>
          <Box sx={{ display: "flex", justifyContent: "space-between", alignItems: "center", mb: 3 }}>
            <Typography variant="h5" component="h2" sx={{ fontWeight: 700 }}>
              {complaint.title || 'N/A'} {/* Changed from subject to title */}
            </Typography>
            <Chip
              label={complaint.status || 'N/A'}
              color={getStatusColor(complaint.status)}
              sx={{ fontWeight: "bold" }}
            />
          </Box>

          <List sx={{ p: 0 }}>
            <ListItem sx={{ px: 0, py: 1 }}>
              <ListItemIcon sx={{ minWidth: 40 }}>
                <PersonIcon color="primary" />
              </ListItemIcon>
              <ListItemText primary="Khách hàng" secondary={complaint.customerName || 'N/A'} />
            </ListItem>
            <ListItem sx={{ px: 0, py: 1 }}>
              <ListItemIcon sx={{ minWidth: 40 }}>
                <CategoryIcon color="primary" />
              </ListItemIcon>
              <ListItemText primary="Loại khiếu nại" secondary={complaint.type || 'N/A'} />
            </ListItem>
            <ListItem sx={{ px: 0, py: 1 }}>
              <ListItemIcon sx={{ minWidth: 40 }}>
                <DescriptionIcon color="primary" />
              </ListItemIcon>
              <ListItemText primary="Mô tả" secondary={complaint.description || 'N/A'} />
            </ListItem>
            <ListItem sx={{ px: 0, py: 1 }}>
              <ListItemIcon sx={{ minWidth: 40 }}>
                <AccessTimeIcon color="primary" />
              </ListItemIcon>
              <ListItemText
                primary="Ngày tạo"
                secondary={complaint.createdAt ? format(new Date(complaint.createdAt), "dd/MM/yyyy HH:mm") : "N/A"}
              />
            </ListItem>
            {/* Thêm các trường thông tin khác nếu có */}
          </List>
        </Paper>
      </Container>
    </Box>
  );
};

export default ComplaintDetailPage;
