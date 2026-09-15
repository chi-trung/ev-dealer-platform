/**
 * Dealer Detail Page
 * Trang route /dealers/:id — layout đầy đủ (info, metrics, LineChart, debt,
 * contracts) nhưng dữ liệu đang là mock hardcoded trong file (chưa đọc theo
 * :id, chưa gọi API); các nút Edit/Set Targets/Upload chỉ alert placeholder.
 */

import React from "react";
import { Box, Grid, Paper, Typography, Button, Avatar } from "@mui/material";
import { PageHeader, ModernCard } from "../../components/common";
import LineChart from "../../components/charts/LineChart";
import { useParams } from "react-router-dom";

// Mock dealer data
const dealer = {
  id: "d1",
  name: "Hanoi Motors",
  region: "North",
  contact: "0123 456 789",
  address: "123 Tran Phu, Hanoi",
  debt: 200000000,
};

const monthlyPerformance = [
  { month: "Jan", performance: 120 },
  { month: "Feb", performance: 95 },
  { month: "Mar", performance: 140 },
  { month: "Apr", performance: 110 },
  { month: "May", performance: 160 },
  { month: "Jun", performance: 130 },
];

const DealerDetail = () => {
  const { id } = useParams();

  return (
    <Box sx={{ p: 3 }}>
      <PageHeader
        title={dealer.name}
        subtitle={`${dealer.region} - ${dealer.contact}`}
      />

      <Grid container spacing={3} sx={{ mb: 3 }}>
        <Grid item xs={12} md={4}>
          <Paper sx={{ p: 3, borderRadius: 2 }}>
            <Box sx={{ display: "flex", alignItems: "center", mb: 2 }}>
              <Avatar sx={{ mr: 2 }}>{dealer.name.charAt(0)}</Avatar>
              <Box>
                <Typography variant="h6">{dealer.name}</Typography>
                <Typography variant="body2" color="text.secondary">
                  {dealer.address}
                </Typography>
                <Typography variant="body2" color="text.secondary">
                  Contact: {dealer.contact}
                </Typography>
              </Box>
            </Box>

            <ModernCard
              title="Debt"
              value={`${dealer.debt.toLocaleString()} VNĐ`}
              color="warning"
            />

            <Box sx={{ mt: 2 }}>
              <Button
                variant="contained"
                sx={{ mr: 1 }}
                onClick={() => alert("Open edit dealer")}
              >
                Edit Dealer
              </Button>
              <Button
                variant="outlined"
                onClick={() => alert("Open set targets")}
              >
                Set Targets
              </Button>
            </Box>
          </Paper>
        </Grid>

        <Grid item xs={12} md={8}>
          <Grid container spacing={2}>
            <Grid item xs={12} md={4}>
              <ModernCard title="Monthly Sales" value="1,200" color="success" />
            </Grid>
            <Grid item xs={12} md={4}>
              <ModernCard
                title="Conversion Rate"
                value="5.2%"
                color="primary"
              />
            </Grid>
            <Grid item xs={12} md={4}>
              <ModernCard title="Returns" value="3" color="error" />
            </Grid>

            <Grid item xs={12}>
              <Paper sx={{ p: 2, borderRadius: 2 }}>
                <Typography variant="h6" sx={{ mb: 1 }}>
                  Monthly Performance
                </Typography>
                <LineChart
                  data={monthlyPerformance}
                  xKey="month"
                  yKey="performance"
                  title="Performance (units)"
                />
              </Paper>
            </Grid>

            <Grid item xs={12} md={6}>
              <Paper sx={{ p: 2, borderRadius: 2 }}>
                <Typography variant="h6">Debt Information</Typography>
                <Typography variant="body2">
                  Total outstanding debt: {dealer.debt.toLocaleString()} VNĐ
                </Typography>
                <Button sx={{ mt: 1 }} variant="outlined">
                  View repayment schedule
                </Button>
              </Paper>
            </Grid>

            <Grid item xs={12} md={6}>
              <Paper sx={{ p: 2, borderRadius: 2 }}>
                <Typography variant="h6">Contracts</Typography>
                <Typography variant="body2">
                  No contract uploaded yet.
                </Typography>
                <Box sx={{ mt: 1 }}>
                  <Button
                    variant="contained"
                    sx={{ mr: 1 }}
                    onClick={() => alert("Upload contract")}
                  >
                    Upload Contract
                  </Button>
                  <Button
                    variant="outlined"
                    onClick={() => alert("View contract")}
                  >
                    View
                  </Button>
                </Box>
              </Paper>
            </Grid>
          </Grid>
        </Grid>
      </Grid>
    </Box>
  );
};

export default DealerDetail;
