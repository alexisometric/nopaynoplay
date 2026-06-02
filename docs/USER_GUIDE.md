# 🙋 User Guide

This guide explains how NoPayNoPlay works from a **member's perspective** — what you see, what you can do, and what happens when your subscription expires.

> ⚠️ The user UI (header button, banner, modal) requires the **File Transformation** plugin to be installed on the server. If you don't see these elements, ask your admin to install it.

---

## 💳 Header Button

Once logged into Jellyfin, look for the **💳** (monetization) icon in the top-right header area.

- **Click it** to open your subscription modal
- Shows all relevant information about your plan and payments

---

## 🖥️ Subscription Modal

The modal shows:

### Hero Card (top section)

| State | What you see |
|---|---|
| ✅ **Up to date** | Green card — "18 days left" + next due date |
| ⏳ **Expiring soon** | Amber card — "2 days left" + due date |
| ⚠️ **Grace period** | Orange card — "Expired 2 days ago" + original due date |
| 🚫 **Blocked** | Red card — "Expired 10 days ago" |
| 🆓 **Free access** | Green card — "Free access for you" (exempt users) |

A **progress gauge** shows the remaining time visually.

### Choose a Plan

If the admin has configured subscription **tiers**, you'll see cards like:

| 1 month | 3 months | 6 months ★ Best deal | 12 months |
|---|---|---|---|
| €10 | €25 | €45 | €80 |
| €10/mo | €8.33/mo | €7.50/mo — Save 25% | €6.67/mo — Save 33% |

Click a tier to select it — the payment buttons will update to the tier's amount.

### How to Pay

**PayPal** and/or **Lydia** payment cards are shown with the amount pre-filled:
- Click a card to open the payment link in a new tab
- The amount is pre-filled in the URL (PayPal.me / Lydia)

### "I Just Paid" Button

After sending the money:

1. Click **"I just paid"** to notify the admin
2. Wait for admin confirmation (your modal will show a **"Pending admin confirmation"** banner)
3. Once confirmed, your subscription is extended

> **Rate limit:** You can only use "I just paid" once every 30 minutes.

### Promo / Referral Codes

If you have a promo code:

1. Type the code in the input field
2. Click **Redeem**
3. If valid, your subscription is extended immediately

> Codes are **case-insensitive**. Invalid or expired codes show an error message.

### Payment History

Scroll down to see your transaction history (dates, amounts, methods). If you have more than 5 entries, click **"Show all (N)"** to expand.

### Need Help?

The contact email set by your admin appears at the bottom of the modal.

---

## 📢 Banner

Depending on your subscription state, a sticky banner appears at the top of the page:

| State | Banner colour | What it says | Can dismiss? |
|---|---|---|---|
| ⏳ **Expiring soon** | Amber | "Your subscription expires in 3 days" | ✅ Yes (one-time) |
| ⚠️ **Grace period** | Orange | "Your subscription has expired — grace period" | ❌ No |
| 🚫 **Blocked** | Red | "Playback blocked: subscription expired" | ❌ No |

The **"Pay now"** button on the banner opens the subscription modal.

---

## 🔔 Notifications

You'll receive Jellyfin bell notifications based on your subscription state:

| Milestone | Notification |
|---|---|
| **3 days before expiry** | "Your subscription expires in 3 days" |
| **1 day before expiry** | "Your subscription expires tomorrow" |
| **Day of expiry** | "Your subscription has expired — grace period" |
| **Grace period ended** | "Playback blocked: subscription expired" |

> Each milestone fires **at most once** per cycle — no spam.

---

## 🚫 What happens when blocked

When your subscription expires (after the grace period):

1. **Playback is disabled** — you can't start any new streams
2. **Active sessions are stopped** — current playback is interrupted
3. **Your account is NOT deleted** — you can still log in, browse the library, and access the settings
4. **The banner and modal still work** — pay to restore access immediately

Once the admin records a payment (or you redeem a promo code), everything is restored automatically.

---

## 🆓 Free / Exempt Access

If the admin has marked you as **exempt**:
- You have **permanent free access**
- No expiry date, no countdown, no banner
- The modal shows a **"Free access for you"** card
- A **donation section** invites voluntary contributions (if payment links are configured)

---

## 🔗 Direct Link

You can open the subscription modal directly by navigating to:

```
http://your-server/jellyfin/#!/npnp
```

Useful for bookmarks or links in emails.

---

## 🧪 Test Mode

If your admin has shared a test link with `?npnpTest=STATE`, you can preview different subscription states without affecting real data. A **"Test mode"** badge appears on the banner and in the modal to make it clear this is a simulation.
