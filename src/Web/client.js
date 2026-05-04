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

    function getApiClient() {
        try {
            if (window.ApiClient) return window.ApiClient;
            if (window.connectionManager && window.connectionManager.currentApiClient) {
                return window.connectionManager.currentApiClient();
            }
        } catch (_) {}
        return null;
    }

    function fetchMe() {
        var api = getApiClient();
        if (!api) return Promise.reject(new Error('ApiClient unavailable'));
        var url = api.getUrl('NoPayNoPlay/Me');
        return api.ajax({ type: 'GET', url: url, dataType: 'json' });
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
            + '#npnp-banner{position:sticky;top:0;left:0;right:0;z-index:9999;'
            + 'padding:10px 16px;font:600 14px/1.4 system-ui,sans-serif;color:#fff;'
            + 'display:flex;align-items:center;justify-content:space-between;gap:12px;}'
            + '#npnp-banner button{background:rgba(255,255,255,.2);color:#fff;border:1px solid #fff;'
            + 'padding:6px 12px;border-radius:4px;cursor:pointer;font-weight:600;}'
            + '#npnp-banner button:hover{background:rgba(255,255,255,.35);} '
            + '.npnp-header-btn{background:transparent;border:none;color:inherit;cursor:pointer;'
            + 'padding:8px;display:inline-flex;align-items:center;justify-content:center;}'
            + '.npnp-header-btn .material-icons{font-size:24px;}'
            + '.npnp-modal-backdrop{position:fixed;inset:0;background:rgba(0,0,0,.6);'
            + 'z-index:10000;display:flex;align-items:center;justify-content:center;}'
            + '.npnp-modal{background:#202020;color:#fff;padding:24px;border-radius:8px;'
            + 'min-width:320px;max-width:90vw;font:14px/1.5 system-ui,sans-serif;'
            + 'box-shadow:0 10px 40px rgba(0,0,0,.5);}'
            + '.npnp-modal h2{margin:0 0 12px;font-size:18px;}'
            + '.npnp-modal .row{margin:6px 0;}'
            + '.npnp-modal .actions{display:flex;flex-wrap:wrap;gap:8px;margin-top:16px;}'
            + '.npnp-modal .actions a,.npnp-modal .actions button{'
            + 'background:#00a4dc;color:#fff;text-decoration:none;padding:8px 14px;'
            + 'border-radius:4px;font-weight:600;border:none;cursor:pointer;display:inline-block;}'
            + '.npnp-modal .actions a:hover,.npnp-modal .actions button:hover{background:#0088b8;}'
            + '.npnp-modal .close{float:right;cursor:pointer;background:none;border:none;color:#fff;'
            + 'font-size:20px;line-height:1;}'
            + '.npnp-iban{background:#111;padding:8px;border-radius:4px;font-family:monospace;'
            + 'word-break:break-all;margin:6px 0;}';
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
        return String(s).replace(/[&<>"']/g, function (c) {
            return ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', '\'': '&#39;' })[c];
        });
    }

    function openModal(data) {
        ensureStyles();
        closeModal();

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

        var html = ''
            + '<div class="npnp-modal-backdrop" id="npnp-modal-backdrop">'
            + '  <div class="npnp-modal" role="dialog" aria-modal="true" aria-label="' + escapeHtml(modalTitle) + '">'
            + '    <button class="close" type="button" aria-label="' + escapeHtml(t(data, 'user.modal.close', 'Close')) + '">&times;</button>'
            + '    <h2>' + escapeHtml(modalTitle) + '</h2>'
            + '    <div class="row">' + escapeHtml(t(data, 'user.modal.state', 'Status:')) + ' <strong style="color:' + (STATE_COLORS[data.state] || '#fff') + '">' + escapeHtml(stateLabel) + '</strong></div>'
            + '    <div class="row">' + escapeHtml(t(data, 'user.modal.nextDue', 'Next due date:')) + ' <strong>' + formatDate(data.expiryDate, data.lang) + '</strong></div>'
            + '    <div class="row">' + escapeHtml(t(data, 'user.modal.amount', 'Amount:')) + ' <strong>' + Number(data.price).toFixed(2) + ' ' + escapeHtml(data.currency || 'EUR') + '</strong></div>'
            + '    <div class="actions">' + actions.join('') + '</div>'
            + ibanBlock
            + note
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
        banner.innerHTML = '<span>' + escapeHtml(msg) + '</span>'
            + '<button type="button">' + escapeHtml(t(data, 'user.modal.payNow', 'Pay now')) + '</button>';
        banner.querySelector('button').addEventListener('click', function () { openModal(data); });
        document.body.insertBefore(banner, document.body.firstChild);
    }

    function ensureHeaderButton(data) {
        var headers = document.querySelectorAll('.headerRight, .skinHeader .headerRight');
        if (!headers.length) return false;
        var anyAdded = false;
        var label = t(data, 'user.modal.headerButton', 'My subscription');
        headers.forEach(function (header) {
            if (header.querySelector('.npnp-header-btn')) return;
            var btn = document.createElement('button');
            btn.type = 'button';
            btn.className = 'npnp-header-btn paper-icon-button-light';
            btn.title = label;
            btn.setAttribute('aria-label', label);
            btn.innerHTML = '<span class="material-icons" aria-hidden="true">monetization_on</span>';
            btn.addEventListener('click', function () { openModal(data); });
            // Insert before the search button if present.
            var search = header.querySelector('.headerSearchButton, [is="emby-button"][title="Search"]');
            if (search) header.insertBefore(btn, search);
            else header.insertBefore(btn, header.firstChild);
            anyAdded = true;
        });
        return anyAdded;
    }

    var lastData = null;

    function refresh() {
        fetchMe().then(function (data) {
            lastData = data;
            if (data.state === 'Exempt') {
                // No payment UI for exempt users.
                var b = document.getElementById('npnp-banner');
                if (b) b.parentNode.removeChild(b);
                return;
            }
            ensureHeaderButton(data);
            ensureBanner(data);
        }).catch(function () {
            // anonymous user: ignore.
        });
    }

    // Soft polling to re-inject the button after SPA navigation.
    function tick() {
        if (lastData && lastData.state !== 'Exempt') {
            ensureHeaderButton(lastData);
        }
    }

    function onReady() {
        refresh();
        setInterval(tick, 2000);
        setInterval(refresh, 5 * 60 * 1000);
        document.addEventListener('viewshow', refresh);
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', onReady);
    } else {
        onReady();
    }
})();
