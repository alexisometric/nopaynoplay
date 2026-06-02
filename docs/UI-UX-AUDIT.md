# NoPayNoPlay — UI/UX Audit Report

**Date:** 2026-06-02  
**Scope:** Admin dashboard (`config.html` + `config.js`) and user-facing client (`client.js`)  
**Version:** v1.1.0 (from `client.js` header)

---

## Executive Summary

The NoPayNoPlay plugin delivers a **well-crafted, theme-adaptive, accessible** dual-interface (admin dashboard + user modal/banner). The codebase demonstrates strong attention to detail in theming, i18n, accessibility, and defensive coding. The audit identifies **0 critical issues**, **4 medium-severity findings**, and **8 low-severity recommendations** across both interfaces.

**Overall grade: B+ (Very Good)**

---

## 1. Architecture & Code Organization

### 1.1 Strengths

| Aspect | Assessment |
|--------|-----------|
| **Theme-adaptive CSS** | Uses `color-mix(in srgb, currentColor …)` to derive surfaces/borders from the inherited text color. This is an elegant approach that works across light, dark, and custom themes (e.g. ElegantFin) without per-theme overrides. |
| **CSS variable scoping** | Admin variables are scoped to `#NoPayNoPlayConfigPage`; client variables to `#npnp-banner, .npnp-modal-backdrop, .npnp-toast-stack`. No leakage into Jellyfin's global namespace. |
| **Semantic state colors** | `--npnp-ok`, `--npnp-warn`, `--npnp-danger` are hard-coded (not themed) — correct, since they convey meaning. |
| **Zero external dependencies** | QR code generator is vendored locally (`qrcode.js`). No CDN calls, no tracking. |
| **i18n architecture** | Strings fetched from the server with culture auto-detection. Plural forms supported (`{n} day` vs `{n} days`). 8 languages. |
| **Defensive injection** | `WebTransformer.cs` has a `LooksLikeHtmlDocument()` heuristic to avoid corrupting Webpack JS chunks that happen to contain `</body>` substrings. |

### 1.2 Concerns

- **M1 — Monolithic JS files**: [`config.js`](src/Web/config.js:1) is ~1645 lines and [`client.js`](src/Web/client.js:1) is ~1295 lines. Both mix DOM manipulation, API calls, state management, templating, and event handling in a single IIFE. This is manageable at current size but will become hard to maintain as features grow. *Consider splitting into modules (ESM or at least separate files concatenated at build time).*

---

## 2. Admin Dashboard (`config.html` + `config.js`)

### 2.1 Layout & Navigation

| Finding | Severity | Details |
|---------|----------|---------|
| **Tab-based navigation** | ✅ Good | 9 tabs with WAI-ARIA `role="tablist"` / `role="tab"` / `role="tabpanel"`. Keyboard navigation (arrow keys, Home/End) implemented. |
| **Tab persistence** | ✅ Good | Last-used tab saved in `sessionStorage` and restored on revisit. |
| **Filter persistence** | ✅ Good | Search/state/method filters persisted in `localStorage` across sessions. |
| **Tab count** | ⚠️ Low | 9 tabs is approaching the upper limit of comfortable horizontal tab navigation. On narrow screens (tablet), tabs wrap to multiple rows. Consider grouping: "Settings" could absorb "Tiers", "Tags", and "Promo codes" as sub-sections. |

### 2.2 Members Table

| Finding | Severity | Details |
|---------|----------|---------|
| **Sortable columns** | ✅ Good | Click headers to sort; arrow indicator shows direction. |
| **Multi-select with bulk bar** | ✅ Good | Checkboxes + bulk actions (pay, exempt, reset). Bar appears/disappears dynamically. |
| **Inline tag assignment** | ✅ Good | `<select>` per row for tag changes with instant API call. |
| **State pills** | ✅ Good | Color-coded badges (`Ok`=green, `Blocked`=red, etc.) with pending claim and arrears badges. |
| **Summary cards** | ✅ Good | Clickable stat cards at top filter the table by state. Active filter highlighted with border. |
| **Revenue chart** | ✅ Good | Inline SVG bar chart — no external charting library. Accessible with `role="img"` and `aria-label`. |
| **M2 — No pagination** | 🔶 Medium | The entire user list is rendered in one table. For servers with 100+ users, this will cause DOM bloat and slow rendering. *Add client-side pagination (e.g., 50 per page) or virtual scrolling.* |
| **M3 — No loading skeleton** | 🔷 Low | While data loads, the table area is blank until `Dashboard.showLoadingMsg()` (a Jellyfin global spinner) finishes. A skeleton placeholder in the table area would feel more responsive. |
| **L1 — CSV export uses `document.createElement('a')`** | 🔷 Low | Works but the download link briefly appears in the DOM. Consider using `URL.createObjectURL` + `revokeObjectURL` which is already done correctly. |

