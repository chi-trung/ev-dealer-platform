import React, { useState, useEffect } from "react";
import {
  Box,
  Container,
  CircularProgress,
  Alert,
  Dialog,
  DialogActions,
  DialogContent,
  DialogContentText,
  DialogTitle,
  Button,
  Snackbar,
} from "@mui/material";
import { useNavigate } from "react-router-dom";
import { PageHeader, DataTable } from "../../components/common";
import {
  Add as AddIcon,
  Edit as EditIcon,
  Delete as DeleteIcon,
  Feedback as FeedbackIcon,
} from "@mui/icons-material";
import { format } from "date-fns";
import complaintService from "../../services/complaintService";

const FeedbackList = () => {
  const navigate = useNavigate();
  const [feedbacks, setFeedbacks] = useState([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(null);
  const [dialogOpen, setDialogOpen] = useState(false);
  const [selectedFeedbackId, setSelectedFeedbackId] = useState(null);
  const [snackbar, setSnackbar] = useState({ open: false, message: "" });

  const fetchComplaints = async () => {
    try {
      setLoading(true);
      setError(null);
      const data = await complaintService.getAllComplaints();
      setFeedbacks(Array.isArray(data) ? data : []);
    } catch {
      setError("Failed to fetch feedbacks.");
      setFeedbacks([]);
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    fetchComplaints();
  }, []);

  const handleDeleteClick = (id) => {
    setSelectedFeedbackId(id);
    setDialogOpen(true);
  };

  const handleDialogClose = () => {
    setDialogOpen(false);
    setSelectedFeedbackId(null);
  };

  const handleDeleteConfirm = async () => {
    if (!selectedFeedbackId) return;

    try {
      await complaintService.deleteComplaint(selectedFeedbackId);
      setFeedbacks(feedbacks.filter((fb) => fb.id !== selectedFeedbackId));
      setSnackbar({ open: true, message: "Feedback deleted successfully!" });
    } catch {
      setError("Failed to delete feedback.");
    } finally {
      handleDialogClose();
    }
  };

  const columns = [
    { field: "customerName", headerName: "Customer", width: 200 },
    { field: "subject", headerName: "Subject", width: 250 },
    { field: "status", headerName: "Status", width: 120 },
    {
      field: "createdAt",
      headerName: "Date",
      width: 150,
      valueGetter: (params) =>
        params.row.createdAt
          ? format(new Date(params.row.createdAt), "dd/MM/yyyy")
          : "N/A",
    },
    { field: "actions", headerName: "Actions", type: "actions", width: 120 },
  ];

  const actions = [
    {
      icon: <EditIcon />,
      tooltip: "Edit",
      onClick: (row) => navigate(`/feedbacks/${row.id}/edit`),
      color: "warning.main",
    },
    {
      icon: <DeleteIcon />,
      tooltip: "Delete",
      onClick: (row) => handleDeleteClick(row.id),
      color: "error.main",
    },
  ];

  const pageActions = [
    {
      label: "Add Feedback",
      icon: <AddIcon />,
      variant: "contained",
      color: "primary",
      onClick: () => navigate("/feedbacks/new"),
    },
  ];

  const breadcrumbs = [
    { label: "Dashboard", href: "/dashboard" },
    { label: "Feedbacks", href: "/feedbacks" },
  ];

  return (
    <Container maxWidth="xl">
      <PageHeader
        title="Feedbacks"
        subtitle="Manage customer feedbacks and complaints"
        breadcrumbs={breadcrumbs}
        actions={pageActions}
        icon={<FeedbackIcon />}
      />

      {loading && (
        <Box display="flex" justifyContent="center" mt={4}>
          <CircularProgress />
        </Box>
      )}

      {error && (
        <Box mt={4}>
          <Alert severity="error">{error}</Alert>
        </Box>
      )}

      {!loading && !error && (
        <DataTable
          columns={columns}
          data={feedbacks}
          searchable={true}
          pagination={true}
          actions={actions}
          title="Feedback List"
        />
      )}

      <Dialog open={dialogOpen} onClose={handleDialogClose}>
        <DialogTitle>Confirm Delete</DialogTitle>
        <DialogContent>
          <DialogContentText>
            Are you sure you want to delete this feedback? This action cannot be undone.
          </DialogContentText>
        </DialogContent>
        <DialogActions>
          <Button onClick={handleDialogClose}>Cancel</Button>
          <Button onClick={handleDeleteConfirm} color="error" autoFocus>
            Delete
          </Button>
        </DialogActions>
      </Dialog>

      <Snackbar
        open={snackbar.open}
        autoHideDuration={4000}
        onClose={() => setSnackbar({ open: false, message: "" })}
        message={snackbar.message}
      />
    </Container>
  );
};

export default FeedbackList;
