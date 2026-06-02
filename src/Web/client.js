/* NoPayNoPlay - client injected into jellyfin-web (v1.1.0). */
(function () {
    'use strict';

    if (window.__noPayNoPlayLoaded) {
        return;
    }
    window.__noPayNoPlayLoaded = true;

    var STATE_COLORS = {
        Ok: '#2ecc71',
        WarningSoon: '#f39c12',
        InGrace: '#e67e22',
        Blocked: '#e74c3c',
        Exempt: '#6c7a89'
    };

    var TEST_STATES = {
        ok: 'Ok',
        warningsoon: 'WarningSoon',
        ingrace: 'InGrace',
        blocked: 'Blocked',
        exempt: 'Exempt'
    };

    function getApiClient() {
        try {
            if (window.ApiClient) return window.ApiClient;
            if (window.connectionManager && window.connectionManager.currentApiClient) {
                return window.connectionManager.currentApiClient();
            }
        } catch (_) {}
        return null;
    }

    function lcfirstKey(k) {
        return k && k.length ? k.charAt(0).toLowerCase() + k.slice(1) : k;
    }
    function normalizeKeys(obj) {
        if (!obj || typeof obj !== 'object' || Array.isArray(obj)) return obj;
        var out = {};
        Object.keys(obj).forEach(function (k) { out[lcfirstKey(k)] = obj[k]; });
        return out;
    }
    function normalizeMe(raw) {
        var data = normalizeKeys(raw);
        if (data && Array.isArray(data.transactions)) {
            data.transactions = data.transactions.map(normalizeKeys);
        }
        return data;
    }

    function getTestStateOverride() {
        try {
            var qs = new URLSearchParams(window.location.search);
            var raw = qs.get('npnpTest');
            if (!raw) return null;
            var key = String(raw).toLowerCase();
            return TEST_STATES[key] || null;
        } catch (_) { return null; }
    }

    function fetchMe() {
        var api = getApiClient();
        if (!api) return Promise.reject(new Error('ApiClient unavailable'));
        return api.ajax({ type: 'GET', url: api.getUrl('NoPayNoPlay/Me'), dataType: 'json' });
    }

    function postJson(path, body) {
        var api = getApiClient();
        if (!api) return Promise.reject(new Error('ApiClient unavailable'));
        return api.ajax({
            type: 'POST',
            url: api.getUrl('NoPayNoPlay/' + path),
            contentType: 'application/json',
            data: JSON.stringify(body || {}),
            dataType: 'json'
        });
    }

    // Persist simulated test-mode actions across the auto-refresh that happens
    // every few minutes (and after simulated successes). Stored only for the
    // current tab/session and only honoured while ?npnpTest=… is in the URL.
    function readTestSession() {
        try {
            var raw = sessionStorage.getItem('npnpTestSession');
            return raw ? JSON.parse(raw) : {};
        } catch (_) { return {}; }
    }
    function writeTestSession(obj) {
        try { sessionStorage.setItem('npnpTestSession', JSON.stringify(obj || {})); } catch (_) {}
    }

    function applyTestOverride(data) {
        var forced = getTestStateOverride();
        if (!forced) return data;
        var clone = Object.assign({}, data);
        clone.state = forced;
        clone.__testMode = true;
        var now = new Date();
        if (forced === 'WarningSoon') {
            clone.daysLeft = 2;
            clone.expiryDate = new Date(now.getTime() + 2 * 86400000).toISOString();
        } else if (forced === 'InGrace') {
            clone.daysLeft = -1;
            clone.expiryDate = new Date(now.getTime() - 1 * 86400000).toISOString();
        } else if (forced === 'Blocked') {
            clone.daysLeft = -10;
            clone.expiryDate = new Date(now.getTime() - 10 * 86400000).toISOString();
        } else if (forced === 'Ok') {
            clone.daysLeft = 18;
            clone.expiryDate = new Date(now.getTime() + 18 * 86400000).toISOString();
        }

        // Provide sample payment URLs so the QR / payment cards render even
        // when the admin has not configured PayPal / Lydia yet.
        if (!clone.paypalMeUrl && !clone.lydiaUrl) {
            clone.paypalMeUrl = 'https://paypal.me/example/'
                + (Number(clone.price) || 5).toFixed(0)
                + (clone.currency || 'EUR');
            clone.lydiaUrl = 'https://lydia-app.com/pots/example';
        }
        if (!clone.price || Number(clone.price) <= 0) {
            clone.price = 5;
            clone.currency = clone.currency || 'EUR';
        }

        // Replay simulated actions persisted earlier in this tab.
        var sess = readTestSession();
        if (sess.pendingClaim) {
            clone.hasPendingPaymentClaim = true;
            clone.pendingPaymentClaimAt = sess.pendingClaim;
        }
        if (Array.isArray(sess.transactions) && sess.transactions.length) {
            clone.transactions = sess.transactions.concat(clone.transactions || []);
        }
        return clone;
    }

    function applyAdminPreviewSkin(data) {
        if (!data || !data.isAdminPreview) return data;
        // If a test override is already active, keep that state — we don't
        // want the admin-preview skin to clobber it.
        if (data.__testMode) {
            var c = Object.assign({}, data);
            c.__previewMode = true;
            return c;
        }
        var clone = Object.assign({}, data);
        clone.state = 'WarningSoon';
        clone.__previewMode = true;
        return clone;
    }

    function t(data, key, fallback) {
        if (data && data.strings && Object.prototype.hasOwnProperty.call(data.strings, key)) {
            return data.strings[key];
        }
        return fallback;
    }

    // Plural helper. Picks `${key}.one` for n===1, otherwise `${key}.other`.
    // Falls back to `${key}` when no plural form is registered. Substitutes
    // {n} with the count.
    function tp(data, key, n, fallback) {
        var suffix = (Number(n) === 1) ? '.one' : '.other';
        var v = t(data, key + suffix, null);
        if (v === null) v = t(data, key, fallback);
        return String(v).replace(/\{n\}/g, String(n));
    }

    function format(template, tokens) {
        return String(template).replace(/\{(\w+)\}/g, function (_, k) {
            return Object.prototype.hasOwnProperty.call(tokens, k) ? tokens[k] : '';
        });
    }

    function formatDate(iso, lang) {
        try {
            var d = new Date(iso);
            return new Intl.DateTimeFormat(lang || 'en', {
                day: '2-digit', month: '2-digit', year: 'numeric'
            }).format(d);
        } catch (_) { return iso; }
    }

    // Format a price WITHOUT trailing ".00" when the value is an integer.
    // e.g. 10 -> "10", 10.5 -> "10.50", 9.95 -> "9.95".
    function formatPrice(value) {
        var n = Number(value || 0);
        if (!isFinite(n)) return '0';
        if (Math.abs(n - Math.round(n)) < 0.005) {
            return String(Math.round(n));
        }
        return n.toFixed(2);
    }

    // Locale-aware currency formatting (e.g. "10 €" / "€10" / "10,50 €"), dropping
    // the cents on round amounts. Falls back to "<price> <CODE>" when the currency
    // code isn't a valid ISO 4217 symbol Intl can render.
    function formatMoney(value, currency, lang) {
        var n = Number(value || 0);
        if (!isFinite(n)) n = 0;
        var isInt = Math.abs(n - Math.round(n)) < 0.005;
        try {
            return new Intl.NumberFormat(lang || 'en', {
                style: 'currency',
                currency: String(currency || 'EUR').toUpperCase(),
                minimumFractionDigits: isInt ? 0 : 2,
                maximumFractionDigits: isInt ? 0 : 2
            }).format(n);
        } catch (_) {
            return formatPrice(n) + ' ' + (currency || 'EUR');
        }
    }

    // Clipboard copy with a graceful execCommand fallback + toast feedback.
    function copyToClipboard(text, okMsg, failMsg, dismissLabel) {
        var done = function () { toast(okMsg, 'success', dismissLabel); };
        var fail = function () { toast(failMsg, 'error', dismissLabel); };
        var fallback = function () {
            try {
                var ta = document.createElement('textarea');
                ta.value = text;
                ta.style.position = 'fixed';
                ta.style.opacity = '0';
                document.body.appendChild(ta);
                ta.select();
                document.execCommand('copy');
                document.body.removeChild(ta);
                done();
            } catch (_) { fail(); }
        };
        if (navigator.clipboard && navigator.clipboard.writeText) {
            navigator.clipboard.writeText(text).then(done).catch(fallback);
        } else {
            fallback();
        }
    }

    // Render a QR code as an inline SVG using the vendored generator (window.qrcode,
    // loaded lazily). Fixed black-on-white so it stays scannable on any theme.
    // Returns '' if the generator isn't available or encoding fails.
    function qrSvg(text, size) {
        if (!window.qrcode || !text) return '';
        try {
            var qr = window.qrcode(0, 'M');
            qr.addData(String(text));
            qr.make();
            var count = qr.getModuleCount();
            var cell = size / count;
            var rects = '';
            for (var r = 0; r < count; r++) {
                for (var c = 0; c < count; c++) {
                    if (qr.isDark(r, c)) {
                        rects += '<rect x="' + (c * cell).toFixed(2) + '" y="' + (r * cell).toFixed(2)
                            + '" width="' + cell.toFixed(2) + '" height="' + cell.toFixed(2) + '"/>';
                    }
                }
            }
            return '<svg class="npnp-qr" width="' + size + '" height="' + size + '" viewBox="0 0 ' + size + ' ' + size
                + '" xmlns="http://www.w3.org/2000/svg" aria-hidden="true">'
                + '<rect width="' + size + '" height="' + size + '" fill="#fff"/>'
                + '<g fill="#000">' + rects + '</g></svg>';
        } catch (_) { return ''; }
    }

    function ensureStyles() {
        if (document.getElementById('npnp-styles')) return;
        // Theme-adaptive variables. Strategy:
        //   1. Accent / foreground / background try a chain of well-known
        //      Jellyfin theme variables (--accent, --theme-primary-color,
        //      --theme-body-background-color, --background-color, ...) so
        //      themes such as ElegantFin or the default skins drive the
        //      plugin colours automatically.
        //   2. Surfaces, borders and input backgrounds are derived from
        //      currentColor via color-mix. This makes the plugin look
        //      correct on light AND dark themes without per-theme code:
        //      a slightly translucent layer of the text colour produces a
        //      subtle elevation in either direction.
        //   3. Semantic state colours (ok / warn / danger) and the banner
        //      gradient stay hard-coded on purpose: they carry meaning
        //      (expiring soon, blocked) and must not be themed away.
        // Variables are scoped to the plugin's own roots (#npnp-banner,
        // .npnp-modal-backdrop, .npnp-toast-stack) so they never leak into
        // the rest of Jellyfin.
        var themeVars = ''
            + '--npnp-accent:var(--accent,var(--theme-primary-color,#00a4dc));'
            + '--npnp-accent-fg:var(--theme-accent-text-color,#fff);'
            + '--npnp-bg:var(--theme-body-background-color,var(--background-color,#1a1a1a));'
            + '--npnp-fg:var(--theme-body-color,var(--theme-text-color,inherit));'
            + '--npnp-surface:color-mix(in srgb, currentColor 6%, transparent);'
            + '--npnp-surface-hover:color-mix(in srgb, currentColor 12%, transparent);'
            + '--npnp-border:color-mix(in srgb, currentColor 18%, transparent);'
            + '--npnp-border-strong:color-mix(in srgb, currentColor 28%, transparent);'
            + '--npnp-input-bg:color-mix(in srgb, currentColor 4%, transparent);'
            + '--npnp-ok:#2ecc71;'
            + '--npnp-warn:#f39c12;'
            + '--npnp-danger:#e74c3c;';
        var css = ''
            + '.npnp-modal-backdrop,#npnp-banner,.npnp-toast-stack{' + themeVars + '}'
            // Banner
            + '#npnp-banner{position:fixed;left:0;right:0;z-index:998;'
            + 'top:var(--npnp-header-h,0px);'
            + 'padding:12px 18px;font:600 14px/1.4 system-ui,-apple-system,sans-serif;color:#fff;'
            + 'display:flex;align-items:center;justify-content:space-between;gap:14px;'
            + 'box-shadow:0 4px 14px rgba(0,0,0,.35);}'
            + '#npnp-banner .npnp-banner-msg{flex:1;display:flex;flex-direction:column;gap:2px;}'
            + '#npnp-banner .npnp-banner-title{font-size:15px;font-weight:700;letter-spacing:.2px;}'
            + '#npnp-banner .npnp-banner-sub{font-size:12px;font-weight:500;opacity:.92;}'
            + '#npnp-banner .npnp-banner-actions{display:flex;align-items:center;gap:8px;flex-wrap:wrap;}'
            + '#npnp-banner button{background:#fff;color:#222;border:none;'
            + 'padding:8px 14px;border-radius:999px;cursor:pointer;font-weight:700;font-size:13px;'
            + 'box-shadow:0 2px 6px rgba(0,0,0,.18);transition:transform .12s,filter .12s;}'
            + '#npnp-banner button:hover{filter:brightness(1.1);transform:translateY(-1px);}'
            + '#npnp-banner .npnp-banner-dismiss{background:transparent;color:#fff;border:1px solid rgba(255,255,255,.6);'
            + 'padding:6px 10px;font-weight:500;}'
            + '#npnp-banner .npnp-banner-dismiss:hover{background:rgba(255,255,255,.15);}'
            + '@media (max-width:600px){#npnp-banner{font-size:13px;padding:10px 12px;flex-wrap:wrap;}'
            + '#npnp-banner .npnp-banner-msg{flex:1 1 100%;}}'
            + 'body.npnp-has-banner .mainAnimatedPages,body.npnp-has-banner .skinBody,'
            + 'body.npnp-has-banner .pageContainer{padding-top:var(--npnp-banner-pad,0px) !important;}'
            + '#npnp-banner .npnp-test-badge{background:rgba(0,0,0,.5);color:#fff;border-radius:4px;'
            + 'padding:2px 6px;font-size:11px;margin-right:8px;letter-spacing:.5px;}'
            // Header button
            + '.npnp-header-btn{background:transparent;border:none;color:inherit;cursor:pointer;'
            + 'padding:8px;display:inline-flex;align-items:center;justify-content:center;}'
            + '.npnp-header-btn .material-icons{font-size:24px;}'
            // Modal
            + '.npnp-modal-backdrop{position:fixed;inset:0;background:rgba(0,0,0,.65);'
            + 'z-index:10000;display:flex;align-items:center;justify-content:center;'
            + 'animation:npnpFade .15s ease-out;}'
            + '@keyframes npnpFade{from{opacity:0}to{opacity:1}}'
            + '@keyframes npnpSlide{from{transform:translateY(12px);opacity:0}to{transform:none;opacity:1}}'
            + '.npnp-modal{background:var(--npnp-bg);color:var(--npnp-fg);padding:0;border-radius:16px;'
            + 'width:min(600px,94vw);max-height:90vh;overflow:auto;'
            + 'font:14px/1.5 system-ui,-apple-system,sans-serif;'
            + 'border:1px solid var(--npnp-border);'
            + 'box-shadow:0 24px 64px rgba(0,0,0,.55);animation:npnpSlide .2s cubic-bezier(.2,.8,.2,1);}'
            + '.npnp-modal-header{padding:18px 22px 12px;display:flex;align-items:flex-start;justify-content:space-between;gap:12px;'
            + 'position:sticky;top:0;z-index:3;background:var(--npnp-bg);border-bottom:1px solid var(--npnp-border);}'
            + '.npnp-modal-header h2{margin:0;font-size:20px;font-weight:800;letter-spacing:-.2px;}'
            + '.npnp-modal-header .close{background:none;border:none;color:inherit;'
            + 'font-size:22px;line-height:1;cursor:pointer;opacity:.6;padding:6px 8px;border-radius:8px;'
            + 'transition:opacity .12s,background .12s;}'
            + '.npnp-modal-header .close:hover{opacity:1;background:var(--npnp-surface);}'
            + '.npnp-modal-body{padding:12px 22px 22px;}'
            // Hero card (status + countdown, or free-access for exempt users).
            + '.npnp-hero{display:flex;gap:14px;align-items:center;padding:16px;border-radius:14px;margin-bottom:16px;'
            + 'background:linear-gradient(135deg,color-mix(in srgb,var(--npnp-state) 22%,transparent),color-mix(in srgb,var(--npnp-state) 6%,transparent));'
            + 'border:1px solid color-mix(in srgb,var(--npnp-state) 42%,transparent);}'
            + '.npnp-hero-icon{width:46px;height:46px;border-radius:50%;flex-shrink:0;display:flex;align-items:center;justify-content:center;'
            + 'background:color-mix(in srgb,var(--npnp-state) 28%,transparent);}'
            + '.npnp-hero-icon .material-icons{font-size:26px;color:var(--npnp-state);}'
            + '.npnp-hero-body{display:flex;flex-direction:column;gap:3px;min-width:0;}'
            + '.npnp-status-pill{align-self:flex-start;display:inline-flex;align-items:center;gap:6px;'
            + 'font-size:11px;font-weight:700;text-transform:uppercase;letter-spacing:.6px;'
            + 'padding:3px 9px;border-radius:999px;background:color-mix(in srgb,var(--npnp-state) 32%,transparent);color:var(--npnp-fg);}'
            + '.npnp-hero-main{font-size:24px;font-weight:800;line-height:1.15;}'
            + '.npnp-hero-sub{font-size:13px;opacity:.85;}'
            + '.npnp-modal h3{margin:20px 0 9px;font-size:11px;text-transform:uppercase;'
            + 'letter-spacing:.7px;opacity:.7;font-weight:700;display:flex;align-items:center;gap:8px;}'
            + '.npnp-modal h3::before{content:"";width:16px;height:2px;border-radius:2px;background:var(--npnp-accent);opacity:.9;}'
            + '.npnp-modal .row{margin:6px 0;}'
            + '.npnp-pay-grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(180px,1fr));gap:10px;}'
            + '.npnp-pay-card{background:var(--npnp-surface);border:1px solid var(--npnp-border);border-radius:12px;padding:13px 14px;'
            + 'display:flex;align-items:center;gap:12px;text-decoration:none;color:inherit;'
            + 'transition:border-color .12s,transform .12s,background .12s,box-shadow .12s;}'
            + '.npnp-pay-card:hover{border-color:var(--npnp-accent);background:var(--npnp-surface-hover);transform:translateY(-1px);'
            + 'box-shadow:0 6px 18px color-mix(in srgb,var(--npnp-accent) 18%,transparent);}'
            + '.npnp-pay-icon{width:38px;height:38px;border-radius:10px;flex-shrink:0;display:flex;align-items:center;justify-content:center;'
            + 'background:color-mix(in srgb,var(--npnp-accent) 16%,transparent);}'
            + '.npnp-pay-icon .material-icons{font-size:20px;color:var(--npnp-accent);}'
            + '.npnp-pay-text{display:flex;flex-direction:column;gap:2px;flex:1;min-width:0;}'
            + '.npnp-pay-card .npnp-pay-title{font-weight:700;font-size:14px;}'
            + '.npnp-pay-card .npnp-pay-amount{font-size:12.5px;opacity:.8;}'
            + '.npnp-pay-card .npnp-pay-go{font-size:16px;opacity:.45;flex-shrink:0;}'
            // Hero time-remaining gauge.
            + '.npnp-hero-gauge{height:5px;border-radius:999px;margin-top:9px;overflow:hidden;'
            + 'background:color-mix(in srgb,var(--npnp-fg,currentColor) 14%,transparent);}'
            + '.npnp-hero-gauge>span{display:block;height:100%;border-radius:999px;}'
            // Tier savings chip.
            + '.npnp-tier-save{display:inline-block;margin-top:6px;font-size:11px;font-weight:700;'
            + 'padding:1px 7px;border-radius:999px;background:color-mix(in srgb,var(--npnp-ok) 22%,transparent);color:var(--npnp-ok);}'
            // Payment reference.
            + '.npnp-ref-row{display:flex;align-items:center;gap:10px;margin-top:12px;'
            + 'background:var(--npnp-surface);border:1px solid var(--npnp-border);border-radius:10px;padding:10px 12px;}'
            + '.npnp-ref-text{display:flex;flex-direction:column;gap:2px;flex:1;min-width:0;}'
            + '.npnp-ref-label{font-size:11px;text-transform:uppercase;letter-spacing:.5px;opacity:.6;font-weight:700;}'
            + '.npnp-ref-code{font:700 14px ui-monospace,monospace;letter-spacing:.5px;word-break:break-all;}'
            + '.npnp-ref-hint{font-size:12px;opacity:.7;margin-top:6px;}'
            // QR codes.
            + '.npnp-qr-grid{display:flex;flex-wrap:wrap;gap:14px;}'
            + '.npnp-qr-card{display:flex;flex-direction:column;align-items:center;gap:6px;}'
            + '.npnp-qr-card .npnp-qr{border-radius:8px;display:block;}'
            + '.npnp-qr-label{font-size:12px;font-weight:600;opacity:.85;}'
            // Collapsible history rows.
            + '.npnp-tx-hidden{display:none;}'
            + '.npnp-mini-btn{background:var(--npnp-surface);color:inherit;border:1px solid var(--npnp-border-strong);border-radius:6px;'
            + 'padding:6px 12px;cursor:pointer;font-size:13px;font-weight:600;}'
            + '.npnp-mini-btn:hover{border-color:var(--npnp-accent);background:var(--npnp-surface-hover);}'
            + '.npnp-mini-btn.primary{background:var(--npnp-accent);border-color:var(--npnp-accent);color:var(--npnp-accent-fg);}'
            + '.npnp-mini-btn.primary:hover{filter:brightness(1.1);background:var(--npnp-accent);}'
            + '.npnp-mini-btn.success{background:var(--npnp-ok);border-color:var(--npnp-ok);color:#fff;}'
            + '.npnp-mini-btn:disabled{opacity:.5;cursor:not-allowed;}'
            + '.npnp-pending-banner{background:color-mix(in srgb,var(--npnp-warn) 15%,transparent);border:1px solid var(--npnp-warn);'
            + 'color:inherit;padding:10px 12px;border-radius:8px;font-size:13px;margin:8px 0 0;'
            + 'display:flex;align-items:center;gap:8px;}'
            + '.npnp-history{width:100%;border-collapse:collapse;font-size:13px;}'
            + '.npnp-history th,.npnp-history td{padding:6px 8px;border-bottom:1px solid var(--npnp-border);text-align:left;}'
            + '.npnp-history th{opacity:.6;font-weight:600;font-size:11px;text-transform:uppercase;letter-spacing:.4px;}'
            + '.npnp-empty{opacity:.55;font-style:italic;padding:8px 0;font-size:13px;}'
            + '.npnp-promo-row{display:flex;gap:8px;align-items:center;}'
            + '.npnp-promo-row input{flex:1;background:var(--npnp-input-bg);border:1px solid var(--npnp-border);color:inherit;'
            + 'padding:8px 10px;border-radius:6px;font:600 13px ui-monospace,monospace;'
            + 'text-transform:uppercase;letter-spacing:1px;}'
            // Tier cards
            + '.npnp-tiers{display:grid;grid-template-columns:repeat(auto-fit,minmax(140px,1fr));gap:10px;margin:6px 0 4px;}'
            + '.npnp-tier{background:var(--npnp-surface);border:1px solid var(--npnp-border);border-radius:10px;padding:12px;text-align:center;'
            + 'cursor:pointer;transition:transform .12s,border-color .12s,background .12s;position:relative;color:inherit;}'
            + '.npnp-tier:hover{border-color:var(--npnp-accent);background:var(--npnp-surface-hover);transform:translateY(-1px);}'
            + '.npnp-tier .npnp-tier-months{font-size:13px;opacity:.75;text-transform:uppercase;letter-spacing:.5px;}'
            + '.npnp-tier .npnp-tier-price{font-size:22px;font-weight:800;margin:4px 0 2px;}'
            + '.npnp-tier .npnp-tier-permonth{font-size:11px;opacity:.6;}'
            + '.npnp-tier .npnp-tier-label{font-size:12px;opacity:.85;margin-top:6px;}'
            + '.npnp-tier.highlight{background:linear-gradient(135deg,color-mix(in srgb,var(--npnp-accent) 15%,transparent),var(--npnp-surface));'
            + 'border-color:var(--npnp-accent);box-shadow:0 0 0 1px var(--npnp-accent) inset,0 6px 18px color-mix(in srgb,var(--npnp-accent) 20%,transparent);}'
            + '.npnp-tier.highlight .npnp-tier-badge{position:absolute;top:-9px;right:8px;background:var(--npnp-accent);color:var(--npnp-accent-fg);'
            + 'padding:2px 8px;border-radius:999px;font-size:10px;font-weight:700;text-transform:uppercase;letter-spacing:.5px;}'
            + '.npnp-tier.selected{outline:2px solid var(--npnp-accent);}'
            + '.npnp-note-row{display:flex;align-items:flex-start;gap:8px;margin-top:12px;font-size:13px;opacity:.85;}'
            + '.npnp-note-row .npnp-note-text{flex:1;white-space:pre-wrap;word-break:break-word;}'
            + '.npnp-contact-row{margin-top:10px;font-size:13px;}'
            + '.npnp-contact-row a{color:var(--npnp-accent);text-decoration:none;}'
            + '.npnp-contact-row a:hover{text-decoration:underline;}'
            // Donation note
            + '.npnp-donate-note{display:flex;align-items:center;gap:8px;'
            + 'background:color-mix(in srgb,var(--npnp-danger) 10%,transparent);border:1px solid color-mix(in srgb,var(--npnp-danger) 35%,transparent);'
            + 'border-radius:8px;padding:10px 12px;margin-top:10px;font-size:13px;line-height:1.4;}'
            + '.npnp-donate-note .material-icons{color:var(--npnp-danger);font-size:18px;flex-shrink:0;}'
            // Toast (kept on a dark surface on purpose so it stays
            // readable against any background, including the player UI).
            + '.npnp-toast-stack{position:fixed;right:18px;bottom:18px;z-index:10001;'
            + 'display:flex;flex-direction:column-reverse;gap:8px;pointer-events:none;}'
            + '.npnp-toast{pointer-events:auto;background:var(--npnp-bg);color:var(--npnp-fg);'
            + 'padding:12px 16px;border-radius:8px;box-shadow:0 6px 20px rgba(0,0,0,.5);'
            + 'min-width:240px;max-width:360px;display:flex;align-items:center;gap:10px;'
            + 'font:14px/1.4 system-ui,sans-serif;border:1px solid var(--npnp-border);border-left:4px solid var(--npnp-accent);'
            + 'animation:npnpSlide .18s ease-out;}'
            + '.npnp-toast.success{border-left-color:var(--npnp-ok);}'
            + '.npnp-toast.error{border-left-color:var(--npnp-danger);}'
            + '.npnp-toast.warn{border-left-color:var(--npnp-warn);}'
            + '.npnp-toast .npnp-toast-close{background:none;border:none;color:inherit;opacity:.6;'
            + 'cursor:pointer;font-size:16px;padding:0;line-height:1;}'
            + '.npnp-toast .npnp-toast-close:hover{opacity:1;}'
            // Banner icon + slide-down entrance.
            + '#npnp-banner .npnp-banner-icon{font-size:22px;flex-shrink:0;line-height:1;}'
            + '#npnp-banner{animation:npnpBannerIn .22s ease-out;}'
            + '@keyframes npnpBannerIn{from{transform:translateY(-100%);opacity:0}to{transform:none;opacity:1}}'
            // Visible focus ring for keyboard users on every interactive element.
            + '#npnp-banner button:focus-visible,.npnp-mini-btn:focus-visible,.npnp-tier:focus-visible,'
            + '.npnp-pay-card:focus-visible,.npnp-modal-header .close:focus-visible,'
            + '.npnp-promo-row input:focus-visible{outline:2px solid var(--npnp-accent);outline-offset:2px;}'
            + '.npnp-modal:focus{outline:none;}'
            // Respect the OS "reduce motion" setting.
            + '@media (prefers-reduced-motion:reduce){'
            + '.npnp-modal-backdrop,.npnp-modal,.npnp-toast,#npnp-banner{animation:none !important;}'
            + '.npnp-pay-card,.npnp-tier,#npnp-banner button,.npnp-mini-btn{transition:none !important;}'
            + '.npnp-pay-card:hover,.npnp-tier:hover,#npnp-banner button:hover{transform:none !important;}}';
        var s = document.createElement('style');
        s.id = 'npnp-styles';
        s.textContent = css;
        document.head.appendChild(s);
    }

    function escapeHtml(s) {
        return String(s == null ? '' : s).replace(/[&<>"']/g, function (c) {
            return ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', '\'': '&#39;' })[c];
        });
    }

    function lcfirst(s) {
        return s ? s.charAt(0).toLowerCase() + s.slice(1) : s;
    }

    // ----- Toast -----
    function ensureToastStack() {
        ensureStyles();
        var stack = document.getElementById('npnp-toast-stack');
        if (!stack) {
            stack = document.createElement('div');
            stack.id = 'npnp-toast-stack';
            stack.className = 'npnp-toast-stack';
            stack.setAttribute('role', 'status');
            stack.setAttribute('aria-live', 'polite');
            document.body.appendChild(stack);
        }
        return stack;
    }

    function toast(message, kind, dismissLabel) {
        var stack = ensureToastStack();
        var el = document.createElement('div');
        el.className = 'npnp-toast ' + (kind || '');
        el.innerHTML = '<span style="flex:1;">' + escapeHtml(message) + '</span>'
            + '<button type="button" class="npnp-toast-close" aria-label="'
            + escapeHtml(dismissLabel || 'Dismiss') + '">&times;</button>';
        var close = function () { if (el.parentNode) el.parentNode.removeChild(el); };
        el.querySelector('.npnp-toast-close').addEventListener('click', close);
        stack.appendChild(el);
        setTimeout(close, 4500);
    }

    function closeModal() {
        var m = document.getElementById('npnp-modal-backdrop');
        if (m) m.parentNode.removeChild(m);
    }

    // ----- Modal -----
    function renderHistory(data) {
        var tx = data.transactions || [];
        if (!tx.length) {
            return '<div class="npnp-empty">'
                + escapeHtml(t(data, 'user.modal.history.empty', 'No payment recorded yet.'))
                + '</div>';
        }
        var rows = tx.map(function (e, i) {
            return '<tr' + (i >= 5 ? ' class="npnp-tx-hidden"' : '') + '>'
                + '<td>' + escapeHtml(formatDate(e.date, data.lang)) + '</td>'
                + '<td>' + escapeHtml(formatMoney(e.amount, data.currency, data.lang)) + '</td>'
                + '<td>' + escapeHtml(e.method || '') + '</td>'
                + '<td>' + escapeHtml(String(e.monthsAdded || 0)) + '</td>'
                + '</tr>';
        }).join('');
        var table = '<table class="npnp-history">'
            + '<thead><tr>'
            + '<th>' + escapeHtml(t(data, 'user.modal.history.date', 'Date')) + '</th>'
            + '<th>' + escapeHtml(t(data, 'user.modal.history.amount', 'Amount')) + '</th>'
            + '<th>' + escapeHtml(t(data, 'user.modal.history.method', 'Method')) + '</th>'
            + '<th>' + escapeHtml(t(data, 'user.modal.history.months', 'Months')) + '</th>'
            + '</tr></thead><tbody>' + rows + '</tbody></table>';
        var more = tx.length > 5
            ? '<button type="button" class="npnp-mini-btn" id="npnp-history-more" style="margin-top:10px;">'
                + escapeHtml(format(t(data, 'user.modal.history.showAll', 'Show all ({n})'), { n: tx.length })) + '</button>'
            : '';
        return table + more;
    }

    function renderHero(data) {
        // Exempt users get a positive (green) hero with NO expiry date; everyone
        // else gets their state colour, a countdown and the due date.
        var color = data.state === 'Exempt' ? '#2ecc71' : (STATE_COLORS[data.state] || '#888');
        var stateLabel = t(data, 'state.' + lcfirst(data.state), data.state);
        var icons = {
            Ok: 'check_circle', WarningSoon: 'schedule', InGrace: 'warning',
            Blocked: 'block', Exempt: 'verified'
        };
        var icon = icons[data.state] || 'info';

        // A user with no recorded payment who is still Ok/WarningSoon is on their
        // free trial — surface that with a friendly pill + gift icon.
        var isTrial = data.state !== 'Exempt'
            && (data.state === 'Ok' || data.state === 'WarningSoon')
            && (!data.transactions || data.transactions.length === 0);
        var pillLabel = isTrial ? t(data, 'user.modal.trial.pill', 'Free trial') : stateLabel;
        if (isTrial) icon = 'card_giftcard';

        var dueOn = format(t(data, 'user.modal.summary.dueOn', 'Due on {date}'), {
            date: formatDate(data.expiryDate, data.lang)
        });

        var main = '';
        var sub = '';
        if (data.state === 'Exempt') {
            main = t(data, 'user.modal.exempt.title', 'Free access for you');
            sub = t(data, 'user.modal.exempt.sub', 'No subscription is required — enjoy the server.');
        } else if (data.state === 'Ok' || data.state === 'WarningSoon') {
            var dl = Math.max(0, Number(data.daysLeft || 0));
            main = tp(data, 'user.modal.summary.daysLeft', dl,
                format(t(data, 'user.modal.summary.daysLeft', '{days} day(s) left'), { days: dl }));
            sub = dueOn;
        } else if (data.state === 'InGrace' || data.state === 'Blocked') {
            var ov = Math.max(0, -Number(data.daysLeft || 0));
            main = tp(data, 'user.modal.summary.expiredAgo', ov,
                format(t(data, 'user.modal.summary.expiredAgo', 'Expired {days} day(s) ago'), { days: ov }));
            sub = dueOn;
        } else {
            main = stateLabel;
            sub = dueOn;
        }

        // Time-remaining gauge (skipped for exempt users, who have no countdown).
        var gauge = '';
        if (data.state !== 'Exempt') {
            var dl = Number(data.daysLeft || 0);
            var frac;
            if (data.state === 'Ok' || data.state === 'WarningSoon') {
                frac = Math.max(0, Math.min(1, dl / 30));
            } else if (data.state === 'InGrace') {
                var grace = Math.max(1, Number(data.graceDays || 1));
                frac = Math.max(0, Math.min(1, (grace - Math.max(0, -dl)) / grace));
            } else {
                frac = 0;
            }
            gauge = '<div class="npnp-hero-gauge"><span style="width:' + Math.round(frac * 100)
                + '%;background:' + color + ';"></span></div>';
        }

        return '<div class="npnp-hero" style="--npnp-state:' + color + ';">'
            + '<div class="npnp-hero-icon"><span class="material-icons" aria-hidden="true">' + icon + '</span></div>'
            + '<div class="npnp-hero-body">'
            + '<span class="npnp-status-pill">' + escapeHtml(pillLabel) + '</span>'
            + '<div class="npnp-hero-main">' + escapeHtml(main) + '</div>'
            + '<div class="npnp-hero-sub">' + escapeHtml(sub) + '</div>'
            + gauge
            + '</div></div>';
    }

    function renderTiers(data) {
        var tiers = Array.isArray(data.tiers) ? data.tiers : [];
        if (!tiers.length) return '';
        var currency = data.currency || 'EUR';
        var badgeLabel = t(data, 'user.modal.tier.popular', 'Best deal');
        var perMonth = t(data, 'user.modal.tier.perMonth', '{price} / month');
        var monthsLabel = t(data, 'user.modal.tier.months', '{n} month(s)');
        var saveLabel = t(data, 'user.modal.tier.save', 'Save {percent}%');
        var monthly = Number(data.price || 0);
        var html = tiers.map(function (raw) {
            var t1 = normalizeKeys(raw);
            var months = Math.max(1, Number(t1.months || 1));
            var price = Number(t1.price || 0);
            var pm = months > 0 ? (price / months) : 0;
            // Savings vs the plain monthly price (only when there's a real discount).
            var saveChip = '';
            if (monthly > 0 && pm > 0 && pm < monthly) {
                var pct = Math.round((1 - pm / monthly) * 100);
                if (pct > 0) {
                    saveChip = '<div class="npnp-tier-save">' + escapeHtml(format(saveLabel, { percent: pct })) + '</div>';
                }
            }
            return '<button type="button" class="npnp-tier' + (t1.highlight ? ' highlight' : '')
                + '" data-tier-id="' + escapeHtml(t1.id || '') + '"'
                + ' data-tier-months="' + months + '"'
                + ' data-tier-price="' + price + '">'
                + (t1.highlight ? '<span class="npnp-tier-badge">' + escapeHtml(badgeLabel) + '</span>' : '')
                + '<div class="npnp-tier-months">' + escapeHtml(format(monthsLabel, { n: months })) + '</div>'
                + '<div class="npnp-tier-price">' + escapeHtml(formatMoney(price, currency, data.lang)) + '</div>'
                + '<div class="npnp-tier-permonth">' + escapeHtml(format(perMonth, { price: formatMoney(pm, currency, data.lang) })) + '</div>'
                + saveChip
                + (t1.label ? '<div class="npnp-tier-label">' + escapeHtml(t1.label) + '</div>' : '')
                + '</button>';
        }).join('');
        return '<h3>' + escapeHtml(t(data, 'user.modal.section.tiers', 'Choose a plan'))
            + '</h3><div class="npnp-tiers">' + html + '</div>';
    }

    function paymentCardHtml(data, key, url, methodLabel, isExempt) {
        if (!url) return '';
        // Exempt users never get a prefilled amount: the card is a pure donation
        // invite ("Choose your amount", no price, no amount appended to the URL).
        var priceForUrl = isExempt ? 0 : Number(data.price || 0);
        var amount = isExempt
            ? t(data, 'user.modal.donate.freeAmount', 'Choose your amount')
            : formatPrice(data.price) + ' ' + (data.currency || 'EUR');
        var icon = isExempt ? 'volunteer_activism' : (key === 'paypal' ? 'account_balance_wallet' : 'smartphone');
        return '<a class="npnp-pay-card" href="' + escapeHtml(buildPaymentUrl(key, url, priceForUrl, data.currency || 'EUR'))
            + '" target="_blank" rel="noopener" data-method="' + escapeHtml(key) + '"'
            + ' data-base-url="' + escapeHtml(url) + '">'
            + '<span class="npnp-pay-icon"><span class="material-icons" aria-hidden="true">' + icon + '</span></span>'
            + '<span class="npnp-pay-text">'
            + '<span class="npnp-pay-title">' + escapeHtml(methodLabel) + '</span>'
            + '<span class="npnp-pay-amount">' + escapeHtml(amount) + '</span>'
            + '</span>'
            + '<span class="material-icons npnp-pay-go" aria-hidden="true">open_in_new</span>'
            + '</a>';
    }

    // Build a payment URL pre-filled with the chosen amount.
    //  - PayPal.me: replace or append /<amount><CURRENCY> at the end of the
    //    handle path, e.g. https://paypal.me/jdoe/12EUR.
    //  - Lydia: append amount=<value> as a query parameter (used as a hint;
    //    Lydia's pot pages don't always honour it but adding it is harmless).
    function buildPaymentUrl(method, baseUrl, amount, currency) {
        if (!baseUrl) return '';
        var amt = Math.max(0, Number(amount || 0));
        if (amt <= 0) return baseUrl;
        var cur = String(currency || 'EUR').toUpperCase().replace(/[^A-Z]/g, '').slice(0, 3) || 'EUR';
        var nice = (Math.round(amt * 100) / 100).toString();
        try {
            if (method === 'paypal') {
                var m = baseUrl.match(/^(https?:\/\/(?:www\.)?paypal\.me\/[^\/?#\s]+)(?:\/[^?#\s]*)?(\?[^#]*)?(#.*)?$/i);
                if (m) {
                    return m[1] + '/' + nice + cur + (m[2] || '') + (m[3] || '');
                }
            }
            if (method === 'lydia') {
                var sep = baseUrl.indexOf('?') === -1 ? '?' : '&';
                if (/[?&]amount=/i.test(baseUrl)) {
                    return baseUrl.replace(/([?&]amount=)[^&#]*/i, '$1' + encodeURIComponent(nice));
                }
                return baseUrl + sep + 'amount=' + encodeURIComponent(nice);
            }
        } catch (_) { /* fall through */ }
        return baseUrl;
    }

    function openModal(rawData) {
        ensureStyles();
        closeModal();
        var data = applyAdminPreviewSkin(rawData);
        var isExempt = data.state === 'Exempt';

        // Exempt users see donation wording ("Donate via PayPal") instead of "Pay with…".
        var donateWith = t(data, 'user.modal.method.donateWith', 'Donate via {method}');
        var paypalLabel = isExempt
            ? format(donateWith, { method: 'PayPal' })
            : t(data, 'user.modal.method.paypal', 'Pay with PayPal');
        var lydiaLabel = isExempt
            ? format(donateWith, { method: 'Lydia' })
            : t(data, 'user.modal.method.lydia', 'Pay with Lydia');
        var cards = [
            paymentCardHtml(data, 'paypal', data.paypalMeUrl, paypalLabel, isExempt),
            paymentCardHtml(data, 'lydia', data.lydiaUrl, lydiaLabel, isExempt)
        ].filter(Boolean).join('');
        var paySectionHeader = isExempt
            ? t(data, 'user.modal.section.donate', 'Support the server')
            : t(data, 'user.modal.section.payment', 'How to pay');
        var paySection = cards
            ? '<h3>' + escapeHtml(paySectionHeader) + '</h3>'
                + '<div class="npnp-pay-grid">' + cards + '</div>'
            : '';

        // Donation note: shown to every user with at least one configured payment
        // URL, encouraging voluntary contributions to keep the server running.
        var donateNote = '';
        if (cards) {
            var donateKey = isExempt
                ? 'user.modal.donate.exempt'
                : 'user.modal.donate.note';
            var donateFallback = isExempt
                ? 'Your access is free — if you want to help cover server costs and maintenance, any donation is welcome.'
                : 'Beyond your subscription, donations are welcome to help cover server costs and continued improvements.';
            donateNote = '<div class="npnp-donate-note">'
                + '<span class="material-icons" aria-hidden="true">favorite</span> '
                + escapeHtml(t(data, donateKey, donateFallback))
                + '</div>';
        }

        var note = data.customNote
            ? '<div class="npnp-note-row">'
                + '<span class="npnp-note-text">' + escapeHtml(data.customNote) + '</span>'
                + '<button type="button" class="npnp-mini-btn" id="npnp-copy-note" title="'
                + escapeHtml(t(data, 'user.modal.copyNote', 'Copy')) + '">'
                + '<span class="material-icons" aria-hidden="true" style="font-size:14px;vertical-align:middle;">content_copy</span> '
                + escapeHtml(t(data, 'user.modal.copyNote', 'Copy'))
                + '</button>'
                + '</div>'
            : '';

        var contactRow = '';
        if (data.contactEmail) {
            var subj = encodeURIComponent('[NoPayNoPlay] ' + (data.state || ''));
            contactRow = '<div class="npnp-contact-row">'
                + escapeHtml(t(data, 'user.modal.contact', 'Need help?')) + ' '
                + '<a href="mailto:' + escapeHtml(data.contactEmail) + '?subject=' + subj + '">'
                + escapeHtml(data.contactEmail) + '</a></div>';
        }

        var pendingBlock = '';
        if (data.hasPendingPaymentClaim && data.pendingPaymentClaimAt) {
            pendingBlock = '<div class="npnp-pending-banner">'
                + '<span class="material-icons" aria-hidden="true">hourglass_top</span>'
                + escapeHtml(format(t(data, 'user.modal.markPaid.pending',
                    'Pending admin confirmation since {date}.'),
                    { date: formatDate(data.pendingPaymentClaimAt, data.lang) }))
                + '</div>';
        }

        var iPaidBtn = '';
        if (!isExempt) {
            iPaidBtn = '<button type="button" class="npnp-mini-btn primary" id="npnp-i-paid"'
                + (data.hasPendingPaymentClaim ? ' disabled' : '')
                + '>'
                + '<span class="material-icons" aria-hidden="true" style="vertical-align:middle;font-size:16px;">check_circle</span> '
                + escapeHtml(t(data, 'user.modal.markPaid', 'I just paid'))
                + '</button>';
        }

        var promoSection = isExempt ? '' : ''
            + '<h3>' + escapeHtml(t(data, 'user.modal.section.promo', 'Promo / referral code')) + '</h3>'
            + '<div class="npnp-promo-row">'
            + '<input type="text" id="npnp-promo-input" maxlength="32" placeholder="'
            + escapeHtml(t(data, 'user.modal.promo.placeholder', 'Enter a code…')) + '" />'
            + '<button type="button" class="npnp-mini-btn" id="npnp-promo-redeem">'
            + escapeHtml(t(data, 'user.modal.promo.redeem', 'Redeem')) + '</button>'
            + '</div>';

        var modalTitle = t(data, 'user.modal.title', 'My subscription');
        var historyTitle = t(data, 'user.modal.section.history', 'Recent payments');
        var previewBanner = data.__previewMode
            ? '<div class="row" style="background:rgba(0,164,220,.18);border:1px solid #00a4dc;'
                + 'padding:8px 12px;border-radius:6px;margin:0 0 12px;font-size:13px;">'
                + escapeHtml(t(data, 'user.modal.previewBadge', 'Admin preview — sample data'))
                + '</div>'
            : '';

        var html = ''
            + '<div class="npnp-modal-backdrop" id="npnp-modal-backdrop">'
            + '  <div class="npnp-modal" role="dialog" aria-modal="true" tabindex="-1" aria-label="' + escapeHtml(modalTitle) + '">'
            + '    <div class="npnp-modal-header">'
            + '      <h2>' + escapeHtml(modalTitle) + '</h2>'
            + '      <button class="close" type="button" aria-label="'
            +          escapeHtml(t(data, 'user.modal.close', 'Close')) + '">&times;</button>'
            + '    </div>'
            + '    <div class="npnp-modal-body">'
            +        previewBanner
            +        renderHero(data)
            +        pendingBlock
            +        (isExempt ? '' : renderTiers(data))
            +        paySection
            +        donateNote
            +        (iPaidBtn ? '<div style="margin-top:14px;">' + iPaidBtn + '</div>' : '')
            +        note
            +        contactRow
            +        promoSection
            +        (isExempt ? '' : ('<h3>' + escapeHtml(historyTitle) + '</h3>' + renderHistory(data)))
            + '    </div>'
            + '  </div>'
            + '</div>';

        var wrap = document.createElement('div');
        wrap.innerHTML = html;
        document.body.appendChild(wrap.firstElementChild);

        var backdrop = document.getElementById('npnp-modal-backdrop');
        backdrop.addEventListener('click', function (e) {
            if (e.target === backdrop) closeModal();
        });
        backdrop.querySelector('.close').addEventListener('click', closeModal);

        // --- Accessibility: focus trap, Escape to close, restore focus on close ---
        var modalEl = backdrop.querySelector('.npnp-modal');
        var opener = document.activeElement;
        function focusables() {
            return Array.prototype.slice.call(modalEl.querySelectorAll(
                'a[href],button:not([disabled]),input:not([disabled]),select:not([disabled]),'
                + 'textarea:not([disabled]),[tabindex]:not([tabindex="-1"])'))
                .filter(function (el) { return el.offsetParent !== null || el === document.activeElement; });
        }
        function onKey(e) {
            if (!document.body.contains(modalEl)) { document.removeEventListener('keydown', onKey); return; }
            if (e.key === 'Escape') {
                e.preventDefault();
                closeModal();
            } else if (e.key === 'Tab') {
                var f = focusables();
                if (!f.length) return;
                var first = f[0], last = f[f.length - 1];
                if (e.shiftKey && document.activeElement === first) { e.preventDefault(); last.focus(); }
                else if (!e.shiftKey && document.activeElement === last) { e.preventDefault(); first.focus(); }
            }
        }
        document.addEventListener('keydown', onKey);
        // Restore focus to the opener once the modal is removed (any close path).
        try {
            var mo = new MutationObserver(function () {
                if (!document.body.contains(modalEl)) {
                    mo.disconnect();
                    document.removeEventListener('keydown', onKey);
                    if (opener && typeof opener.focus === 'function') {
                        try { opener.focus(); } catch (_) {}
                    }
                }
            });
            mo.observe(document.body, { childList: true });
        } catch (_) {}
        // Move focus into the dialog so screen readers announce its label.
        try { modalEl.focus(); } catch (_) {}

        // Tier selection: pick the highlight tier by default and let the user
        // choose another. The selection is reflected when claiming "I paid".
        var selectedTierMonths = 1;
        var selectedTierAmount = Number(data.price || 0);
        var defaultTier = (data.tiers || []).find(function (t1) {
            var n = normalizeKeys(t1);
            return n.highlight;
        }) || (data.tiers || [])[0];
        if (defaultTier) {
            var d = normalizeKeys(defaultTier);
            selectedTierMonths = Math.max(1, Number(d.months || 1));
            selectedTierAmount = Number(d.price || selectedTierAmount);
        }
        Array.prototype.forEach.call(
            document.querySelectorAll('.npnp-tier'),
            function (el) {
                if (defaultTier && el.getAttribute('data-tier-id') === normalizeKeys(defaultTier).id) {
                    el.classList.add('selected');
                }
                el.addEventListener('click', function () {
                    Array.prototype.forEach.call(
                        document.querySelectorAll('.npnp-tier'),
                        function (e2) { e2.classList.remove('selected'); });
                    el.classList.add('selected');
                    selectedTierMonths = Math.max(1, Number(el.getAttribute('data-tier-months') || 1));
                    selectedTierAmount = Number(el.getAttribute('data-tier-price') || selectedTierAmount);
                    updatePaymentCards(selectedTierAmount, data.currency || 'EUR');
                });
            }
        );

        // Refresh PayPal / Lydia hrefs and visible amount when the user
        // picks a different tier (or on initial highlight selection).
        function updatePaymentCards(amount, currency) {
            var pretty = formatPrice(amount) + ' ' + currency;
            Array.prototype.forEach.call(
                document.querySelectorAll('.npnp-pay-card'),
                function (card) {
                    var method = card.getAttribute('data-method');
                    var base = card.getAttribute('data-base-url');
                    if (base) {
                        card.href = buildPaymentUrl(method, base, amount, currency);
                    }
                    var amt = card.querySelector('.npnp-pay-amount');
                    if (amt) amt.textContent = pretty;
                }
            );
        }
        // Apply the highlight tier amount immediately so the cards open with
        // the correct value even before the user clicks a tier. Never for exempt
        // users — their cards are donation invites with no prefilled amount.
        if (defaultTier && !isExempt) {
            updatePaymentCards(selectedTierAmount, data.currency || 'EUR');
        }

        // Copy IBAN / custom note button.
        var copyBtn = document.getElementById('npnp-copy-note');
        if (copyBtn) {
            copyBtn.addEventListener('click', function () {
                var text = data.customNote || '';
                var done = function () {
                    toast(t(data, 'user.modal.copyNote.done', 'Copied to clipboard.'),
                        'success', t(data, 'user.toast.dismiss', 'Dismiss'));
                };
                var fallback = function () {
                    try {
                        var ta = document.createElement('textarea');
                        ta.value = text;
                        ta.style.position = 'fixed';
                        ta.style.opacity = '0';
                        document.body.appendChild(ta);
                        ta.select();
                        document.execCommand('copy');
                        document.body.removeChild(ta);
                        done();
                    } catch (_) {
                        toast(t(data, 'user.modal.copyNote.fail', 'Copy failed.'),
                            'error', t(data, 'user.toast.dismiss', 'Dismiss'));
                    }
                };
                if (navigator.clipboard && navigator.clipboard.writeText) {
                    navigator.clipboard.writeText(text).then(done).catch(fallback);
                } else {
                    fallback();
                }
            });
        }
        var iPaid = document.getElementById('npnp-i-paid');
        if (iPaid) {
            iPaid.addEventListener('click', function () {
                if (!confirm(t(data, 'user.modal.markPaid.confirm',
                    'Tell the admin you have just paid?'))) return;
                iPaid.disabled = true;

                // In test mode (or admin preview), simulate the round-trip
                // locally so the admin can verify the UX without touching
                // real data.
                if (data.__testMode || data.__previewMode) {
                    var sess = readTestSession();
                    sess.pendingClaim = new Date().toISOString();
                    writeTestSession(sess);
                    toast(t(data, 'user.modal.markPaid.success',
                        'Thanks! Admin has been notified.') + ' '
                        + t(data, 'user.modal.testBadge', 'Test mode'),
                        'success', t(data, 'user.toast.dismiss', 'Dismiss'));
                    closeModal();
                    refresh();
                    return;
                }

                postJson('Me/MarkPaid', { method: '' }).then(function () {
                    toast(t(data, 'user.modal.markPaid.success',
                        'Thanks! Admin has been notified.'), 'success',
                        t(data, 'user.toast.dismiss', 'Dismiss'));
                    closeModal();
                    refresh();
                }).catch(function (err) {
                    var msg = (err && err.status === 429)
                        ? t(data, 'user.modal.markPaid.cooldown', 'Already sent. Try again later.')
                        : t(data, 'user.modal.markPaid.cooldown', 'Already sent.');
                    toast(msg, 'warn', t(data, 'user.toast.dismiss', 'Dismiss'));
                    iPaid.disabled = false;
                });
            });
        }

        var redeem = document.getElementById('npnp-promo-redeem');
        if (redeem) {
            redeem.addEventListener('click', function () {
                var input = document.getElementById('npnp-promo-input');
                var code = (input.value || '').trim();
                if (!code) return;
                redeem.disabled = true;

                // Test-mode (or admin preview) simulation: "INVALID" rejects,
                // anything else accepts and pretends 1 month was added.
                if (data.__testMode || data.__previewMode) {
                    if (code.toUpperCase() === 'INVALID') {
                        toast(t(data, 'user.modal.promo.invalid',
                            'Invalid or already-used code.'), 'error',
                            t(data, 'user.toast.dismiss', 'Dismiss'));
                        redeem.disabled = false;
                        return;
                    }
                    toast(format(t(data, 'user.modal.promo.success',
                        'Code applied: {months} month(s) added.'),
                        { months: 1 }) + ' '
                        + t(data, 'user.modal.testBadge', 'Test mode'),
                        'success', t(data, 'user.toast.dismiss', 'Dismiss'));
                    closeModal();
                    return;
                }

                postJson('Me/RedeemCode', { code: code }).then(function (res) {
                    var r = normalizeKeys(res || {});
                    if (r.ok) {
                        toast(format(t(data, 'user.modal.promo.success',
                            'Code applied: {months} month(s) added.'),
                            { months: r.monthsAdded || 0 }), 'success',
                            t(data, 'user.toast.dismiss', 'Dismiss'));
                        closeModal();
                        refresh();
                    } else {
                        toast(t(data, 'user.modal.promo.invalid',
                            'Invalid or already-used code.'), 'error',
                            t(data, 'user.toast.dismiss', 'Dismiss'));
                        redeem.disabled = false;
                    }
                }).catch(function () {
                    toast(t(data, 'user.modal.promo.invalid',
                        'Invalid or already-used code.'), 'error',
                        t(data, 'user.toast.dismiss', 'Dismiss'));
                    redeem.disabled = false;
                });
            });

            // Pressing Enter in the code field redeems it (matches the button).
            var promoInput = document.getElementById('npnp-promo-input');
            if (promoInput) {
                promoInput.addEventListener('keydown', function (e) {
                    if (e.key === 'Enter') { e.preventDefault(); redeem.click(); }
                });
            }
        }
    }

    // ----- Banner -----
    function ensureBanner(data) {
        var existing = document.getElementById('npnp-banner');
        var needBanner = data.state === 'WarningSoon' || data.state === 'InGrace' || data.state === 'Blocked';
        if (!needBanner) {
            if (existing) existing.parentNode.removeChild(existing);
            document.body.classList.remove('npnp-has-banner');
            document.documentElement.style.setProperty('--npnp-banner-pad', '0px');
            return;
        }

        // Honour user-dismiss for the WarningSoon banner only (informational).
        if (data.state === 'WarningSoon' && sessionStorage.getItem('npnpBannerDismissed') === '1' && !data.__testMode) {
            if (existing) existing.parentNode.removeChild(existing);
            document.body.classList.remove('npnp-has-banner');
            document.documentElement.style.setProperty('--npnp-banner-pad', '0px');
            return;
        }

        ensureStyles();
        if (existing) existing.parentNode.removeChild(existing);

        var titleKey = 'user.banner.' + lcfirst(data.state) + '.short';
        var titleFallbacks = {
            warningSoon: 'Your subscription expires in {days} day(s).',
            inGrace: 'Your subscription has expired — grace period.',
            blocked: 'Playback blocked: subscription expired.'
        };
        var title = format(t(data, titleKey, titleFallbacks[lcfirst(data.state)]), {
            days: Math.max(0, Number(data.daysLeft || 0)),
            date: formatDate(data.expiryDate, data.lang)
        });
        var sub = format(t(data, 'user.modal.summary.dueOn', 'Due on {date}'),
            { date: formatDate(data.expiryDate, data.lang) });

        var banner = document.createElement('div');
        banner.id = 'npnp-banner';
        // Urgent states (expired) are announced assertively; the soft warning is polite.
        banner.setAttribute('role', data.state === 'WarningSoon' ? 'status' : 'alert');
        banner.style.background = 'linear-gradient(90deg,'
            + (STATE_COLORS[data.state] || '#e67e22') + ','
            + (STATE_COLORS[data.state] || '#e67e22') + 'cc)';
        var bannerIcons = { WarningSoon: 'schedule', InGrace: 'warning', Blocked: 'block' };
        var bannerIcon = '<span class="material-icons npnp-banner-icon" aria-hidden="true">'
            + (bannerIcons[data.state] || 'info') + '</span>';
        var testBadge = data.__testMode
            ? '<span class="npnp-test-badge">' + escapeHtml(t(data, 'user.modal.testBadge', 'Test mode')) + '</span>'
            : '';
        var dismissBtn = data.state === 'WarningSoon' && !data.__testMode
            ? '<button type="button" class="npnp-banner-dismiss" id="npnp-banner-dismiss">'
                + escapeHtml(t(data, 'user.banner.cta.dismiss', 'Dismiss')) + '</button>'
            : '';
        banner.innerHTML = ''
            + bannerIcon
            + '<div class="npnp-banner-msg">'
            + '<span class="npnp-banner-title">' + testBadge + escapeHtml(title) + '</span>'
            + '<span class="npnp-banner-sub">' + escapeHtml(sub) + '</span>'
            + '</div>'
            + '<div class="npnp-banner-actions">'
            +   dismissBtn
            + '<button type="button" id="npnp-banner-pay">'
            +   escapeHtml(t(data, 'user.banner.cta.payNow', 'Pay now')) + '</button>'
            + '</div>';

        document.body.appendChild(banner);
        document.body.classList.add('npnp-has-banner');

        document.getElementById('npnp-banner-pay').addEventListener('click', function () {
            openModal(data);
        });
        var d = document.getElementById('npnp-banner-dismiss');
        if (d) {
            d.addEventListener('click', function () {
                try { sessionStorage.setItem('npnpBannerDismissed', '1'); } catch (_) {}
                banner.parentNode.removeChild(banner);
                document.body.classList.remove('npnp-has-banner');
                document.documentElement.style.setProperty('--npnp-banner-pad', '0px');
            });
        }

        positionBanner();
    }

    function positionBanner() {
        var banner = document.getElementById('npnp-banner');
        if (!banner) return;
        var header = document.querySelector('.skinHeader');
        var headerH = header ? header.getBoundingClientRect().height : 0;
        document.documentElement.style.setProperty('--npnp-header-h', headerH + 'px');
        requestAnimationFrame(function () {
            var bannerH = banner.getBoundingClientRect().height;
            document.documentElement.style.setProperty('--npnp-banner-pad', (headerH + bannerH) + 'px');
        });
    }

    function ensureHeaderButton(data) {
        if (document.querySelector('.npnp-header-btn')) return true;
        var label = t(data, 'user.modal.headerButton', 'My subscription');

        var search = document.querySelector(
            '.skinHeader .headerSearchButton, .skinHeader [is="emby-button"][title="Search"], '
            + '.skinHeader button[title*="earch"]');
        var container = null;
        var anchor = null;
        if (search && search.parentNode) {
            container = search.parentNode;
            anchor = search;
        } else {
            container = document.querySelector('.headerRight, .skinHeader .headerRight, .skinHeader-content, .skinHeader');
            if (!container) return false;
        }

        var btn = document.createElement('button');
        btn.type = 'button';
        btn.className = 'npnp-header-btn paper-icon-button-light';
        btn.title = label;
        btn.setAttribute('aria-label', label);
        btn.innerHTML = '<span class="material-icons" aria-hidden="true">monetization_on</span>';
        btn.addEventListener('click', function () { openModal(lastData || data); });

        if (anchor) container.insertBefore(btn, anchor);
        else container.appendChild(btn);
        return true;
    }

    var lastData = null;

    // Render a Jellyfin-style activity-log preview toast that mirrors the
    // notification the EnforcementTask would post for the simulated state.
    // Fires once per tab so the admin actually sees what the bell
    // would announce.
    function showTestNotificationPreview(data) {
        if (!data || !data.__testMode) return;
        if (window.__npnpTestNotifShown) return;
        window.__npnpTestNotifShown = true;

        var titleByState = {
            Ok: t(data, 'user.modal.testBadge', 'Test mode') + ' — '
                + t(data, 'state.ok', 'Up to date'),
            WarningSoon: t(data, 'notif.warningSoon.title',
                'NoPayNoPlay: subscription expiring soon'),
            InGrace: t(data, 'notif.inGrace.title',
                'NoPayNoPlay: grace period'),
            Blocked: t(data, 'notif.blocked.title',
                'NoPayNoPlay: playback blocked'),
            Exempt: t(data, 'user.modal.testBadge', 'Test mode') + ' — '
                + t(data, 'state.exempt', 'Free access')
        };
        var kindByState = {
            Ok: 'success',
            WarningSoon: 'warn',
            InGrace: 'warn',
            Blocked: 'error',
            Exempt: 'success'
        };
        var msg = titleByState[data.state]
            || (t(data, 'user.modal.testBadge', 'Test mode') + ': ' + data.state);
        toast(msg, kindByState[data.state] || 'warn',
            t(data, 'user.toast.dismiss', 'Dismiss'));
    }

    function refresh() {
        return fetchMe().then(function (raw) {
            var normalized = normalizeMe(raw);
            var data = applyTestOverride(normalized);
            lastData = data;
            showTestNotificationPreview(data);
            if (data.state === 'Exempt' && !data.__testMode) {
                var b = document.getElementById('npnp-banner');
                if (b) b.parentNode.removeChild(b);
                document.body.classList.remove('npnp-has-banner');
                document.documentElement.style.setProperty('--npnp-banner-pad', '0px');
                ensureHeaderButton(data);
                return;
            }
            ensureHeaderButton(data);
            ensureBanner(data);
        }).catch(function () {});
    }

    function tick() {
        if (lastData) {
            ensureHeaderButton(lastData);
            if (document.getElementById('npnp-banner')) positionBanner();
        }
    }

    function onReady() {
        refresh();
        setInterval(tick, 2000);
        setInterval(refresh, 5 * 60 * 1000);
        document.addEventListener('viewshow', refresh);
        window.addEventListener('resize', positionBanner);

        // Hash deep-link: opening Jellyfin with #!/npnp (or #npnp) auto-opens
        // the modal once data is loaded. Useful for bookmarks and emails.
        function checkHashOpen() {
            var h = (window.location.hash || '').toLowerCase();
            if (h === '#!/npnp' || h === '#npnp') {
                if (lastData) {
                    openModal(lastData);
                } else {
                    refresh().then(function () {
                        if (lastData) openModal(lastData);
                    });
                }
                // Clear the hash so reopening the page doesn't reopen the modal.
                try { history.replaceState(null, '', window.location.pathname + window.location.search); }
                catch (_) {}
            }
        }
        window.addEventListener('hashchange', checkHashOpen);
        checkHashOpen();
        try {
            var mo = new MutationObserver(function () {
                if (lastData) {
                    ensureHeaderButton(lastData);
                    if (document.getElementById('npnp-banner')) positionBanner();
                }
            });
            mo.observe(document.body, { childList: true, subtree: true });
        } catch (_) {}
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', onReady);
    } else {
        onReady();
    }
})();