### 2.3 Modals (Payment, History, Confirm)

| Finding | Severity | Details |
|---------|----------|---------|
| **Focus trap** | ✅ Good | Tab cycles within modal; Escape closes; focus restored to opener on close. |
| **Backdrop click to close** | ✅ Good | Clicking outside the modal dismisses it. |
| **MutationObserver cleanup** | ✅ Good | Keydown listener and observer are cleaned up when modal is removed from DOM. |
| **M4 — `window.confirm()` for "I paid" in admin** | 🔷 Low | The admin "Reset trial" action uses a custom confirm modal, but the user-facing "I just paid" button in `client.js` uses native `confirm()`. This is inconsistent and the native dialog cannot be styled. *Already noted: this is in the client, not admin.* |
| **L2 — Payment modal date field** | 🔷 Low | The date `<input type="date">` has `max` set to today but no `min`. A `min` of e.g. 2020-01-01 would prevent absurd dates. |

### 2.4 Settings Form

| Finding | Severity | Details |
|---------|----------|---------|
| **Clean grid layout** | ✅ Good | `npnp-grid-2` uses `grid-template-columns: repeat(auto-fit, minmax(220px, 1fr))` for responsive form fields. |
| **Input validation** | ✅ Good | Server-side sanitization for URLs, email, currency code, culture code. |
| **L3 — No client-side validation feedback** | 🔷 Low | Invalid values (e.g., malformed PayPal URL) are silently rejected server-side. The field reverts to empty on next load with no explanation. *Add inline validation or at least a flash message when a field was sanitized to empty.* |
| **L4 — Save button position** | 🔷 Low | The "Save" button is at the bottom of the settings tab but there's no sticky/fixed save bar. On long forms (if sections expand), the user must scroll to save. Currently not an issue since the form is short. |

### 2.5 Activity Log

| Finding | Severity | Details |
|---------|----------|---------|
| **Date range filters** | ✅ Good | `From`/`To` date inputs with instant client-side filtering. |
| **Search across columns** | ✅ Good | Searches username, method, and admin note simultaneously. |
| **L5 — No sort on activity table** | 🔷 Low | Unlike the members table, activity columns are not sortable. Adding sort would help admins find specific transactions. |

### 2.6 Tiers & Tags Editors

| Finding | Severity | Details |
|---------|----------|---------|
| **Inline editing** | ✅ Good | Editable table rows with `emby-input` fields. |
| **Add/Delete rows** | ✅ Good | Client-side row manipulation; saved in batch via PUT. |
| **M5 — No "unsaved changes" warning** | 🔶 Medium | If the admin edits tiers/tags and switches tabs without saving, changes are lost silently. The `collectTiers()`/`collectTags()` functions read from the DOM, but the in-memory `state.tiers` is only updated on explicit save. *Add a `beforeunload` listener or intercept tab switches when the DOM differs from the last saved state.* |
| **L6 — Color input styling** | 🔷 Low | The `<input type="color">` in the tags table is well-styled, but there's no text fallback showing the hex value. Users on browsers that don't support `type="color"` get a plain text input with no indication of expected format. |

### 2.7 Diagnostics Tab

| Finding | Severity | Details |
|---------|----------|---------|
| **Clear status display** | ✅ Good | Green/red status badge with detailed assembly information. |
| **Copy JSON button** | ✅ Good | One-click copy of raw diagnostics. |
| **Retry mechanism** | ✅ Good | "Retry registration" with loading indicator. |

### 2.8 Test Mode

| Finding | Severity | Details |
|---------|----------|---------|
| **URL builder** | ✅ Good | Live-updating URL with copy and preview-in-new-tab buttons. |
| **L7 — Test mode help text** | 🔷 Low | The help paragraph is quite long. A collapsible "How does this work?" section would reduce visual clutter. |

---

## 3. User-Facing Client (`client.js`)

### 3.1 Banner

