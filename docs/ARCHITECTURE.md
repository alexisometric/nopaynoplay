# 🏗️ Architecture Overview

## High-level flow

```mermaid
flowchart LR
    A[User signs in] -->|AuthenticationResult event| B(AuthenticationConsumer)
    B -->|calls| C{SubscriptionService}
    C -->|new user| D[7-day trial created]
    C -->|existing user| E[Return current state]
    
    F[Scheduled task<br/>every 12h] -->|EnforcementTask| G{SubscriptionService<br/>evaluate states}
    G -->|warning| H[Notification bell]
    G -->|grace expired| I[UserPolicyEnforcer]
    I -->|Snapshot + block| J[Playback disabled]
    
    K[Admin records<br/>payment] --> C
    L[User clicks<br/>"I just paid"] -->|POST /Me/MarkPaid| M[Pending claim]
    M -->|Admin confirms| C
    N[User redeems<br/>promo code] -->|POST /Me/RedeemCode| C
    
    C --> O[(Jellyfin XML config)]
    O -->|Auto-backup| P[config/backups/]
```

---

## Project Structure

```
src/
├── Plugin.cs                         # BasePlugin entry point
│   ├── Instance singleton             # Global access point
│   ├── SeedDefaultsAndDedupe()        # First-run defaults + fix duplicates
│   └── SaveConfiguration()            # Persist + auto-backup
│
├── PluginEntryPoint.cs               # IHostedService
│   ├── Retry loop (3s→180s back-off)  # FT registration retries
│   └── Diagnostics tracking           # Runtime diagnostics
│
├── AuthenticationConsumer.cs         # IEventConsumer<AuthenticationResultEventArgs>
│   └── Tracks users on sign-in        # Creates subscription if new
│
├── PluginServiceRegistrator.cs       # IPluginServiceRegistrator
│   └── DI registration                # Singleton services
│
├── Configuration/
│   ├── PluginConfiguration.cs        # XML-persisted settings
│   ├── UserSubscription.cs           # Per-user subscription data
│   ├── SubscriptionTier.cs           # Plan definition
│   ├── PromoCode.cs                  # Promo/referral code
│   ├── TransactionEntry.cs           # Payment record
│   ├── AuditLogEntry.cs             # Admin action log
│   └── UserTag.cs                   # Member group definition
│
├── Services/
│   ├── SubscriptionService.cs        # Core business logic
│   │   ├── MutateConfig()            # Thread-safe mutations
│   │   ├── ComputeNextExpiry()       # Date anchoring logic
│   │   ├── EvaluateState()           # State machine (Ok→Warning→Grace→Blocked)
│   │   └── RedeemPromoCode()         # Promo validation
│   │
│   ├── UserPolicyEnforcer.cs         # Playback block/restore
│   │   ├── BlockAsync()              # Save snapshot + disable playback
│   │   └── RestoreAsync()            # Restore from snapshot
│   │
│   └── RateLimiter.cs                # In-memory rate limiter
│       ├── CheckAsync()              # Per-key rate check
│       └── Failure tracking           # Brute-force protection
│
├── Api/
│   ├── NoPayNoPlayController.cs      # REST API controller
│   │   ├── Admin endpoints            # Requires elevation
│   │   └── User endpoints             # Authenticated users
│   │
│   └── ClientAssetsController.cs     # Static file serving
│       ├── /Web/client.js             # User UI script
│       └── /Web/qrcode.js             # QR code generator
│
├── ScheduledTasks/
│   └── EnforcementTask.cs            # IScheduledTask (12h interval)
│       ├── State evaluation           # Check all subscriptions
│       ├── Policy enforcement         # Block/restore users
│       ├── Notification dispatch      # Bell notifications with dedup
│       └── Orphan cleanup             # Remove stale subscriptions
│
├── Localization/
│   ├── Localizer.cs                  # i18n engine
│   │   ├── GetBundle()               # Culture-resolved translations
│   │   └── Merge()                   # Fallback chain
│   │
│   └── strings.{lang}.json           # 8 language bundles
│       ├── en, fr, es, de            # English, French, Spanish, German
│       └── it, pt, ru, zh            # Italian, Portuguese, Russian, Chinese
│
└── Web/
    ├── client.js                     # Injected user UI
    │   ├── My subscription entry   # 💳 in user menu (Modern) / header (Legacy)
    │   ├── Banner                     # Sticky bar — home tab only (JellyFlare-style route filter)
    │   ├── Modal                      # Subscription management
    │   ├── Toast system               # Feedback notifications
    │   └── Test mode                  # ?npnpTest=STATE, ?npnpTestRoute=any preview
    │
    ├── config.html                   # Admin dashboard
    │   └── config.js                 # Dashboard logic
    │
    ├── qrcode.js                     # Vendored QR generator
    │
    └── WebTransformer.cs             # File Transformation callback
        └── TransformAsync()           # Inject <script> into index.html
```

---

## Key Design Decisions

### No External Database

Everything is stored in Jellyfin's XML plugin configuration. This means:
- ✅ No SQLite, PostgreSQL, or other database setup
- ✅ Full compatibility with Jellyfin's backup/restore
- ✅ Configuration is human-readable XML
- ❌ Not suitable for thousands of users (XML performance degrades beyond ~500)

### Thread Safety

`SubscriptionService.MutateConfig()` uses a **static lock** to protect all config mutations:

```csharp
lock (_lock)
{
    if (mutator(Config))
    {
        _save();
    }
}
```

