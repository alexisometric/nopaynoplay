# 🔌 REST API Reference

All routes are prefixed with `/NoPayNoPlay/`.

---

## Public Endpoints

No authentication required.

### `GET /NoPayNoPlay/Strings`

Returns the active translation bundle for the requesting client.

**Culture resolution** (in order):
1. `?lang=` query parameter
2. `Accept-Language` header
3. Jellyfin server UI culture
4. `en` (fallback)

**Response:**
```json
{
  "lang": "fr",
  "strings": { "state.ok": "À jour", ... },
  "available": ["en", "fr", "es", "de", "it", "pt", "ru", "zh"]
}
```

### `GET /NoPayNoPlay/Web/client.js`

Serves the injected client-side JavaScript (user UI). Called automatically by the File Transformation plugin.

### `GET /NoPayNoPlay/Web/qrcode.js`

Serves the vendored QR code generator.

---

## User Endpoints

Require a valid Jellyfin user session (any authenticated user).

### `GET /NoPayNoPlay/Me`

Returns the current user's subscription state and payment information.

**Response:** `MeDto`

```json
{
  "state": "Ok",
  "daysLeft": 18,
  "expiryDate": "2026-06-20T00:00:00Z",
  "price": 10,
  "currency": "EUR",
  "paypalMeUrl": "https://paypal.me/...",
  "lydiaUrl": "https://lydia-app.com/...",
  "customNote": "IBAN: FR76...",
  "contactEmail": "admin@example.com",
  "graceDays": 2,
  "hasPendingPaymentClaim": false,
  "transactions": [
    { "date": "2026-05-01T00:00:00Z", "amount": 10, "method": "PayPal", "monthsAdded": 1 }
  ],
  "tiers": [
    { "months": 1, "price": 10, "label": "", "highlight": false },
    { "months": 3, "price": 25, "label": "Best deal", "highlight": true },
    { "months": 12, "price": 80, "label": "", "highlight": false }
  ],
  "strings": { ... },
  "lang": "en",
  "isAdminPreview": false
}
```

> **Admin preview:** When accessed by an administrator with no test override active, the response is flagged with `isAdminPreview: true` and the state is set to `WarningSoon` with sample data so admins can preview the UI.

### `POST /NoPayNoPlay/Me/MarkPaid`

Declares that the user has sent a payment.

**Request body:**
```json
{ "method": "PayPal" }
```

**Rate limit:** Once per 30 minutes per user.

**Responses:**
| Status | Meaning |
|---|---|
| `200` | Claim registered, pending admin confirmation |
| `429` | Rate-limited — try again later |

### `POST /NoPayNoPlay/Me/RedeemCode`

Redeems a promo/referral code.

**Request body:**
```json
{ "code": "WELCOME10" }
```

**Responses:**
| Status | Meaning |
|---|---|
| `200` | `{ "ok": true, "monthsAdded": 1 }` |
| `400` | Invalid or expired code |

> Brute-force protection: after several failed attempts, the endpoint enters a temporary lockout.

---

## Admin Endpoints

Require `[Authorize(Policy = Policies.RequiresElevation)]`.

### `GET /NoPayNoPlay/Users`

Returns the enriched subscription list for all known users.

**Response:** Array of `UserSubscriptionDto`

```json
[{
  "userId": "...",
  "username": "jdoe",
  "subscriptionDate": "2026-01-15T00:00:00Z",
  "expiryDate": "2026-06-20T00:00:00Z",
  "isExempt": false,
  "isBlocked": false,
  "state": "Ok",
  "daysLeft": 18,
  "hasPendingPaymentClaim": false,
  "pendingPaymentClaimAt": null,
  "tag": "family",
  "totalPaid": 50,
  "arrearsMonths": 0,
  "transactions": [ ... ]
}]
```

### `GET /NoPayNoPlay/Users/Export.csv`

CSV export of the full member list (with formula-injection protection).

### `POST /NoPayNoPlay/Users/{id}/Pay`

Records a payment for a specific user.

**Request body:** `PaymentDto`
```json
{
  "amount": 10,
  "method": "PayPal",
  "monthsAdded": 1,
  "note": "May payment",
  "date": "2026-05-01T12:00:00Z"
}
```