| Finding | Severity | Details |
|---------|----------|---------|
| **Positioning** | ✅ Good | Dynamically positioned below the Jellyfin header via `--npnp-header-h` CSS variable, recalculated on resize and DOM mutations. |
| **Animation** | ✅ Good | Slide-down entrance animation with `prefers-reduced-motion` respect. |
| **Dismissible** | ✅ Good | WarningSoon banner can be dismissed (persisted in `sessionStorage`). Critical states (InGrace, Blocked) cannot be dismissed. |
| **Responsive** | ✅ Good | Wraps on narrow screens (`@media max-width:600px`). |
| **ARIA roles** | ✅ Good | `role="alert"` for urgent states, `role="status"` for informational. |
| **Test mode badge** | ✅ Good | "Test mode" badge visible when `?npnpTest=…` is active. |

### 3.2 Modal (Subscription)

| Finding | Severity | Details |
|---------|----------|---------|
| **Hero card** | ✅ Good | Gradient background derived from state color. Icon + status pill + countdown + progress gauge. |
| **Tier cards** | ✅ Good | Selectable plan cards with savings percentage, "Best deal" badge, per-month breakdown. Highlight tier pre-selected. |
| **Payment cards** | ✅ Good | PayPal/Lydia cards with dynamic amount update when tier changes. |
| **QR codes** | ✅ Good | Inline SVG QR generation for payment URLs (PayPal/Lydia). |
| **Promo code redemption** | ✅ Good | Input + redeem button with Enter key support. Rate-limited with brute-force protection. |
| **"I just paid" flow** | ✅ Good | Self-service claim with cooldown (30 min). Pending state shown. Test mode simulation. |
| **Donation note** | ✅ Good | Heart icon + contextual message for exempt users vs. subscribers. |
| **History table** | ✅ Good | Collapsible (shows 5, "Show all" for more). |
| **Contact email** | ✅ Good | `mailto:` link with pre-filled subject. |
| **Copy custom note** | ✅ Good | Clipboard API with `execCommand` fallback. |
| **Focus trap** | ✅ Good | Same robust implementation as admin modals. |
| **Hash deep-link** | ✅ Good | `#!/npnp` or `#npnp` in URL auto-opens the modal. |

### 3.3 Header Button

| Finding | Severity | Details |
|---------|----------|---------|
| **Placement** | ✅ Good | Inserted before the search button in the Jellyfin header. Falls back to appending to `.headerRight`. |
| **Icon** | ✅ Good | `monetization_on` material icon with `aria-label`. |
| **MutationObserver** | ✅ Good | Re-inserts button if the DOM changes (SPA navigation). |

### 3.4 Toast Notifications

| Finding | Severity | Details |
|---------|----------|---------|
| **Stack** | ✅ Good | Positioned bottom-right, stacks multiple toasts. |
| **Types** | ✅ Good | Success (green left border), error (red), warning (orange). |
| **Dismiss** | ✅ Good | Auto-dismiss after 4.5s + manual close button. |
| **ARIA** | ✅ Good | `role="status"` with `aria-live="polite"`. |

---

## 4. Accessibility Audit

### 4.1 Keyboard Navigation

| Feature | Status |
|---------|--------|
| Tab navigation (arrow keys) | ✅ |
| Modal focus trap | ✅ |
| Escape to close modals | ✅ |
| Focus restoration on modal close | ✅ |
| `focus-visible` outlines on interactive elements | ✅ |
| Roving tabindex on tab buttons | ✅ |

### 4.2 Screen Reader Support

| Feature | Status |
|---------|--------|
| `aria-label` on icon-only buttons | ✅ |
| `role="dialog"` + `aria-modal="true"` on modals | ✅ |
| `role="tablist"` / `role="tab"` / `role="tabpanel"` | ✅ |
| `aria-selected` on tabs | ✅ |
| `aria-controls` / `aria-labelledby` on tab panels | ✅ |
| `role="img"` + `aria-label` on SVG chart | ✅ |
| `role="alert"` / `role="status"` on banner | ✅ |
| `aria-live="polite"` on flash messages and toasts | ✅ |
| **M6 — Table checkboxes lack accessible names** | 🔶 Medium | The "Select all" checkbox and row checkboxes use `<label class="emby-checkbox-label"><input is="emby-checkbox" type="checkbox" /><span></span></label>` with no text label. Screen readers will announce these as "checkbox" with no context. *Add `aria-label="Select all"` and `aria-label="Select {username}"`.* |

### 4.3 Motion & Preferences

