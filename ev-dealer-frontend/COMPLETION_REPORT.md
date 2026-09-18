# Project Completion Report (corrected)

> **Status note:** the version of this file generated 2025-10-20 described a
> planned scaffold that largely never landed: it claimed components
> (`SearchBar.jsx`, `FilterPanel.jsx`, `DateRangePicker.jsx`, `Card.jsx`,
> `Sidebar.jsx`, `Topbar.jsx`, `Pagination.jsx`, `Loading.jsx`), utils
> (`constants.js`, `formatters.js`, `storage.js`), docs
> (`START_HERE/README/QUICK_START/STRUCTURE/TEAM_GUIDE/TASK_CHECKLIST/PROJECT_SUMMARY`)
> and config (`tailwind.config.js`, `postcss.config.js`) that do not exist in
> the repository. Issue #65: this rewrite replaces every inventory claim with
> the tree measured from git-tracked files.
> **Verified against:** `main` @ 53ac11c, 2026-09-16. Regenerate counts with:
> `git ls-files 'ev-dealer-frontend/src'` (115 then; 113 after Issue #67).

## Tổng quan

- **Project:** EV Dealer Management System — Frontend (`ev-dealer-frontend`, v0.0.0)
- **Framework:** React 18.3 + Vite 7.1
- **State:** the app has grown well past the original scaffold. Genuinely new
  since the 2025 report: real 3D (three.js), Firebase push, and a Complaints
  module (the old report never mentioned Complaints pages). The Sales and
  Test-drive modules did appear there — but marked 3/1 files; reality is 8
  Sales pages and the full Customer feedback/test-drive suite. The sections
  below reflect what is actually on disk.

## Thống kê (measured, tracked files only)

| Loại | Số lượng | Chi tiết |
|------|----------|----------|
| **Pages** | 39 jsx (12 modules) | Admin(1) Auth(4) Complaints(4) Customers(9) Dashboard(1) Dealers(2) Landing(1) Notifications(2) Reports(1) Sales(8) Settings(1) Vehicles(5) |
| **Components** | 33 jsx | common(23, incl. 1 test) · charts(3) · Complaints(2) · forms(1) · Notification(1) · vehicles(1) · Car3D · DemandForecastChart |
| **Layouts** | 2 | MainLayout, AuthLayout |
| **Services** | 10 js | api.js + 9 feature services (admin, auth, complaint, customer, dealer, notification, report, testDrive, vehicle) |
| **Firebase** | 3 js | firebaseConfig, messaging, notificationService |
| **Utils** | 2 | imageUtils.js, validators.js |
| **Data / constants** | 5 js | mockDataSales, mockNotifications, mockVehicles, complaintTypes, theme.js |
| **Context** | 1 | AuthContext.jsx |
| **Routing** | 3 | routes/index.jsx, App.jsx, main.jsx |
| **Barrels** | 2 | common/index.js, Notifications/index.js barrels |
| **Styles** | 3 css | index.css, App.css, Auth/Auth.css |
| **Assets** | 6 | 5 webp (bg-hero, car1-4), 1 svg (react.svg) |
| **Docs (frontend root)** | 3 md | DOCS_INDEX.md (entry point), AUTH_MODULE_README.md, this file |
| **Config** | 4 | package.json, vite.config.js, eslint.config.js, .env.example |

**src total: 106 tracked files** (76 jsx · 21 js · 3 css · 6 images), counted
with `git ls-files` on main after Issue #107. Earlier counts in this report
were maintained by hand and drifted; the breakdown above is measured, so
prefer re-running `git ls-files` over trusting the old numbers. History:
115 at 53ac11c → 113 after Issue #67 (Settings/temp.jsx fragment + 0-byte
DemandForecastChart.jsx stub) → 106 after #107 removed the NotificationBell
trio (DemandForecastChart was wired in by #76 along the way).
`tailwind.config.js` / `postcss.config.js` never existed — and see Known gaps:
Tailwind itself is declared but not wired in.

## Modules thực tế (pages directory)

| Module | Files | Ghi chú |
|--------|-------|---------|
| Auth | 4 | Login, Register, ForgotPassword, ResetPassword (+Auth.css) |
| Dashboard | 1 | Dashboard.jsx — no charts on this page (chart components are consumed by Dealers/DealerDetail) |
| Vehicles | 5 | List, Detail, Form, FormModern, Compare — Detail includes the lazy "Mô hình 3D" tab (Car3D, Issue #69) |
| Sales | 8 | SalesList, Quote CRUD-ish (Create/List/View/OrderFromQuote), Contract (Create/Detail), OrderDetail |
| Customers | 9 | List/Detail/New/Edit/Form + TestDrive (Form/List) + Feedback (Form/List) |
| Complaints | 4 | ListPage, DetailPage, New, CreatePage |
| Dealers | 2 | List, Detail |
| Reports | 1 | Reports |
| Notifications | 2 | List, Preferences (Firebase push wired) |
| Settings | 1 | Settings.jsx (temp.jsx orphan fragment deleted, Issue #67) |
| Admin | 1 | UserManagement |
| Landing | 1 | LandingPage |

## Common components (actual list)

Badge, Button, DashboardStatCard, DataTable, Dropdown, Footer, Form, Header,
Hero, Input, Layout, Modal, ModernCard, PageHeader, ProtectedRoute, Section,
StatCard, StatisticCard, Table, Tabs, Toast — barrel `index.js`.

Deleted: `NotificationBell` (+ its `.css` and `.test.jsx`, Issue #107). It was
never imported by any page, layout, route, or the `common/index.js` barrel, and
could not have compiled — it imported `antd` and `framer-motion`, neither of
which is in `package.json`. The header bell was always a plain MUI `IconButton`
in `MainLayout.jsx` that navigates to `/Notifications`.

Not present (old report claimed them): Card, Sidebar, Topbar, Pagination,
Loading, SearchBar, FilterPanel, DateRangePicker. Search exists inside page
code, not as a shared component.

## Tech stack (from package.json)

- **UI:** MUI 7.3 (+emotion), three.js 0.160 + @react-three/fiber/drei (Car3D)
- **Routing/state:** react-router-dom 7.9, zustand 5.0 (installed), AuthContext
- **Data:** axios 1.13, recharts 3.3, date-fns 4.1, es-toolkit 1.42
- **Push:** firebase 12.6
- **Forms:** react-hook-form 7.65
- **Dev:** vite 7.1.7, eslint 9 + react-hooks/refresh plugins

Note: `@types/react` 19.x are dev-deps only; the runtime is React 18.3.1.

## Known gaps (truthful)

- `tailwindcss` 4.1 is in `package.json` dependencies but nothing imports or
  configures it — dead weight until wired (or dropped).
- Orphaned components (complete, working, but no page imports them yet):
  `components/DemandForecastChart.jsx`. (Car3D.jsx was wired into
  Vehicles/VehicleDetail as the "Mô hình 3D" tab, lazy-loaded, Issue #69.)
- Zustand is installed but state lives mostly in context/page code.
- Mock data files (mockVehicles/mockDataSales/mockNotifications) coexist with real service calls.
- Tests: none. There is no frontend test runner installed (no vitest/jest/
  @testing-library in `package.json`) and CI runs only `npm run build` for the
  frontend. The former `NotificationBell.test.jsx` had 18 `no-undef` errors and
  never executed; it was deleted with the component in Issue #107.

---

**Original report generated:** 2025-10-20 · **Rewritten to match reality:** 2026-09-16 (Issue #65)
