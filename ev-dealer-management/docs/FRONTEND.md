# Frontend → Backend (via API Gateway)

How the React app in `ev-dealer-frontend/` reaches the microservices.
Phase 3 (Epic #18): W1 (PR #20) routed every API call through the
gateway; W2 verified the routes live against the compose stack.

## One base URL

`ev-dealer-frontend/src/services/api.js` — a single shared axios
instance. `baseURL` is `VITE_API_BASE_URL`, default
`http://localhost:5036/api` (the gateway). The trailing `/api` is part
of the base URL: call sites use **relative paths without the /api
prefix** (`api.get('/Orders')`, never `api.get('/api/Orders')`).

Frontend code must never hardcode `http://localhost:<port>` — raw
service ports work on a dev laptop only. Direct-service env vars are
gone: the old `.env.example` block (`VITE_*_SERVICE_URL` pointing at
ports 5000–5007, services that were never there) was removed; the
gateway is the single documented base URL.

## The `api` contract (read before writing a call site)

1. **Response unwrapping**: the interceptor returns `response.data`,
   so `await api.get('/Quotes')` resolves to the JSON payload. Do not
   read `.data` off the result (one `.data` level was removed from
   every site in W1). Blob calls (`responseType: 'blob'`) resolve to
   the **Blob itself**, not a response object.
2. **Auth + 401**: the request interceptor attaches the Bearer token.
   On a 401 the interceptor clears `localStorage` and hard-redirects
   to `/login` — this now covers report calls too (previously they ran
   on an instance with no auth at all).
3. **Timeout**: default 10s. Pass a per-request override for slow
   server-side work — the PDF endpoint uses
   `{ responseType: 'blob', timeout: 120000 }` because
   `generate-quote-pdf` renders server-side and the pre-W1 raw-axios
   calls had no timeout at all. (Issue #49: the route the buttons called
   never existed until then — SalesService now ships
   `POST /api/Sales/generate-quote-pdf` rendering `QuotePdfDocument`.)
4. **Rejections are Errors** (with `.response` preserved). The old
   reporting instance rejected with plain strings; callers that logged
   or used `err.message` behave better now, not worse.

## Gateway route coverage (W2 verified live)

Probed through the gateway against the 7-service compose stack; all
converted URL shapes return the payload shapes the call sites expect:

| URL shape | Gateway → | Shape verified |
|---|---|---|
| `/api/Orders`, `/Orders/{id}`, `/Orders/complete` | SalesService :5003 | `$id/$values` list / object / 400-on-empty-POST (route exists) |
| `/api/Quotes`, `/Quotes/{id}` | SalesService | `$id/$values`; `{id}` 404 on empty DB (proves wiring, not failure) |
| `/api/Contracts…`, `/Promotions`, `/Deliveries`, `/Payments…`, `/Sales…`, `/ProcessedReservations` | SalesService | 200 on live endpoints (notification-only ones accumulate by design) |
| `/api/customers`, `/TestDrives…`, `/Complaints…`, `/CustomerService/Complaints…` | CustomerService :5039 | arrays / object |
| `/api/vehicles…` | VehicleService :5068 | `{items,totalCount,page,pageSize}` — matches `response.items` |
| `/api/users`, `/users/{id}`, `/auth/…` | UserService :7001 | 200 authenticated (`/users` is Admin-guarded; anonymous 401 proves routing) |
| `/api/reports/…` | ReportingService :5208 | `metrics` object / arrays (`summary`, `sales-by-region`, `top-vehicles?limit=5`, `sales-summary`, `debt-report`) |
| `/api/Notification/…` (+ lowercase `/api/notifications/…` alias) | NotificationService :5051 | FCM controller routes (`405` on GET of a POST-only route proves routing) |

Auth-guarded routes were verified with a throwaway Admin created via
`/api/auth/register` + local SQLite role flip, then deleted; the work
DB `UserService/data/users.db` is untouched (only the pre-existing
`w5probe` row remains).

## Known gaps (not regressions)

- `NotificationBell.jsx` is **not mounted anywhere** (only its own
  test file references it) and contains two dead ends: raw
  `fetch('/api/notifications…')` and
  `new WebSocket(process.env.REACT_APP_WS_URL || 'ws://…:8080')` —
  Vite never defines `process` in browser code, and no WebSocket
  backend exists in this repo. Left as-is (documented in Epic #18).
- `notificationService.js`'s email/SMS/test-sms/health convenience
  methods post to `/notifications/…` paths the backend has no
  controllers for (only PWA-localStorage/notification-stats methods
  have callers). The gateway lowercases-alias routes them to
  `NotificationController`, which answers FCM-topic endpoints
  (`send-to-topic` etc. 500 without Firebase creds). No conversion
  was at stake — W1 left this file alone.
- Pre-existing eslint debt (11 errors at W1 time: `OrderDetail.jsx`,
  `SalesList.jsx:814-815` unused vars, `authService.js` patterns) is
  in code this phase did not touch.