| Feature | Status |
|---------|--------|
| `prefers-reduced-motion` respected | ✅ (both admin and client CSS) |
| Animations disabled when preferred | ✅ |

### 4.4 Color & Contrast

| Finding | Severity | Details |
|---------|----------|---------|
| **State colors** | ✅ Good | Green/orange/red are distinguishable and carry semantic meaning. |
| **Theme-adaptive surfaces** | ✅ Good | `color-mix` derivation ensures contrast in both light and dark themes. |
| **L8 — State pills text contrast** | 🔷 Low | The state pill uses `color: #fff` on colored backgrounds. On the `Exempt` state (`#95a5a6`), white text on light gray may fail WCAG AA contrast ratio (contrast ratio ~2.8:1, minimum is 4.5:1). *Consider darkening the Exempt color or using dark text on light backgrounds.* |

---

## 5. Responsive Design

| Breakpoint | Behavior |
|-----------|----------|
| **Admin tabs** | `flex-wrap: wrap` — tabs wrap to multiple rows on narrow screens. |
| **Admin stats** | `flex-wrap: wrap` — stat cards stack vertically. |
| **Admin forms** | `grid-template-columns: repeat(auto-fit, minmax(220px, 1fr))` — fields collapse to single column. |
| **Admin tables** | `overflow-x: auto` — horizontal scroll on narrow screens. No responsive table strategy (e.g., card layout on mobile). |
| **Client banner** | `flex-wrap: wrap` at 600px — message and actions stack. |
| **Client modal** | `width: min(600px, 94vw)` — never overflows viewport. |
| **Client tiers** | `grid-template-columns: repeat(auto-fit, minmax(140px, 1fr))` — responsive card grid. |
| **Client payment cards** | `grid-template-columns: repeat(auto-fit, minmax(180px, 1fr))` — responsive. |

**Assessment:** Good responsive foundations. The admin table horizontal scroll is acceptable for a dashboard, though a "card view" toggle for mobile would be a nice enhancement.

---

## 6. Performance

| Finding | Severity | Details |
|---------|----------|---------|
| **No external dependencies** | ✅ Good | QR code vendored; no CDN calls. |
| **SVG chart** | ✅ Good | Inline SVG, no charting library overhead. |
| **CSS in JS** | ⚠️ Note | `client.js` injects a `<style>` block at runtime (~200 lines of CSS). This is acceptable for a plugin that must not modify Jellyfin's build pipeline, but it means CSS is parsed on every page load rather than being cached. |
| **M7 — Full re-render on every filter change** | 🔶 Medium | `renderUsers()` rebuilds the entire table HTML on every keystroke in the search box. For large user lists, this causes jank. *Add a debounce (150-300ms) on the search input and consider DOM diffing or virtual scrolling.* |
| **Polling intervals** | ✅ Good | `client.js` polls every 5 minutes and ticks every 2 seconds for banner positioning. Reasonable. |

---

## 7. Error Handling & Edge Cases

| Finding | Assessment |
|---------|-----------|
| **API failures** | `.catch(function () { /* ignore */ })` is used in several places. While this prevents unhandled rejections, it silently swallows errors. The flash message system could be used to surface transient failures. |
| **Empty states** | Every table/list has an explicit empty state message (e.g., "No member to display.", "No activity yet."). |
| **Deleted users** | Handled gracefully — shows "(deleted)" for orphaned subscriptions. |
| **Admin preview** | Administrators see sample data in the user modal so they can preview the UI without having a real subscription. |
| **Test mode** | Comprehensive `?npnpTest=` system simulates all states, including "I paid" and promo redemption, without touching real data. |
| **Brute-force protection** | Promo code redemption has per-user (5), per-IP (20), and global (100) failure thresholds with 15-minute lockouts. |
| **Rate limiting** | "I just paid" button has 30-minute cooldown. |

---

## 8. i18n Coverage

| Language | File | Completeness |
|----------|------|-------------|
| English | `strings.en.json` | ✅ Complete (reference) |
| French | `strings.fr.json` | ✅ Complete |
| German | `strings.de.json` | ⚠️ Not audited |
| Spanish | `strings.es.json` | ⚠️ Not audited |
| Italian | `strings.it.json` | ⚠️ Not audited |
| Portuguese | `strings.pt.json` | ⚠️ Not audited |
| Russian | `strings.ru.json` | ⚠️ Not audited |
| Chinese | `strings.zh.json` | ⚠️ Not audited |

