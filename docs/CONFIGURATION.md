# ⚙️ Configuration Reference

The admin dashboard is available at **Dashboard → Plugins → NoPayNoPlay**.

---

## 📋 Settings Tab

### Subscription

| Setting | Default | Description |
|---|---|---|
| **Monthly price** | `10` | Base monthly subscription amount |
| **Currency code** | `EUR` | ISO 4217 currency code (e.g. `EUR`, `USD`, `GBP`) |
| **Trial days on first sign-in** | `7` | Free trial length for new accounts (0 = no trial) |
| **Grace days after expiry** | `2` | Days before playback is blocked once subscription expires |
| **Warn user this many days before expiry** | `3` | Days before expiry when the warning banner appears |

### Payment Instructions

| Setting | Description |
|---|---|
| **PayPal.me link** | Full PayPal.me URL (e.g. `https://paypal.me/yourhandle`) — shown in the user modal with a QR code |
| **Lydia link** | Full Lydia pot/app URL — shown in the user modal with a QR code |
| **Custom note** | Free-form text (e.g. IBAN, bank details, instructions) displayed in the user modal |
| **Contact email** | Email for user inquiries — shown as a `mailto:` link in the modal |

### Appearance

| Setting | Description |
|---|---|
| **Plugin language** | Override the auto-detected UI language. Set to "Auto" to follow the Jellyfin server language |

### Theme Preview

A live preview section below the Appearance settings shows the detected theme colours:

| Swatch | Shows |
|---|---|
| 🟦 **Accent** | The current accent colour (Jellyfin blue `#00a4dc`, ElegantFin violet `rgb(117 111 226)`, or custom theme) |
| ⬜ **Surface** | Derived card/surface background from `currentColor` |
| 🔲 **Border** | Derived border colour from `currentColor` |
| 🔤 **Text** | Current text colour |
| ⭕ **Radius** | Detected border radius value in pixels (from ElegantFin's `--smallRadius` or fallback) |

The swatches update automatically when the theme changes, giving admins immediate feedback on how the plugin adapts.

### Save

The **sticky save button** at the bottom persists all settings immediately. Configuration backups are created automatically (retention: 10 backups) in `config/NoPayNoPlay.backups/`. The save bar adapts to ElegantFin with `--headerColor` and `backdrop-filter: blur()`.

---

## 👥 Members Tab

### Summary cards

Quick overview with clickable state counts:
- **Up to date** (green) — subscription active
- **Expiring soon** (amber) — within the warning window
- **Grace period** (orange) — expired but within grace days
- **Blocked** (red) — playback disabled
- **Free access** (grey) — manually exempted members

### Revenue stats

Four stat cards showing: revenue this month, last 12 months, all-time, and total transaction count.

A 12-month inline **SVG bar chart** renders below the stats — no external libraries required.

### Filters & search

| Filter | Description |
|---|---|
| **Search** | Filter members by username |
| **Filter by state** | Select a specific subscription state |
| **Filter by payment method** | Show only members who paid via a specific method |
| **Export CSV** | Download the filtered member list as a CSV file |

### Bulk actions

Select multiple users via checkboxes to perform batch operations:
- **Record payment** — apply a payment to all selected users
- **Toggle exemption** — exempt/unexempt all selected users
- **Reset** — reset selected users to a fresh trial
- **Notify** — send a reminder notification to all selected users

### Per-user actions

Each user row has action buttons:

| Button | Action |
|---|---|
| 💳 **Pay** | Record a payment transaction |
| 📜 **History** | View/edit/delete transaction history |
| 🔓/🔒 **Exempt** | Toggle free-access exemption |
| 🔄 **Reset** | Reset the user to a fresh trial |
| 🟡 **Pending claim** (badge) | Confirm or reject a user's "I just paid" declaration |

### Tag selector

Each user can be assigned a tag (e.g. "Family", "Friends", "Guests") which overrides their monthly price. Tag settings are managed in the **Tags** tab.

### Pagination

The member list is paginated (50 per page) with previous/next controls.

---

## 📊 Activity Tab

Shows all payment transactions with:
- **Date** and **time**
- **User** who paid
- **Amount** and **method**
- **Months added**
- **Admin note**

Filters: search by keyword, date range (from/to). Columns are sortable by clicking the headers.

**Export CSV** downloads the filtered activity log with formula-injection protection.

---

## 🎁 Promo Codes Tab

Create and manage promo/referral codes that grant free months.

| Field | Description |
|---|---|
| **Code** | 6–32 characters, uppercase letters, digits, underscores, hyphens |
| **Free months granted** | Number of months the code gives (1–60) |
| **Max uses** | Maximum redemptions (0 = unlimited) |
| **Expires on** | Optional expiry date |

### Creating a code

1. Fill in the form fields
2. Click **Add code**
3. The code appears in the table below

### Managing codes

The promo table shows: code, months granted, current usage count, expiry date, creation date. Click the **🗑️** button to delete a code.

---

## 📦 Tiers Tab

Define subscription packages shown in the user modal (e.g. 1 month, 3 months, 6 months, 12 months).

| Column | Description |
|---|---|
| **Months** | Duration of the tier |
| **Price** | Total price for this tier |
| **Label** | Optional label (e.g. "Most popular", "Best value") |
| **Highlight** | Check to mark this tier as the "Best deal" — it gets a visual badge and is pre-selected |

> **Savings are computed automatically.** If a tier's per-month price is lower than the base monthly price, a "Save X%" chip appears on the card.

Click **Add tier** to append a row, edit inline, then **Save tiers** to persist.

---

## 🏷️ Tags Tab

Group members and override prices per tag.

| Column | Description |
|---|---|
| **Key** | Internal identifier (e.g. `family`, `friends`, `guests`) |
| **Label** | Display label shown in the tag selector |
| **Color** | Color indicator (used visually in future releases) |
| **Price override** | Override the base monthly price for members with this tag (0 = use base price) |

Click **Add tag** to append a row, edit inline, then **Save tags** to persist.

---

## 📝 Audit Log Tab

Records the last 500 administrative actions with:
- **Timestamp**
- **Actor** (admin user)
- **Action** — e.g. `payment.add`, `payment.edit`, `exempt.toggle`, `reset`, `tag.assign`, `promo.add`, `promo.delete`
- **Target** — affected user
- **Details** — free-form description (truncated to 500 chars)

---

## 🧪 Test Mode Tab

Preview the user-facing UI without modifying real data.

1. Select a state from the dropdown
2. Click **Preview in a new tab** to open Jellyfin with `?npnpTest=STATE`
3. Or click **Copy URL** to share the test link

### Simulated behaviours in test mode

- **"I just paid"** button simulates a successful round-trip locally
- **Promo code redemption** accepts any code (except `INVALID` which triggers the error path)
- A **test notification toast** appears once per tab to simulate the bell notification
- A "Test mode" badge is shown on the banner and in the modal

---

## 🔬 Diagnostics Tab

Shows the File Transformation registration status:

- **Registered** — whether the JS injection callback was accepted by File Transformation
- **Found FT assembly** — whether the File Transformation assembly was discovered
- **Callback details** — assembly, class, method names
- **Matching assemblies** — all assemblies containing `.FileTransformation` in their name
- **Notes** — diagnostic messages from the registration attempt

Use **Retry registration** to re-attempt registration without restarting Jellyfin. **Refresh** reloads diagnostics. **Copy raw JSON** copies the full diagnostic object for debugging.
