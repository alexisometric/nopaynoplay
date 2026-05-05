/* NoPayNoPlay - client injected into jellyfin-web. */
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

    // Jellyfin's default JSON serializer keeps PascalCase. Normalize the top-level
    // payload (and the Transactions array) to camelCase so the rest of the code can
    // assume `data.price`, `data.strings`, etc. without guessing both casings.
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
        var url = api.getUrl('NoPayNoPlay/Me');
        return api.ajax({ type: 'GET', url: url, dataType: 'json' });
    }

    function applyTestOverride(data) {
        var forced = getTestStateOverride();
        if (!forced) return data;
        var clone = Object.assign({}, data);
        clone.state = forced;
        clone.__testMode = true;
        // Provide plausible expiry / days-left for the previewed banner.
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
        }
        return clone;
    }

    // Re-skin admin preview data so the modal is meaningful (a real subscription
    // about to expire) instead of showing the admin's own "Exempt" status.
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

    function ensureStyles() {
        if (document.getElementById('npnp-styles')) return;
        var css = ''
            + '#npnp-banner{position:fixed;left:0;right:0;z-index:998;'
            + 'top:var(--npnp-header-h,0px);'
            + 'padding:10px 16px;font:600 14px/1.4 system-ui,sans-serif;color:#fff;'
            + 'display:flex;align-items:center;justify-content:space-between;gap:12px;'
            + 'box-shadow:0 2px 6px rgba(0,0,0,.25);}'
            + '@media (max-width:600px){#npnp-banner{font-size:13px;padding:8px 12px;flex-wrap:wrap;}'
            + '#npnp-banner > span{flex:1 1 100%;}}'
            + 'body.npnp-has-banner .mainAnimatedPages,body.npnp-has-banner .skinBody,'
            + 'body.npnp-has-banner .pageContainer{padding-top:var(--npnp-banner-pad,0px) !important;}'
            + '#npnp-banner button{background:rgba(255,255,255,.2);color:#fff;border:1px solid #fff;'
            + 'padding:6px 12px;border-radius:4px;cursor:pointer;font-weight:600;}'
            + '#npnp-banner button:hover{background:rgba(255,255,255,.35);} '
            + '#npnp-banner .npnp-test-badge{background:#000;color:#fff;border-radius:4px;'
            + 'padding:2px 6px;font-size:11px;margin-right:8px;letter-spacing:.5px;}'
            + '.npnp-header-btn{background:transparent;border:none;color:inherit;cursor:pointer;'
            + 'padding:8px;display:inline-flex;align-items:center;justify-content:center;}'
            + '.npnp-header-btn .material-icons{font-size:24px;}'
            + '.npnp-modal-backdrop{position:fixed;inset:0;background:rgba(0,0,0,.6);'
            + 'z-index:10000;display:flex;align-items:center;justify-content:center;}'
            + '.npnp-modal{background:#202020;color:#fff;padding:24px;border-radius:8px;'
            + 'min-width:340px;max-width:90vw;max-height:90vh;overflow:auto;'
            + 'font:14px/1.5 system-ui,sans-serif;box-shadow:0 10px 40px rgba(0,0,0,.5);}'
            + '.npnp-modal h2{margin:0 0 12px;font-size:18px;}'
            + '.npnp-modal h3{margin:18px 0 8px;font-size:14px;text-transform:uppercase;'
            + 'letter-spacing:.5px;opacity:.75;}'
            + '.npnp-modal .row{margin:6px 0;}'
            + '.npnp-modal .actions{display:flex;flex-wrap:wrap;gap:8px;margin-top:16px;}'
            + '.npnp-modal .actions a,.npnp-modal .actions button{'
            + 'background:#00a4dc;color:#fff;text-decoration:none;padding:8px 14px;'
            + 'border-radius:4px;font-weight:600;border:none;cursor:pointer;display:inline-block;}'
            + '.npnp-modal .actions a:hover,.npnp-modal .actions button:hover{background:#0088b8;}'
            + '.npnp-modal .close{float:right;cursor:pointer;background:none;border:none;color:#fff;'
            + 'font-size:20px;line-height:1;}'
            + '.npnp-iban{background:#111;padding:8px;border-radius:4px;font-family:monospace;'
            + 'word-break:break-all;margin:6px 0;}'
            + '.npnp-history{width:100%;border-collapse:collapse;font-size:13px;}'
            + '.npnp-history th,.npnp-history td{padding:6px 8px;border-bottom:1px solid #333;'
            + 'text-align:left;}'
            + '.npnp-history th{opacity:.7;font-weight:600;}'
            + '.npnp-empty{opacity:.6;font-style:italic;padding:8px 0;}';
        var s = document.createElement('style');
        s.id = 'npnp-styles';
        s.textContent = css;
        document.head.appendChild(s);
    }

    function closeModal() {
        var m = document.getElementById('npnp-modal-backdrop');
        if (m) m.parentNode.removeChild(m);
    }

    function lcfirst(s) {
        return s ? s.charAt(0).toLowerCase() + s.slice(1) : s;
    }

    function escapeHtml(s) {
        return String(s == null ? '' : s).replace(/[&<>"']/g, function (c) {
            return ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', '\'': '&#39;' })[c];
        });
    }

    function renderHistory(data) {
        var tx = (data.transactions || data.Transactions || []);
        if (!tx.length) {
            return '<div class="npnp-empty">'
                + escapeHtml(t(data, 'user.modal.history.empty', 'No payment recorded yet.'))
                + '</div>';
        }
        var rows = tx.map(function (e) {
            var date = e.date || e.Date;
            var amount = (e.amount != null ? e.amount : e.Amount) || 0;
            var months = (e.monthsAdded != null ? e.monthsAdded : e.MonthsAdded) || 0;
            var method = e.method || e.Method || '';
            return '<tr>'
                + '<td>' + escapeHtml(formatDate(date, data.lang)) + '</td>'
                + '<td>' + escapeHtml(Number(amount).toFixed(2)) + ' ' + escapeHtml(data.currency || 'EUR') + '</td>'
                + '<td>' + escapeHtml(method) + '</td>'
                + '<td>' + escapeHtml(String(months)) + '</td>'
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

    function openModal(rawData) {
        ensureStyles();
        closeModal();
        // Admin preview: render a sample-data view of the modal so admins can
        // inspect what regular users see.
        var data = applyAdminPreviewSkin(rawData);

        var actions = [];
        if (data.paypalMeUrl) {
            actions.push('<a href="' + data.paypalMeUrl + '" target="_blank" rel="noopener">PayPal</a>');
        }
        if (data.lydiaUrl) {
            actions.push('<a href="' + data.lydiaUrl + '" target="_blank" rel="noopener">Lydia</a>');
        }

        var ibanBlock = '';
        if (data.ibanText) {
            ibanBlock = '<div class="row">' + escapeHtml(t(data, 'user.modal.bankTransfer', 'Bank transfer (IBAN):')) + '</div>'
                + '<div class="npnp-iban" id="npnp-iban-text">' + escapeHtml(data.ibanText) + '</div>'
                + '<button id="npnp-copy-iban" type="button">' + escapeHtml(t(data, 'user.modal.copyIban', 'Copy IBAN')) + '</button>';
        }

        var note = data.customNote
            ? '<div class="row" style="opacity:.8;margin-top:12px;">' + escapeHtml(data.customNote) + '</div>'
            : '';

        var stateLabel = t(data, 'state.' + lcfirst(data.state), data.state);
        var modalTitle = t(data, 'user.modal.title', 'My subscription');
        var historyTitle = t(data, 'user.modal.history', 'Payment history');
        var previewBanner = data.__previewMode
            ? '<div class="row" style="background:rgba(0,164,220,.18);border:1px solid #00a4dc;'
                + 'padding:8px 12px;border-radius:4px;margin:0 0 12px;font-size:13px;">'
                + escapeHtml(t(data, 'user.modal.previewBadge', 'Admin preview — sample data'))
                + '</div>'
            : '';

        var html = ''
            + '<div class="npnp-modal-backdrop" id="npnp-modal-backdrop">'
            + '  <div class="npnp-modal" role="dialog" aria-modal="true" aria-label="' + escapeHtml(modalTitle) + '">'
            + '    <button class="close" type="button" aria-label="' + escapeHtml(t(data, 'user.modal.close', 'Close')) + '">&times;</button>'
            + '    <h2>' + escapeHtml(modalTitle) + '</h2>'
            + previewBanner
            + '    <div class="row">' + escapeHtml(t(data, 'user.modal.state', 'Status:')) + ' <strong style="color:' + (STATE_COLORS[data.state] || '#fff') + '">' + escapeHtml(stateLabel) + '</strong></div>'
            + '    <div class="row">' + escapeHtml(t(data, 'user.modal.nextDue', 'Next due date:')) + ' <strong>' + formatDate(data.expiryDate, data.lang) + '</strong></div>'
            + '    <div class="row">' + escapeHtml(t(data, 'user.modal.amount', 'Amount:')) + ' <strong>' + Number(data.price || 0).toFixed(2) + ' ' + escapeHtml(data.currency || 'EUR') + '</strong></div>'
            + '    <div class="actions">' + actions.join('') + '</div>'
            + ibanBlock
            + note
            + '    <h3>' + escapeHtml(historyTitle) + '</h3>'
            + '    ' + renderHistory(data)
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

        var copyBtn = document.getElementById('npnp-copy-iban');
        if (copyBtn) {
            copyBtn.addEventListener('click', function () {
                var txt = document.getElementById('npnp-iban-text').innerText;
                if (navigator.clipboard) navigator.clipboard.writeText(txt);
                var copied = t(data, 'user.modal.copied', 'Copied!');
                var label = t(data, 'user.modal.copyIban', 'Copy IBAN');
                copyBtn.textContent = copied;
                setTimeout(function () { copyBtn.textContent = label; }, 1500);
            });
        }
    }

    function ensureBanner(data) {
        var existing = document.getElementById('npnp-banner');
        var needBanner = data.state === 'WarningSoon' || data.state === 'InGrace' || data.state === 'Blocked';
        if (!needBanner) {
            if (existing) existing.parentNode.removeChild(existing);
            document.body.classList.remove('npnp-has-banner');
            document.documentElement.style.setProperty('--npnp-banner-pad', '0px');
            return;
        }

        ensureStyles();
        if (existing) existing.parentNode.removeChild(existing);

        var key = 'user.banner.' + lcfirst(data.state);
        var fallbacks = {
            warningSoon: 'Your subscription expires on {date} (in {days} day(s)).',
            inGrace: 'Subscription expired on {date} — grace period in progress.',
            blocked: 'Playback disabled: subscription expired on {date}.'
        };
        var template = t(data, key, fallbacks[lcfirst(data.state)]);
        var msg = format(template, {
            date: formatDate(data.expiryDate, data.lang),
            days: data.daysLeft
        });

        var banner = document.createElement('div');
        banner.id = 'npnp-banner';
        banner.style.background = STATE_COLORS[data.state] || '#e67e22';
        var testBadge = data.__testMode
            ? '<span class="npnp-test-badge">' + escapeHtml(t(data, 'user.modal.testBadge', 'Test mode')) + '</span>'
            : '';
        banner.innerHTML = '<span>' + testBadge + escapeHtml(msg) + '</span>'
            + '<button type="button">' + escapeHtml(t(data, 'user.modal.payNow', 'Pay now')) + '</button>';
        banner.querySelector('button').addEventListener('click', function () { openModal(data); });
        document.body.appendChild(banner);
        document.body.classList.add('npnp-has-banner');
        positionBanner();
    }

    // Push the banner just below Jellyfin's fixed header and reserve space for it
    // in the page content so it never overlaps the user's view (mobile included).
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

        // Most reliable anchor across Jellyfin versions: the search button.
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

    function refresh() {
        fetchMe().then(function (raw) {
            var normalized = normalizeMe(raw);
            var data = applyTestOverride(normalized);
            lastData = data;
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
        }).catch(function () {
            // anonymous user: ignore.
        });
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