**Note:** The English and French files were audited and are complete. The other 6 languages should be verified for completeness against the English reference.

---

## 9. Security Observations

| Finding | Assessment |
|---------|-----------|
| **XSS prevention** | `escapeHtml()` used consistently for all user-generated content rendered in HTML. |
| **CSV injection guard** | `CsvEscape()` prefixes cells starting with `=+-@` with a single quote. |
| **Input sanitization** | Server-side: URL validation, email regex, currency code whitelist, culture code regex, promo code regex. |
| **Admin-only endpoints** | All mutation endpoints require `RequiresElevation` policy. |
| **No sensitive data leakage** | `MeTransactionDto` deliberately omits `AdminNote` from user-facing responses. |

---

## 10. Summary of Findings

### Critical (0)
None.

### Medium (4)

| ID | Description | Location |
|----|-------------|----------|
| **M1** | Monolithic JS files (~1645 + ~1295 lines) — hard to maintain as features grow | [`config.js`](src/Web/config.js:1), [`client.js`](src/Web/client.js:1) |
| **M2** | No pagination on members table — DOM bloat with 100+ users | [`config.js`](src/Web/config.js:502) `renderUsers()` |
| **M5** | No "unsaved changes" warning when leaving Tiers/Tags tabs | [`config.js`](src/Web/config.js:1176) `renderTiers()`, `renderTags()` |
| **M6** | Checkboxes lack accessible names for screen readers | [`config.js`](src/Web/config.js:518) select-all, row checkboxes |
| **M7** | Full table re-render on every search keystroke — no debounce | [`config.js`](src/Web/config.js:1422) search input handler |

### Low (8)

| ID | Description | Location |
|----|-------------|----------|
| **L1** | CSV download creates visible `<a>` element briefly | [`config.js`](src/Web/config.js:1469) `downloadCsv()` |
| **L2** | Payment date field has no `min` attribute | [`config.html`](src/Web/config.html:951) `#npnpPayDate` |
| **L3** | No client-side validation feedback on settings form | [`config.js`](src/Web/config.js:141) `saveSettings()` |
| **L4** | Save button not sticky — requires scroll on long forms | [`config.html`](src/Web/config.html:245) |
| **L5** | Activity table columns not sortable | [`config.js`](src/Web/config.js:700) `renderActivity()` |
| **L6** | Color input has no hex text fallback | [`config.js`](src/Web/config.js:1278) tag color input |
| **L7** | Test mode help text is verbose | [`config.html`](src/Web/config.html:415) |
| **L8** | Exempt state pill may fail WCAG AA contrast | [`config.js`](src/Web/config.js:32) `STATE_COLORS.Exempt = '#95a5a6'` |

---

## 11. Recommendations by Priority

### Immediate (next release)
1. **M6** — Add `aria-label` to checkboxes in the members table.
2. **M7** — Add 200ms debounce to the members search input.
3. **M5** — Add `beforeunload` listener when tiers/tags have unsaved DOM changes.

### Short-term (1-2 releases)
4. **M2** — Implement client-side pagination for the members table (50 per page).
5. **L8** — Darken the Exempt state color for better contrast (e.g., `#7f8c8d`).
6. **L3** — Add inline validation feedback on the settings form (highlight fields that were sanitized to empty).

### Long-term (nice to have)
7. **M1** — Split `config.js` and `client.js` into modules.
8. **L5** — Add column sorting to the activity table.
9. **L1** — Use a cleaner download approach for CSV exports.
10. **L7** — Add collapsible help sections for verbose descriptions.

---

## 12. What's Already Excellent

The plugin already does many things at a high level of quality that deserve recognition:

- **Theme system**: The `color-mix` approach to theme-adaptive surfaces is elegant and future-proof.
- **Accessibility foundation**: ARIA roles, keyboard navigation, focus management, and `prefers-reduced-motion` are all properly implemented.
- **Test mode**: The `?npnpTest=` system with session persistence is a standout feature for admin preview.
- **Defensive code**: The `LooksLikeHtmlDocument()` heuristic in `WebTransformer.cs` shows careful thinking about edge cases.
- **i18n**: Full plural support, culture auto-detection, and 8 languages is impressive for a plugin.
- **Zero dependencies**: No npm, no CDN, no tracking — everything is self-contained.
- **Security**: Proper XSS prevention, CSV injection guard, rate limiting with multi-tier brute-force protection.
