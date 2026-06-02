# 📦 Installation Guide

## Prerequisites

- **Jellyfin 10.11.x** or compatible version
- **Administrator access** to the Jellyfin dashboard
- _(Recommended)_ [**File Transformation**](https://github.com/IAmParadox27/jellyfin-plugin-file-transformation) plugin for the user-facing UI (banner, modal, header button)

---

## Method 1: Via Repository (recommended)

### Step 1 — Add the repository

1. Go to **Dashboard → Plugins → Repositories**
2. Click the **➕** button
3. Fill in the fields:

| Field | Value |
|---|---|
| **Repository Name** | `NoPayNoPlay` |
| **Repository URL** | `https://raw.githubusercontent.com/alexisometric/nopaynoplay/main/manifest.json` |

4. Click **Save**

### Step 2 — Install the plugin

1. Go to **Dashboard → Plugins → Catalog**
2. Find **NoPayNoPlay** in the list
3. Click **Install**
4. Confirm the installation
5. **Restart Jellyfin** when prompted

### Step 3 — Install the companion plugin (recommended)

The user-facing UI (header button, subscription banner, payment modal) requires the **[File Transformation](https://github.com/IAmParadox27/jellyfin-plugin-file-transformation)** plugin.

1. Install **File Transformation** from the same Catalog or from its [releases page](https://github.com/IAmParadox27/jellyfin-plugin-file-transformation/releases)
2. Restart Jellyfin

> **Without File Transformation**, the plugin still works server-side (enforcement, notifications, admin dashboard), but users won't see the header button, banner, or modal.

### Step 4 — Configure

Go to **Dashboard → Plugins → NoPayNoPlay** and set at least:
- **Monthly price** and **currency**
- **Grace days** and **trial days**
- **PayPal.me** and/or **Lydia** payment links
- **Contact email** (optional but recommended)

---

## Method 2: Manual ZIP installation

1. Download the latest release ZIP from the [releases page](https://github.com/alexisometric/nopaynoplay/releases)
2. Extract the contents
3. Copy the folder to Jellyfin's `plugins/` directory:

   | Platform | Typical path |
   |---|---|
   | **Linux** | `/var/lib/jellyfin/plugins/` |
   | **Windows** | `%ProgramData%\Jellyfin\Server\plugins\` |
   | **Docker** | `/jellyfin/plugins/` (bind mount) |
   | **macOS** | `~/.local/share/jellyfin/plugins/` |

4. Restart Jellyfin

---

## Method 3: Build from source

See [DEVELOPMENT.md](./DEVELOPMENT.md) for build instructions.

---

## Verifying the installation

1. Go to **Dashboard → Plugins** and check that **NoPayNoPlay** appears in the list
2. Open the plugin configuration page
3. The header status panel should show a green **"File Transformation OK"** badge (if File Transformation is installed)
4. Open Jellyfin in a regular (non-admin) browser tab — you should see the **💳** button in the header

### Test mode

You can preview the user UI without affecting real data:

1. Go to the **Test mode** tab in the admin dashboard
2. Select a state (e.g. "Expiring soon", "Blocked")
3. Click **Preview in a new tab** — this opens Jellyfin with `?npnpTest=STATE`

---

## Upgrading

1. Go to **Dashboard → Plugins → Catalog**
2. Click **Update** next to NoPayNoPlay (if available)
3. Restart Jellyfin

> Configuration is preserved between updates. Automatic backups (up to 10) are stored in `config/NoPayNoPlay.backups/`.

---

## Troubleshooting

| Symptom | Likely cause | Fix |
|---|---|---|
| User UI not showing | File Transformation plugin not installed | Install the companion plugin and restart |
| 💳 button appears only to admins | User not logged in test tab | Open an **incognito/private** tab and log in as a regular user |
| Banner shows but modal won't open | Browser console error | Check browser DevTools console for JS errors |
| "File Transformation not detected" warning | FT not yet registered | Click **Retry registration** on the Diagnostics tab |
| Plugin not in Catalog after adding repo | Repository URL incorrect | Verify the manifest URL and refresh repositories |
| Settings not saving | File permissions | Ensure Jellyfin can write to its `config/` directory |
