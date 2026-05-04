/* Page de configuration admin NoPayNoPlay. */
(function () {
    'use strict';

    var STATE_LABELS = {
        Ok: { label: 'À jour', color: '#2ecc71' },
        WarningSoon: { label: 'Échéance proche', color: '#f39c12' },
        InGrace: { label: 'Période de grâce', color: '#e67e22' },
        Blocked: { label: 'Bloqué', color: '#e74c3c' },
        Exempt: { label: 'Exempté', color: '#95a5a6' }
    };

    function api() { return window.ApiClient; }

    function loadSettings(page) {
        return api().ajax({
            type: 'GET', url: api().getUrl('NoPayNoPlay/Settings'), dataType: 'json'
        }).then(function (cfg) {
            page.querySelector('#npnpPrice').value = cfg.MonthlyPrice;
            page.querySelector('#npnpCurrency').value = cfg.Currency || 'EUR';
            page.querySelector('#npnpGrace').value = cfg.GraceDays;
            page.querySelector('#npnpTrial').value = cfg.TrialDays;
            page.querySelector('#npnpWarning').value = cfg.WarningDaysBefore;
            page.querySelector('#npnpPaypal').value = cfg.PaypalMeUrl || '';
            page.querySelector('#npnpLydia').value = cfg.LydiaUrl || '';
            page.querySelector('#npnpIban').value = cfg.IbanText || '';
            page.querySelector('#npnpNote').value = cfg.CustomNote || '';
        });
    }

    function saveSettings(page) {
        var body = {
            MonthlyPrice: parseFloat(page.querySelector('#npnpPrice').value || '0'),
            Currency: page.querySelector('#npnpCurrency').value || 'EUR',
            GraceDays: parseInt(page.querySelector('#npnpGrace').value || '0', 10),
            TrialDays: parseInt(page.querySelector('#npnpTrial').value || '0', 10),
            WarningDaysBefore: parseInt(page.querySelector('#npnpWarning').value || '0', 10),
            PaypalMeUrl: page.querySelector('#npnpPaypal').value,
            LydiaUrl: page.querySelector('#npnpLydia').value,
            IbanText: page.querySelector('#npnpIban').value,
            CustomNote: page.querySelector('#npnpNote').value
        };
        return api().ajax({
            type: 'POST',
            url: api().getUrl('NoPayNoPlay/Settings'),
            data: JSON.stringify(body),
            contentType: 'application/json'
        });
    }

    function loadUsers(page) {
        return api().ajax({
            type: 'GET', url: api().getUrl('NoPayNoPlay/Users'), dataType: 'json'
        }).then(function (users) {
            renderUsers(page, users);
        });
    }

    function escapeHtml(s) {
        return String(s == null ? '' : s).replace(/[&<>"']/g, function (c) {
            return ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', '\'': '&#39;' })[c];
        });
    }

    function renderUsers(page, users) {
        var rows = users.map(function (u) {
            var meta = STATE_LABELS[u.State] || { label: u.State, color: '#888' };
            var color = u.IsExempt ? STATE_LABELS.Exempt.color : meta.color;
            var label = u.IsExempt ? 'Exempté' : meta.label;
            var expiry = new Date(u.ExpiryDate).toLocaleDateString('fr-FR');
            return ''
                + '<tr data-userid="' + u.UserId + '">'
                + '<td><span style="display:inline-block;width:10px;height:10px;border-radius:50%;background:' + color + ';margin-right:6px;"></span>' + escapeHtml(u.Username) + '</td>'
                + '<td>' + escapeHtml(label) + '</td>'
                + '<td>' + expiry + '</td>'
                + '<td>' + u.DaysLeft + '</td>'
                + '<td>'
                + '<button is="emby-button" type="button" class="raised npnp-pay">Valider paiement</button> '
                + '<button is="emby-button" type="button" class="npnp-exempt">' + (u.IsExempt ? 'Retirer exemption' : 'Exempter') + '</button> '
                + '<button is="emby-button" type="button" class="npnp-reset">Reset essai</button>'
                + '</td>'
                + '</tr>';
        }).join('');

        var html = ''
            + '<table style="width:100%;border-collapse:collapse;" class="detailTable">'
            + '<thead><tr>'
            + '<th style="text-align:left;padding:6px;">Utilisateur</th>'
            + '<th style="text-align:left;padding:6px;">État</th>'
            + '<th style="text-align:left;padding:6px;">Échéance</th>'
            + '<th style="text-align:left;padding:6px;">Jours restants</th>'
            + '<th style="text-align:left;padding:6px;">Actions</th>'
            + '</tr></thead><tbody>' + rows + '</tbody></table>';

        var container = page.querySelector('#npnpUsersTable');
        container.innerHTML = html;

        container.querySelectorAll('tr[data-userid]').forEach(function (tr) {
            var userId = tr.getAttribute('data-userid');
            var user = users.find(function (x) { return x.UserId === userId; });

            tr.querySelector('.npnp-pay').addEventListener('click', function () {
                promptPayment(page, user);
            });
            tr.querySelector('.npnp-exempt').addEventListener('click', function () {
                api().ajax({
                    type: 'POST',
                    url: api().getUrl('NoPayNoPlay/Users/' + userId + '/Exempt'),
                    data: JSON.stringify({ IsExempt: !user.IsExempt }),
                    contentType: 'application/json'
                }).then(function () { loadUsers(page); });
            });
            tr.querySelector('.npnp-reset').addEventListener('click', function () {
                if (!confirm('Réinitialiser l\'utilisateur à un nouvel essai ?')) return;
                api().ajax({
                    type: 'POST',
                    url: api().getUrl('NoPayNoPlay/Users/' + userId + '/Reset')
                }).then(function () { loadUsers(page); });
            });
        });
    }

    function promptPayment(page, user) {
        var amount = prompt('Montant reçu (€) ?', '10');
        if (amount === null) return;
        var method = prompt('Méthode (PayPal, Lydia, Virement, Espèces, Autre) ?', 'PayPal') || '';
        var months = prompt('Nombre de mois à ajouter ?', '1');
        if (months === null) return;
        var note = prompt('Note (optionnel) ?', '') || '';

        api().ajax({
            type: 'POST',
            url: api().getUrl('NoPayNoPlay/Users/' + user.UserId + '/Pay'),
            data: JSON.stringify({
                Amount: parseFloat(amount) || 0,
                Method: method,
                MonthsAdded: parseInt(months, 10) || 1,
                Note: note
            }),
            contentType: 'application/json'
        }).then(function () {
            Dashboard.alert('Paiement enregistré.');
            loadUsers(page);
        });
    }

    document.querySelectorAll('#NoPayNoPlayConfigPage').forEach(function (page) {
        page.addEventListener('pageshow', function () {
            Dashboard.showLoadingMsg();
            Promise.all([loadSettings(page), loadUsers(page)])
                .finally(function () { Dashboard.hideLoadingMsg(); });
        });

        page.querySelector('#npnpSettingsForm').addEventListener('submit', function (e) {
            e.preventDefault();
            Dashboard.showLoadingMsg();
            saveSettings(page).then(function () {
                Dashboard.hideLoadingMsg();
                Dashboard.processPluginConfigurationUpdateResult();
            });
            return false;
        });
    });
})();