This prevents races between:
- The scheduled task (running every 12h)
- Admin API endpoints
- Self-service endpoints (`/Me/MarkPaid`, `/Me/RedeemCode`)

### Expiry Date Computation

`ComputeNextExpiry()` uses an **anchor-day preservation** strategy:

1. **Regular payments** extend from current expiry (or now if lapsed)
2. **Backfilled payments** (past dates) only extend if the payment period reaches beyond current expiry
3. **End-of-month** is handled correctly (Jan 31 + 1 month = Feb 28/29)

```mermaid
flowchart TD
    A[New payment] --> B{Is backfill?}
    B -->|Yes| C{Payment period<br/>> current expiry?}
    C -->|Yes| D[Extend from current expiry]
    C -->|No| E[No change - historical]
    B -->|No| F[Extend from current expiry or now]
    D --> G[Anchor day preserved]
    F --> G
```

### Subscription State Machine

```
Ok ──(warning window)──→ WarningSoon ──(expiry)──→ InGrace ──(grace expired)──→ Blocked
│                                                                                    │
└───────────────────── Payment received ─────────────────────────────────────────────┘
```

Plus:
- **Exempt** — orthogonal state, never blocked
- **Administrators** — always exempt automatically

### UserPolicy Enforcement

A block removes **seven** permissions, not just playback: media playback, audio and video transcoding, remuxing, content downloading, sync transcoding and media conversion. Dropping only the first four left downloads and offline sync open, which was the most valuable path for a lapsed member.

Before blocking, a **snapshot** records the current value of those seven permissions:

```json
{
  "enableMediaPlayback": true,
  "enableAudioPlaybackTranscoding": true,
  "enableVideoPlaybackTranscoding": true,
  "enablePlaybackRemuxing": true,
  "enableContentDownloading": true,
  "enableSyncTranscoding": true,
  "enableMediaConversion": true,
  "inferred": false
}
```

The snapshot is taken **once**, the first time the user is blocked; later passes reuse it instead of recording the already-cleared policy (which would otherwise turn a permanent block into a silent revocation of the account's permissions). If every managed permission is already `false` when the snapshot is taken, `inferred` is set: the pre-block state is unknown, so the restore re-enables everything rather than "restoring" `false` and leaving the member blocked forever.

On restore, **only flags matching the snapshot** are reverted — a permission the admin granted during the block is kept, and a permission the admin denied is not re-granted.

The state change is **persisted immediately** (`SubscriptionService.Save()`) from every caller — the authentication consumer, the admin endpoints and the scheduled task — so a block applied at sign-in survives a restart.

Active sessions are stopped immediately via `SendPlaystateCommand(Stop)` with a 5-second timeout per session, and only when the block becomes effective (a repeated pass does not interrupt anything).

### File Transformation (JS Injection)

The `WebTransformer` class registers a callback with the File Transformation plugin:

1. Looks for `index.html` in the transformable files
2. Only modifies payloads that look like HTML documents (`<!doctype html` or `<html` start) — avoids corrupting Webpack JS chunks
3. Injects `<script defer src="../NoPayNoPlay/Web/client.js?v=HASH">` before `</body>` — `index.html` is served from `<base>/web/`, so `../` resolves to the application root whether Jellyfin sits at `/` or under a base URL. `HASH` is a 12-hex **content hash** of the embedded `client.js` + `qrcode.js` (`WebAssetVersion`), not the assembly version, so even a hotfix that keeps the same version invalidates browser caches
4. Strips previously injected scripts (cache-busting on update)
5. Retries with back-off (3/8/20/45/90/180 s, then every 15 min for two hours) if the FT assembly isn't loaded yet — Jellyfin loads plugins in an arbitrary order and an admin may install File Transformation after this plugin. The `registered` flag is only kept when FT actually acknowledges the `index.html` pattern, so a failed registration cannot pin the plugin into "already registered" and block every later retry.

### Banner placement & theme compatibility

The subscription banner (states `WarningSoon` / `InGrace` / `Blocked`) is shown **only on the Jellyfin home tab**, using a JellyFlare-style route filter (`isHomePage()`, matched against `window.location.hash` — `#/home.html` / `#/home` / empty). It is re-evaluated on SPA navigation (`hashchange` / `popstate`) and `viewshow` so it disappears the moment the user leaves the home tab. Admins can preview it anywhere via `?npnpTestRoute=any` (gated on `isAdmin`).

Placement is theme/layout aware:
- The banner clears the live top bar — measured `.skinHeader`, the modern MUI AppBar (Jellyfin 12), or ElegantFin's `--appBarHeight` token — and slides to `top:0` when the header is scrolled out of view.
- When the **Media Bar** plugin owns the home tab (fullscreen slideshow, `#slides-container` / `.bar-loading`), the banner overlays the slideshow without pushing page content (the slideshow is absolutely positioned; the content-padding compensation is skipped).

### Anti-spam & Rate Limiting

| Mechanism | Scope | Details |
|---|---|---|
| Notification dedup | Per-user, per-milestone | `LastNotificationKey` = `state:milestone` |
| "I just paid" | Per-user, 30 min | In-memory timestamp, resets on restart |
| Promo brute-force | Per-IP, threshold-based | Lockout after N failed attempts |

### i18n System

Culture resolution chain:
1. Admin override (`UiCultureOverride` setting)
2. `?lang=` query parameter (the web client forwards Jellyfin's active UI language)
3. Jellyfin server UI culture
4. `Accept-Language` HTTP header
5. `en` fallback

Plural support: `key.one` / `key.other` suffixes. Token substitution via `{token}` placeholders.