- `date` is optional — defaults to "now". Useful for backfilling past payments.
- Future dates are clamped to the current date.

### `POST /NoPayNoPlay/Users/{id}/ConfirmPending`

Confirms a user's pending "I just paid" claim and records the payment.

**Request body:** `PaymentDto` (same as `/Pay`)

### `POST /NoPayNoPlay/Users/{id}/RejectPending`

Rejects a pending claim without recording a payment.

### `POST /NoPayNoPlay/Users/{id}/Exempt`

Toggles the exemption flag.

**Request body:**
```json
{ "isExempt": true }
```

### `POST /NoPayNoPlay/Users/{id}/Reset`

Resets the user to a fresh trial. Clears the exemption flag and resets the subscription date to "now".

### `POST /NoPayNoPlay/Users/{id}/Notify`

Manually sends a reminder notification to the user.

### `POST /NoPayNoPlay/Users/{id}/Tag`

Sets the user's tag.

**Request body:**
```json
{ "tag": "family" }
```

### `PATCH /NoPayNoPlay/Users/{id}/Transactions/{txId}`

Edits a transaction entry.

**Request body:** `TransactionPatchDto` (partial update — only provided fields are changed)
```json
{
  "amount": 12,
  "method": "Bank",
  "monthsAdded": 1,
  "note": "Corrected amount",
  "date": "2026-05-01T12:00:00Z"
}
```

### `DELETE /NoPayNoPlay/Users/{id}/Transactions/{txId}`

Deletes a transaction. The user's expiry date is recomputed after deletion.

### `POST /NoPayNoPlay/Users/BulkPay`

Records a payment for multiple users at once.

**Request body:**
```json
{
  "userIds": ["id1", "id2"],
  "payment": { "amount": 10, "method": "PayPal", "monthsAdded": 1 }
}
```

### `POST /NoPayNoPlay/Users/BulkExempt`

Toggles exemption for multiple users.

### `POST /NoPayNoPlay/Users/BulkReset`

Resets multiple users to a fresh trial.

### `POST /NoPayNoPlay/Users/BulkNotify`

Sends a reminder notification to multiple users.

### `GET /NoPayNoPlay/Activity`

Returns the aggregated activity/payment log (sorted by date descending).

**Response:** Array of activity rows with `date`, `username`, `amount`, `method`, `monthsAdded`, `adminNote`.

### `GET /NoPayNoPlay/Activity/Export.csv`

CSV export of the activity log.

### `GET /NoPayNoPlay/Stats`

Returns revenue statistics.

**Response:**
```json
{
  "revenueThisMonth": 120,
  "revenueLast12Months": 1450,
  "revenueAllTime": 3200,
  "transactionCount": 45,
  "currency": "EUR",
  "monthlyLabels": ["2025-06", "2025-07", ...],
  "monthlyAmounts": [80, 95, ...]
}
```

### `GET /NoPayNoPlay/Settings`

Returns the current global plugin settings.

### `POST /NoPayNoPlay/Settings`

Updates global plugin settings. Same schema as the configuration object.

### `GET /NoPayNoPlay/PromoCodes`

Returns all promo codes.

### `POST /NoPayNoPlay/PromoCodes`

Creates a new promo code.

**Request body:**
```json
{
  "code": "SUMMER2026",
  "monthsGranted": 1,
  "maxUses": 10,
  "expiresAt": "2026-09-01T23:59:59Z"
}
```

### `DELETE /NoPayNoPlay/PromoCodes/{id}`

Deletes a promo code by its internal ID.

### `GET /NoPayNoPlay/Status`

Returns the plugin runtime status.

**Response:**
```json
{
  "fileTransformationRegistered": true,
  "subscriptionCount": 15,
  "uptime": "5d 3h 12m"
}
```

### `GET /NoPayNoPlay/Diagnostics`

Returns File Transformation registration diagnostics.

### `GET /NoPayNoPlay/Health`

Lightweight health probe. Returns `200 OK` with `{ "status": "healthy" }`.

### `POST /NoPayNoPlay/Diagnostics/Retry`

Re-attempts File Transformation registration without restarting.
