# ECommerce checkout frontend

React + Vite + TypeScript checkout for the existing .NET 10 API. It uses the
backend-created PaymentIntent only. It never creates a PaymentIntent or a
Checkout Session, and never calls `/api/webhooks/stripe`.

## Setup

```bash
cd frontend
npm install
cp .env.example .env
npm run dev
```

`.env` is gitignored. Set:

| Variable | Meaning |
| --- | --- |
| `VITE_API_BASE_URL` | API base URL, e.g. `http://localhost:5246` or the HTTPS Railway URL |
| `VITE_STRIPE_PUBLISHABLE_KEY` | Stripe **publishable** key (`pk_test_...`) only |

Never put `sk_*`, `whsec_*`, the JWT signing key, or the database connection
string in `frontend/.env` — Vite embeds all `VITE_*` values in the browser bundle.

## Required API CORS origin

The backend policy is `Cors:AllowedOrigins` and uses `AllowCredentials`, so a
wildcard is not possible. Add the exact frontend origin to the API environment
(as Railway variables, not frontend variables):

```text
Cors__AllowedOrigins__0=https://localhost:5001
Cors__AllowedOrigins__1=http://localhost:5173
Cors__AllowedOrigins__2=https://<your-project>.vercel.app
```

Replace `<your-project>.vercel.app` with the real Vercel domain. If you use a
custom domain, add it as another indexed entry. Opening dev and production at
the same time needs both origins. The API URL on Vercel must be `https://`;
otherwise the browser blocks it as mixed content.

## Flow

1. `POST /api/v1/Auth/login` → JWT kept in `sessionStorage`.
2. Load basket, default address (or add one) and active delivery methods.
3. `POST /api/v1/Orders` with a fresh `Idempotency-Key` (`crypto.randomUUID()`,
   max 128 chars). The same key is reused if creation fails before an order
   exists; a new key is used for the next logical attempt.
4. Read `data.clientSecret`, `data.paymentIntentId` and `data.id` from the
   `ApiResponse<OrderResponse>` envelope. `clientSecret` stays in memory only —
   it is never logged, stored or put in a URL.
5. `<Elements options={{ clientSecret }}>` + `<PaymentElement>` +
   `stripe.confirmPayment({ ..., redirect: 'if_required' })`. Stripe handles
   card entry and 3DS; `return_url` is `/checkout/return?orderId=<id>`.
6. The return page polls `GET /api/v1/Orders/my/{id}` with backoff. The backend
   webhook is the source of truth; a successful browser confirmation is never
   treated as "paid".
7. Failures expose `POST /api/v1/Orders/{id}/retry-payment` and
   `POST /api/v1/Orders/{id}/cancel`.
8. `/admin/refund` uses an Admin JWT and `POST /api/v1/admin/orders/{id}/refund`.

## Test locally

Seed data is only created in Development. Set `SeedAdmin__Password` and
`SeedDemoUsers__Password` in the repository-root `.env`, then restart the API.

```text
Customer: demo.customer@example.com
Admin:    the SeedAdmin__Email account
Password: the value of SeedDemoUsers__Password / SeedAdmin__Password
```

Use Stripe's test card:

```text
4242 4242 4242 4242
Expiry: any future date
CVC:    any 3 digits
ZIP:    any value
```

For 3DS use `4000 0025 0000 3155`. The Stripe webhook must point at the API
(`/api/webhooks/stripe`) or the order will remain "verifying".

## Deploy to Vercel

- Framework preset: Vite, root directory `frontend`.
- Add `VITE_API_BASE_URL` and `VITE_STRIPE_PUBLISHABLE_KEY` as Vercel env vars.
- Add the deployed origin to the API's `Cors__AllowedOrigins__*` as shown above.
- `vercel.json` rewrites unknown paths to `index.html` so the return URL works
  after a hard refresh.
