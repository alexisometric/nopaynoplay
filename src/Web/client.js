/* NoPayNoPlay - client injecté dans jellyfin-web. */
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
        if (!api) return Promise.reject(new Error('ApiClient indisponible'));
        var url = api.getUrl('NoPayNoPlay/Me');
        return api.ajax({ type: 'GET', url: url, dataType: 'json' });
    }

    function formatDate(iso) {
        try {
            var d = new Date(iso);
            return new Intl.DateTimeFormat('fr-FR', {
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
            ibanBlock = '<div class="row">Virement (RIB / IBAN) :</div>'
                + '<div class="npnp-iban" id="npnp-iban-text">' + escapeHtml(data.ibanText) + '</div>'
                + '<button id="npnp-copy-iban" type="button">Copier l\'IBAN</button>';
        }

        var note = data.customNote
            ? '<div class="row" style="opacity:.8;margin-top:12px;">' + escapeHtml(data.customNote) + '</div>'
            : '';

        var stateLabel = ({
            Ok: 'À jour',
            WarningSoon: 'Échéance proche',
            InGrace: 'En période de grâce',
            Blocked: 'Bloqué',
            Exempt: 'Accès gratuit'
        })[data.state] || data.state;

        var html = ''
            + '<div class="npnp-modal-backdrop" id="npnp-modal-backdrop">'
            + '  <div class="npnp-modal" role="dialog" aria-modal="true" aria-label="Abonnement NoPayNoPlay">'
            + '    <button class="close" type="button" aria-label="Fermer">&times;</button>'
            + '    <h2>Mon abonnement</h2>'
            + '    <div class="row">État : <strong style="color:' + (STATE_COLORS[data.state] || '#fff') + '">' + stateLabel + '</strong></div>'
            + '    <div class="row">Prochaine échéance : <strong>' + formatDate(data.expiryDate) + '</strong></div>'
            + '    <div class="row">Montant : <strong>' + Number(data.price).toFixed(2) + ' ' + escapeHtml(data.currency || 'EUR') + '</strong></div>'
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
                copyBtn.textContent = 'Copié !';
                setTimeout(function () { copyBtn.textContent = 'Copier l\'IBAN'; }, 1500);
            });
        }
    }

    function escapeHtml(s) {
        return String(s).replace(/[&<>"']/g, function (c) {
            return ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', '\'': '&#39;' })[c];
        });
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

        var msg;
        if (data.state === 'WarningSoon') {
            msg = 'Ton abonnement expire le ' + formatDate(data.expiryDate)
                + ' (dans ' + data.daysLeft + ' jour' + (data.daysLeft > 1 ? 's' : '') + ').';
        } else if (data.state === 'InGrace') {
            msg = 'Abonnement expiré depuis le ' + formatDate(data.expiryDate)
                + ' — période de grâce en cours.';
        } else {
            msg = 'Lecture désactivée : abonnement expiré le ' + formatDate(data.expiryDate) + '.';
        }

        var banner = document.createElement('div');
        banner.id = 'npnp-banner';
        banner.style.background = STATE_COLORS[data.state] || '#e67e22';
        banner.innerHTML = '<span>' + escapeHtml(msg) + '</span>'
            + '<button type="button">Régler maintenant</button>';
        banner.querySelector('button').addEventListener('click', function () { openModal(data); });
        document.body.insertBefore(banner, document.body.firstChild);
    }

    function ensureHeaderButton(data) {
        var headers = document.querySelectorAll('.headerRight, .skinHeader .headerRight');
        if (!headers.length) return false;
        var anyAdded = false;
        headers.forEach(function (header) {
            if (header.querySelector('.npnp-header-btn')) return;
            var btn = document.createElement('button');
            btn.type = 'button';
            btn.className = 'npnp-header-btn paper-icon-button-light';
            btn.title = 'Mon abonnement';
            btn.setAttribute('aria-label', 'Mon abonnement');
            btn.innerHTML = '<span class="material-icons" aria-hidden="true">monetization_on</span>';
            btn.addEventListener('click', function () { openModal(data); });
            // Insère avant le bouton de recherche si présent.
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
                // Pas d'UI de paiement pour les exemptés.
                var b = document.getElementById('npnp-banner');
                if (b) b.parentNode.removeChild(b);
                return;
            }
            ensureHeaderButton(data);
            ensureBanner(data);
        }).catch(function () {
            // utilisateur non connecté : ignore.
        });
    }

    // Polling doux pour réinjecter le bouton après navigation SPA.
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
