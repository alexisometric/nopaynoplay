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
        Exempt: '#95a5a6'
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

    function ensureStyles() {
        if (document.getElementById('npnp-styles')) return;
        var css = ''
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
            + '.npnp-modal{background:#1a1a1a;color:#fff;padding:0;border-radius:12px;'
            + 'width:min(560px,92vw);max-height:90vh;overflow:auto;'
            + 'font:14px/1.5 system-ui,-apple-system,sans-serif;'
            + 'box-shadow:0 16px 48px rgba(0,0,0,.6);animation:npnpSlide .18s ease-out;}'
            + '.npnp-modal-header{padding:18px 20px 0;display:flex;align-items:flex-start;justify-content:space-between;gap:12px;}'
            + '.npnp-modal-header h2{margin:0;font-size:19px;font-weight:700;}'
            + '.npnp-modal-header .close{background:none;border:none;color:#fff;'
            + 'font-size:24px;line-height:1;cursor:pointer;opacity:.7;padding:4px 8px;}'
            + '.npnp-modal-header .close:hover{opacity:1;}'
            + '.npnp-modal-body{padding:14px 20px 20px;}'
            + '.npnp-summary{display:flex;flex-direction:column;gap:6px;padding:14px 16px;'
            + 'border-radius:10px;margin-bottom:14px;}'
            + '.npnp-summary .npnp-status-pill{display:inline-flex;align-items:center;gap:6px;'
            + 'font-size:12px;font-weight:700;text-transform:uppercase;letter-spacing:.6px;'
            + 'padding:4px 10px;border-radius:999px;background:rgba(255,255,255,.18);align-self:flex-start;}'
            + '.npnp-summary .npnp-summary-main{font-size:22px;font-weight:700;line-height:1.2;}'
            + '.npnp-summary .npnp-summary-sub{font-size:13px;opacity:.92;}'
            + '.npnp-modal h3{margin:18px 0 8px;font-size:11px;text-transform:uppercase;'
            + 'letter-spacing:.7px;opacity:.65;font-weight:700;}'
            + '.npnp-modal .row{margin:6px 0;}'
            + '.npnp-pay-grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(160px,1fr));gap:10px;}'
            + '.npnp-pay-card{background:#262626;border:1px solid #333;border-radius:10px;padding:14px;'
            + 'display:flex;flex-direction:column;gap:8px;text-decoration:none;color:#fff;'
            + 'transition:border-color .12s,transform .12s;}'
            + '.npnp-pay-card:hover{border-color:#00a4dc;transform:translateY(-1px);}'
            + '.npnp-pay-card .npnp-pay-title{font-weight:700;font-size:14px;display:flex;align-items:center;gap:8px;}'
            + '.npnp-pay-card .npnp-pay-amount{font-size:13px;opacity:.85;}'
            + '.npnp-pay-card .material-icons{font-size:18px;}'
            + '.npnp-mini-btn{background:#2a2a2a;color:#fff;border:1px solid #444;border-radius:6px;'
            + 'padding:6px 12px;cursor:pointer;font-size:13px;font-weight:600;}'
            + '.npnp-mini-btn:hover{border-color:#00a4dc;}'
            + '.npnp-mini-btn.primary{background:#00a4dc;border-color:#00a4dc;}'
            + '.npnp-mini-btn.primary:hover{background:#0088b8;}'
            + '.npnp-mini-btn.success{background:#27ae60;border-color:#27ae60;}'
            + '.npnp-mini-btn:disabled{opacity:.5;cursor:not-allowed;}'
            + '.npnp-pending-banner{background:rgba(243,156,18,.15);border:1px solid #f39c12;'
            + 'color:#fde3a7;padding:10px 12px;border-radius:8px;font-size:13px;margin:8px 0 0;'
            + 'display:flex;align-items:center;gap:8px;}'
            + '.npnp-history{width:100%;border-collapse:collapse;font-size:13px;}'
            + '.npnp-history th,.npnp-history td{padding:6px 8px;border-bottom:1px solid #2a2a2a;text-align:left;}'
            + '.npnp-history th{opacity:.6;font-weight:600;font-size:11px;text-transform:uppercase;letter-spacing:.4px;}'
            + '.npnp-empty{opacity:.55;font-style:italic;padding:8px 0;font-size:13px;}'
            + '.npnp-promo-row{display:flex;gap:8px;align-items:center;}'
            + '.npnp-promo-row input{flex:1;background:#0d0d0d;border:1px solid #333;color:#fff;'
            + 'padding:8px 10px;border-radius:6px;font:600 13px ui-monospace,monospace;'
            + 'text-transform:uppercase;letter-spacing:1px;}'
            + '.npnp-qr-wrap{display:flex;align-items:center;gap:14px;background:#fff;color:#222;'
            + 'padding:12px;border-radius:10px;margin-top:10px;}'
            + '.npnp-qr-wrap svg{width:120px;height:120px;flex-shrink:0;}'
            + '.npnp-qr-wrap .npnp-qr-info{font-size:12px;color:#444;}'
            // Tier cards
            + '.npnp-tiers{display:grid;grid-template-columns:repeat(auto-fit,minmax(140px,1fr));gap:10px;margin:6px 0 4px;}'
            + '.npnp-tier{background:#1f1f1f;border:1px solid #333;border-radius:10px;padding:12px;text-align:center;'
            + 'cursor:pointer;transition:transform .12s,border-color .12s,background .12s;position:relative;}'
            + '.npnp-tier:hover{border-color:#00a4dc;transform:translateY(-1px);}'
            + '.npnp-tier .npnp-tier-months{font-size:13px;opacity:.75;text-transform:uppercase;letter-spacing:.5px;}'
            + '.npnp-tier .npnp-tier-price{font-size:22px;font-weight:800;margin:4px 0 2px;}'
            + '.npnp-tier .npnp-tier-permonth{font-size:11px;opacity:.6;}'
            + '.npnp-tier .npnp-tier-label{font-size:12px;opacity:.85;margin-top:6px;}'
            + '.npnp-tier.highlight{background:linear-gradient(135deg,#00a4dc22,#1f1f1f);'
            + 'border-color:#00a4dc;box-shadow:0 0 0 1px #00a4dc inset,0 6px 18px rgba(0,164,220,.15);}'
            + '.npnp-tier.highlight .npnp-tier-badge{position:absolute;top:-9px;right:8px;background:#00a4dc;color:#fff;'
            + 'padding:2px 8px;border-radius:999px;font-size:10px;font-weight:700;text-transform:uppercase;letter-spacing:.5px;}'
            + '.npnp-tier.selected{outline:2px solid #00a4dc;}'
            + '.npnp-note-row{display:flex;align-items:flex-start;gap:8px;margin-top:12px;font-size:13px;opacity:.85;}'
            + '.npnp-note-row .npnp-note-text{flex:1;white-space:pre-wrap;word-break:break-word;}'
            + '.npnp-contact-row{margin-top:10px;font-size:13px;}'
            + '.npnp-contact-row a{color:#00a4dc;text-decoration:none;}'
            + '.npnp-contact-row a:hover{text-decoration:underline;}'
            // Exempt banner
            + '.npnp-exempt-banner{display:flex;align-items:center;gap:12px;'
            + 'background:linear-gradient(135deg,#2ecc7133,#1f1f1f);border:1px solid #2ecc7166;'
            + 'border-radius:10px;padding:12px 14px;margin:0 0 12px;}'
            + '.npnp-exempt-banner .material-icons{color:#2ecc71;font-size:28px;}'
            + '.npnp-exempt-title{font-weight:700;font-size:15px;}'
            + '.npnp-exempt-sub{font-size:13px;opacity:.85;margin-top:2px;}'
            // Donation note
            + '.npnp-donate-note{display:flex;align-items:center;gap:8px;'
            + 'background:rgba(231,76,60,.10);border:1px solid rgba(231,76,60,.35);'
            + 'border-radius:8px;padding:10px 12px;margin-top:10px;font-size:13px;line-height:1.4;}'
            + '.npnp-donate-note .material-icons{color:#e74c3c;font-size:18px;flex-shrink:0;}'
            // Toast
            + '.npnp-toast-stack{position:fixed;right:18px;bottom:18px;z-index:10001;'
            + 'display:flex;flex-direction:column-reverse;gap:8px;pointer-events:none;}'
            + '.npnp-toast{pointer-events:auto;background:#222;color:#fff;'
            + 'padding:12px 16px;border-radius:8px;box-shadow:0 6px 20px rgba(0,0,0,.5);'
            + 'min-width:240px;max-width:360px;display:flex;align-items:center;gap:10px;'
            + 'font:14px/1.4 system-ui,sans-serif;border-left:4px solid #00a4dc;'
            + 'animation:npnpSlide .18s ease-out;}'
            + '.npnp-toast.success{border-left-color:#2ecc71;}'
            + '.npnp-toast.error{border-left-color:#e74c3c;}'
            + '.npnp-toast.warn{border-left-color:#f39c12;}'
            + '.npnp-toast .npnp-toast-close{background:none;border:none;color:#aaa;'
            + 'cursor:pointer;font-size:16px;padding:0;line-height:1;}';
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
        var rows = tx.slice(0, 5).map(function (e) {
            return '<tr>'
                + '<td>' + escapeHtml(formatDate(e.date, data.lang)) + '</td>'
                + '<td>' + escapeHtml(formatPrice(e.amount)) + ' ' + escapeHtml(data.currency || 'EUR') + '</td>'
                + '<td>' + escapeHtml(e.method || '') + '</td>'
                + '<td>' + escapeHtml(String(e.monthsAdded || 0)) + '</td>'
                + '</tr>';
        }).join('');
        return '<table class="npnp-history">'
            + '<thead><tr>'
            + '<th>' + escapeHtml(t(data, 'user.modal.history.date', 'Date')) + '</th>'
            + '<th>' + escapeHtml(t(data, 'user.modal.history.amount', 'Amount')) + '</th>'
            + '<th>' + escapeHtml(t(data, 'user.modal.history.method', 'Method')) + '</th>'
            + '<th>' + escapeHtml(t(data, 'user.modal.history.months', 'Months')) + '</th>'
            + '</tr></thead><tbody>' + rows + '</tbody></table>';
    }

    function renderSummary(data) {
        var color = STATE_COLORS[data.state] || '#444';
        var stateLabel = t(data, 'state.' + lcfirst(data.state), data.state);

        var main = '';
        var sub = '';
        var dueOn = format(t(data, 'user.modal.summary.dueOn', 'Due on {date}'), {
            date: formatDate(data.expiryDate, data.lang)
        });

        if (data.state === 'Ok' || data.state === 'WarningSoon') {
            main = tp(data, 'user.modal.summary.daysLeft',
                Math.max(0, Number(data.daysLeft || 0)),
                format(t(data, 'user.modal.summary.daysLeft', '{days} day(s) left'),
                    { days: Math.max(0, Number(data.daysLeft || 0)) }));
            sub = dueOn;
        } else if (data.state === 'InGrace' || data.state === 'Blocked') {
            main = tp(data, 'user.modal.summary.expiredAgo',
                Math.max(0, -Number(data.daysLeft || 0)),
                format(t(data, 'user.modal.summary.expiredAgo', 'Expired {days} day(s) ago'),
                    { days: Math.max(0, -Number(data.daysLeft || 0)) }));
            sub = dueOn;
        } else {
            main = stateLabel;
            sub = dueOn;
        }

        return '<div class="npnp-summary" style="background:linear-gradient(135deg,'
            + color + '33,' + color + '11);border:1px solid ' + color + '66;">'
            + '<span class="npnp-status-pill" style="background:' + color + '33;color:#fff;">'
            + escapeHtml(stateLabel) + '</span>'
            + '<div class="npnp-summary-main">' + escapeHtml(main) + '</div>'
            + '<div class="npnp-summary-sub">' + escapeHtml(sub) + '</div>'
            + '</div>';
    }

    function renderTiers(data) {
        var tiers = Array.isArray(data.tiers) ? data.tiers : [];
        if (!tiers.length) return '';
        var currency = data.currency || 'EUR';
        var badgeLabel = t(data, 'user.modal.tier.popular', 'Best deal');
        var perMonth = t(data, 'user.modal.tier.perMonth', '{price} / month');
        var monthsLabel = t(data, 'user.modal.tier.months', '{n} month(s)');
        var html = tiers.map(function (raw) {
            var t1 = normalizeKeys(raw);
            var months = Math.max(1, Number(t1.months || 1));
            var price = Number(t1.price || 0);
            var pm = months > 0 ? (price / months) : 0;
            return '<button type="button" class="npnp-tier' + (t1.highlight ? ' highlight' : '')
                + '" data-tier-id="' + escapeHtml(t1.id || '') + '"'
                + ' data-tier-months="' + months + '"'
                + ' data-tier-price="' + price + '">'
                + (t1.highlight ? '<span class="npnp-tier-badge">' + escapeHtml(badgeLabel) + '</span>' : '')
                + '<div class="npnp-tier-months">' + escapeHtml(format(monthsLabel, { n: months })) + '</div>'
                + '<div class="npnp-tier-price">' + escapeHtml(formatPrice(price)) + ' ' + escapeHtml(currency) + '</div>'
                + '<div class="npnp-tier-permonth">' + escapeHtml(format(perMonth, { price: formatPrice(pm) + ' ' + currency })) + '</div>'
                + (t1.label ? '<div class="npnp-tier-label">' + escapeHtml(t1.label) + '</div>' : '')
                + '</button>';
        }).join('');
        return '<h3>' + escapeHtml(t(data, 'user.modal.section.tiers', 'Choose a plan'))
            + '</h3><div class="npnp-tiers">' + html + '</div>';
    }

    function paymentCardHtml(data, key, url, methodLabel) {
        if (!url) return '';
        var amount = formatPrice(data.price) + ' ' + (data.currency || 'EUR');
        return '<a class="npnp-pay-card" href="' + escapeHtml(url)
            + '" target="_blank" rel="noopener" data-method="' + escapeHtml(key) + '">'
            + '<span class="npnp-pay-title">'
            + '<span class="material-icons" aria-hidden="true">open_in_new</span>'
            + escapeHtml(methodLabel) + '</span>'
            + '<span class="npnp-pay-amount">' + escapeHtml(amount) + '</span>'
            + '</a>';
    }

    function buildQrUrl(text) {
        var api = getApiClient();
        if (!api || !text) return '';
        return api.getUrl('NoPayNoPlay/Qr', { text: text });
    }

    // Fetch the QR SVG through the authenticated ApiClient and inline it into
    // the slot. Falls back to a clickable text link when generation fails.
    function loadQrInto(slot, text, data) {
        var api = getApiClient();
        if (!api || !text || !slot) return;
        api.ajax({
            type: 'GET',
            url: api.getUrl('NoPayNoPlay/Qr', { text: text }),
            dataType: 'text',
            headers: { Accept: 'image/svg+xml,text/plain,*/*' }
        }).then(function (svg) {
            if (typeof svg !== 'string' || svg.indexOf('<svg') === -1) {
                throw new Error('not-svg');
            }
            // Force the SVG to fill the 120x120 slot regardless of QRCoder defaults.
            var sized = svg.replace(/<svg\b([^>]*)>/i, function (m, attrs) {
                var stripped = attrs
                    .replace(/\swidth="[^"]*"/i, '')
                    .replace(/\sheight="[^"]*"/i, '');
                return '<svg' + stripped + ' width="120" height="120">';
            });
            slot.innerHTML = sized;
            slot.style.color = '';
            slot.style.fontSize = '';
        }).catch(function () {
            slot.style.fontSize = '11px';
            slot.style.color = '#888';
            slot.style.padding = '6px';
            slot.style.textAlign = 'center';
            slot.style.wordBreak = 'break-all';
            slot.textContent = t(data, 'user.modal.qr.fallback',
                'QR unavailable — copy the link above.');
        });
    }

    function openModal(rawData) {
        ensureStyles();
        closeModal();
        var data = applyAdminPreviewSkin(rawData);
        var isExempt = data.state === 'Exempt';

        var paypalLabel = t(data, 'user.modal.method.paypal', 'Pay with PayPal');
        var lydiaLabel = t(data, 'user.modal.method.lydia', 'Pay with Lydia');
        var cards = [
            paymentCardHtml(data, 'paypal', data.paypalMeUrl, paypalLabel),
            paymentCardHtml(data, 'lydia', data.lydiaUrl, lydiaLabel)
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

        // QR for the first available payment URL. We fetch the SVG via the
        // authenticated ApiClient (rather than an <img> tag, which doesn't carry
        // Jellyfin's auth headers and used to fail silently for some users) and
        // inject it inline once the modal is in the DOM.
        var primaryUrl = data.paypalMeUrl || data.lydiaUrl || '';
        var qrSection = '';
        if (primaryUrl) {
            qrSection = '<div class="npnp-qr-wrap">'
                + '<div id="npnp-qr-slot" style="width:120px;height:120px;flex-shrink:0;'
                + 'display:flex;align-items:center;justify-content:center;'
                + 'font-size:11px;color:#888;">…</div>'
                + '<div class="npnp-qr-info">'
                + '<div style="font-weight:700;color:#222;margin-bottom:4px;">'
                + escapeHtml(t(data, 'user.modal.qr.title', 'Or scan this QR code'))
                + '</div>'
                + '<div>' + escapeHtml(t(data, 'user.modal.qr.scan', 'Scan with your phone'))
                + '</div></div></div>';
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
        if (!data.__previewMode && !isExempt) {
            iPaidBtn = '<button type="button" class="npnp-mini-btn primary" id="npnp-i-paid"'
                + (data.hasPendingPaymentClaim ? ' disabled' : '')
                + '>'
                + '<span class="material-icons" aria-hidden="true" style="vertical-align:middle;font-size:16px;">check_circle</span> '
                + escapeHtml(t(data, 'user.modal.markPaid', 'I just paid'))
                + '</button>';
        }

        var promoSection = (data.__previewMode || isExempt) ? '' : ''
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

        var exemptBanner = isExempt && !data.__previewMode
            ? '<div class="npnp-exempt-banner">'
                + '<span class="material-icons" aria-hidden="true">verified</span>'
                + '<div>'
                + '<div class="npnp-exempt-title">'
                + escapeHtml(t(data, 'user.modal.exempt.title', 'Free access for you'))
                + '</div>'
                + '<div class="npnp-exempt-sub">'
                + escapeHtml(t(data, 'user.modal.exempt.sub',
                    'No subscription is required — enjoy the server.'))
                + '</div>'
                + '</div>'
                + '</div>'
            : '';

        var html = ''
            + '<div class="npnp-modal-backdrop" id="npnp-modal-backdrop">'
            + '  <div class="npnp-modal" role="dialog" aria-modal="true" aria-label="' + escapeHtml(modalTitle) + '">'
            + '    <div class="npnp-modal-header">'
            + '      <h2>' + escapeHtml(modalTitle) + '</h2>'
            + '      <button class="close" type="button" aria-label="'
            +          escapeHtml(t(data, 'user.modal.close', 'Close')) + '">&times;</button>'
            + '    </div>'
            + '    <div class="npnp-modal-body">'
            +        previewBanner
            +        exemptBanner
            +        renderSummary(data)
            +        pendingBlock
            +        (isExempt ? '' : renderTiers(data))
            +        paySection
            +        donateNote
            +        (isExempt ? '' : qrSection)
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

        // Asynchronously fetch the QR SVG and inline it. This works regardless
        // of whether <img> tags can carry Jellyfin auth, and lets us show a
        // useful fallback (the URL itself) if generation fails.
        var qrSlot = document.getElementById('npnp-qr-slot');
        if (qrSlot && primaryUrl) {
            loadQrInto(qrSlot, primaryUrl, data);
        }

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
                });
            }
        );

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

                // In test mode, simulate the round-trip locally so the admin
                // can verify the UX without touching real data.
                if (data.__testMode) {
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

                // Test-mode simulation: "INVALID" rejects, anything else accepts
                // and pretends 1 month was added.
                if (data.__testMode) {
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
        banner.style.background = 'linear-gradient(90deg,'
            + (STATE_COLORS[data.state] || '#e67e22') + ','
            + (STATE_COLORS[data.state] || '#e67e22') + 'cc)';
        var testBadge = data.__testMode
            ? '<span class="npnp-test-badge">' + escapeHtml(t(data, 'user.modal.testBadge', 'Test mode')) + '</span>'
            : '';
        var dismissBtn = data.state === 'WarningSoon' && !data.__testMode
            ? '<button type="button" class="npnp-banner-dismiss" id="npnp-banner-dismiss">'
                + escapeHtml(t(data, 'user.banner.cta.dismiss', 'Dismiss')) + '</button>'
            : '';
        banner.innerHTML = ''
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
