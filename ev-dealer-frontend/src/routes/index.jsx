/**
 * Route Configuration — toàn bộ route của app khai báo trong <AppRoutes /> bên dưới.
 */

import { Routes, Route, Navigate } from "react-router-dom";

// Layouts
import MainLayout from "../layouts/MainLayout";
import AuthLayout from "../layouts/AuthLayout";

// Public Pages
import LandingPage from "../pages/Landing/LandingPage";

// Auth Pages
import Login from "../pages/Auth/Login";
import Register from "../pages/Auth/Register";
import ForgotPassword from "../pages/Auth/ForgotPassword";
import ResetPassword from "../pages/Auth/ResetPassword";

// Main Pages
import Dashboard from "../pages/Dashboard/Dashboard";
import VehicleList from "../pages/Vehicles/VehicleList";
import VehicleDetail from "../pages/Vehicles/VehicleDetail";
import VehicleForm from "../pages/Vehicles/VehicleForm";
import VehicleCompare from "../pages/Vehicles/VehicleCompare";
import SalesList from "../pages/Sales/SalesList";
import QuoteCreate from "../pages/Sales/QuoteCreate";
import OrderDetail from "../pages/Sales/OrderDetail";
import QuoteList from "../pages/Sales/QuoteList"; // Import QuoteList
import QuoteView from "../pages/Sales/QuoteView";
import OrderCreateFromQuote from "../pages/Sales/OrderCreateFromQuote"; // New: Import OrderCreateFromQuote
import ContractCreate from "../pages/Sales/ContractCreate"; // REMOVED: ContractCreate is no longer directly accessible from SalesList
import ContractDetail from "../pages/Sales/ContractDetail"; // New: Import ContractDetail
// import SalesManagementPage from "../pages/Sales/SalesManagementPage"; // REMOVED: Import the new page
import CustomerList from "../pages/Customers/CustomerList";
import CustomerDetail from "../pages/Customers/CustomerDetail";
import CustomerNew from "../pages/Customers/CustomerNew";
import CustomerEdit from "../pages/Customers/CustomerEdit";
import TestDriveForm from "../pages/Customers/TestDriveForm";
import TestDriveList from "../pages/Customers/TestDriveList"; // New: Import TestDriveList
import DealerList from "../pages/Dealers/DealerList";
import DealerDetail from "../pages/Dealers/DealerDetail";
import Reports from "../pages/Reports/Reports";
import Notifications from "../pages/Notifications/Notifications";
import NotificationPreferences from "../pages/Notifications/NotificationPreferences";
import Settings from "../pages/Settings/Settings";

// Admin Pages
import UserManagement from "../pages/Admin/UserManagement";

// Complaint Pages (NEW IMPORTS)
import ComplaintListPage from "../pages/Complaints/ComplaintListPage";
import ComplaintNew from "../pages/Complaints/ComplaintNew"; // Changed from CreateComplaintPage
import ComplaintDetailPage from "../pages/Complaints/ComplaintDetailPage";

// Protected Route
import ProtectedRoute from "../components/common/ProtectedRoute";

const AppRoutes = () => {
  return (
    <Routes>
      {/* Public Routes */}
      <Route path="/" element={<LandingPage />} />
      <Route path="/landing" element={<LandingPage />} />

      {/* Auth Routes */}
      <Route element={<AuthLayout />}>
        <Route path="/login" element={<Login />} />
        <Route path="/register" element={<Register />} />
        <Route path="/forgot-password" element={<ForgotPassword />} />
        <Route path="/reset-password" element={<ResetPassword />} />
      </Route>

      {/* Protected Routes */}
      <Route
        element={
          <ProtectedRoute>
            <MainLayout />
          </ProtectedRoute>
        }
      >
        <Route path="/dashboard" element={<Dashboard />} />

        {/* Vehicle Routes */}
        <Route path="/vehicles" element={<VehicleList />} />
        <Route path="/vehicles/compare" element={<VehicleCompare />} />
        <Route path="/vehicles/new" element={<VehicleForm />} />
        <Route path="/vehicles/:id" element={<VehicleDetail />} />
        <Route path="/vehicles/:id/edit" element={<VehicleForm />} />

        {/* Sales Routes */}
        <Route path="/sales" element={<SalesList />} />
        <Route path="/sales/quote/new" element={<QuoteCreate />} />
        <Route path="/sales/quotes" element={<QuoteList />} /> {/* New QuoteList Route */}
        <Route path="/sales/quotes/:id/view" element={<QuoteView />} />
        <Route path="/sales/orders/create-from-quote/:quoteId" element={<OrderCreateFromQuote />} /> {/* New: Route for creating order from quote */}
        <Route path="/sales/contract/create/:orderId" element={<ContractCreate />} />
        <Route path="/sales/contract/detail/:contractId" element={<ContractDetail />} /> {/* New: Route for contract detail */}
        <Route path="/sales/order/:orderId" element={<OrderDetail />} />
        {/* <Route path="/sales/manage" element={<SalesManagementPage />} */} {/* REMOVED: New Sales Management Route */}

        {/* Customer Routes */}
        <Route path="/customers" element={<CustomerList />} />
        <Route path="/customers/new" element={<CustomerNew />} />
        <Route path="/customers/:id" element={<CustomerDetail />} />
        <Route path="/customers/:id/edit" element={<CustomerEdit />} />
        <Route path="/customers/test-drive/new" element={<TestDriveForm />} />

        {/* Test Drive Routes */}
        <Route path="/test-drives" element={<TestDriveList />} /> {/* New: Route for TestDriveList */}

        {/* Dealer Routes */}
        <Route path="/dealers" element={<DealerList />} />
        <Route path="/dealers/:id" element={<DealerDetail />} />

        {/* Complaints Routes (NEW ROUTES) */}
        <Route path="/complaints" element={<ComplaintListPage />} />
        <Route path="/complaints/new" element={<ComplaintNew />} /> {/* Changed to ComplaintNew */}
        <Route path="/complaints/:id" element={<ComplaintDetailPage />} />

        {/* Reports */}
        <Route path="/reports" element={<Reports />} />

        {/* Notifications (allow capitalized path for compatibility) */}
        <Route path="/notifications" element={<Notifications />} />
        <Route path="/Notifications" element={<Notifications />} />
        <Route
          path="/notifications/preferences"
          element={<NotificationPreferences />}
        />

        {/* Settings */}
        <Route path="/settings" element={<Settings />} />

        {/* Admin Routes */}
        <Route
          path="/admin/users"
          element={
            <ProtectedRoute roles={['Admin']}>
              <UserManagement />
            </ProtectedRoute>
          }
        />
      </Route>

      {/* Default Route */}
      <Route path="*" element={<Navigate to="/" replace />} />
    </Routes>
  );
};

export default AppRoutes;
