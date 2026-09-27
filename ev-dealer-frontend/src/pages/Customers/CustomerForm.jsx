import React, { useState } from "react";
import {
  Box,
  Button,
  TextField,
  Grid,
  MenuItem,
  CircularProgress,
} from "@mui/material";

const CustomerForm = ({
  onSubmit,
  onCancel,
  initialData,
  isEdit = false,
  loading = false,
}) => {
  const [formData, setFormData] = useState(() => ({
    name: initialData?.name || "",
    email: initialData?.email || "",
    phone: initialData?.phone || "",
    address: initialData?.address || "",
    status: initialData?.status || "Active",
    dealerId: initialData?.dealerId || "",
    // Issue #150: the password for the login account created with the
    // customer. Only sent when creating — the edit form has no field for it
    // because UpdateCustomerRequest has none either, and an empty string would
    // overwrite a stored password with nothing.
    password: "",
  }));

  const handleChange = (e) => {
    const { name, value } = e.target;
    setFormData((prevData) => ({
      ...prevData,
      [name]: name === "dealerId" && value !== "" ? parseInt(value, 10) : value,
    }));
  };

  const handleSubmit = (e) => {
    e.preventDefault();
    // Issue #150: never send a password on edit. The field is absent from
    // UpdateCustomerRequest, and sending "" would be a silent no-op today but
    // a stored-empty-password bug the moment that request shape changes.
    if (isEdit) {
      // eslint-disable-next-line no-unused-vars -- destructured to be dropped
      const { password, ...rest } = formData;
      onSubmit(rest);
      return;
    }
    onSubmit(formData);
  };

  return (
    <form onSubmit={handleSubmit}>
      <Grid container spacing={3}>
        <Grid item xs={12} sm={6}>
          <TextField
            fullWidth
            label="Name"
            name="name"
            value={formData.name}
            onChange={handleChange}
            required
            disabled={loading}
          />
        </Grid>
        <Grid item xs={12} sm={6}>
          <TextField
            fullWidth
            label="Email"
            name="email"
            type="email"
            value={formData.email}
            onChange={handleChange}
            required
            disabled={loading}
          />
        </Grid>
        <Grid item xs={12} sm={6}>
          <TextField
            fullWidth
            label="Phone"
            name="phone"
            value={formData.phone}
            onChange={handleChange}
            disabled={loading}
          />
        </Grid>
        <Grid item xs={12} sm={6}>
          <TextField
            fullWidth
            label="Address"
            name="address"
            value={formData.address}
            onChange={handleChange}
            disabled={loading}
          />
        </Grid>
        <Grid item xs={12} sm={6}>
          <TextField
            fullWidth
            select
            label="Status"
            name="status"
            value={formData.status}
            onChange={handleChange}
            disabled={loading}
          >
            <MenuItem value="Active">Active</MenuItem>
            <MenuItem value="Inactive">Inactive</MenuItem>
            <MenuItem value="Pending">Pending</MenuItem>
          </TextField>
        </Grid>
        <Grid item xs={12} sm={6}>
          <TextField
            fullWidth
            label="Dealer ID"
            name="dealerId"
            type="number"
            value={formData.dealerId}
            onChange={handleChange}
            required
            disabled={loading}
            inputProps={{ min: 1 }}
          />
        </Grid>
        {/* Issue #150: create-only. The account is provisioned at the moment the
            customer row is created, so a password typed here at edit time would
            have nothing to apply to — the API ignores it. Rendered only when
            !isEdit so the field cannot be filled in and then silently dropped. */}
        {!isEdit && (
          <Grid item xs={12} sm={6}>
            <TextField
              fullWidth
              label="Login Password"
              name="password"
              type="password"
              value={formData.password}
              onChange={handleChange}
              required
              disabled={loading}
              helperText="Set the customer's initial login password. There is no outbound email on this deployment, so it cannot be sent to them — give it to them directly. Min 8 characters."
              inputProps={{ minLength: 8, maxLength: 100 }}
            />
          </Grid>
        )}
        <Grid
          item
          xs={12}
          sx={{ display: "flex", justifyContent: "flex-end", gap: 2, mt: 2 }}
        >
          <Button variant="outlined" onClick={onCancel} disabled={loading}>
            Cancel
          </Button>
          <Button
            type="submit"
            variant="contained"
            color="primary"
            disabled={loading}
          >
            {loading ? (
              <CircularProgress size={24} />
            ) : isEdit ? (
              "Update Customer"
            ) : (
              "Create Customer"
            )}
          </Button>
        </Grid>
      </Grid>
    </form>
  );
};

export default CustomerForm;
