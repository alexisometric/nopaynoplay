/* NoPayNoPlay admin configuration page. */
(function () {
    'use strict';

    var i18n = { lang: 'en', strings: {} };

    function api() { return window.ApiClient; }

    function t(key, fallback) {
        if (Object.prototype.hasOwnProperty.call(i18n.strings, key)) {
            return i18n.strings[key];
        }
        return fallback;
    }

    function format(template, tokens) {
        return String(template).replace(/\{(\w+)\}/g, function (_, k) {
            return Object.prototype.hasOwnProperty.call(tokens, k) ? tokens[k] : '';
        });
    }

    function loadStrings() {
        return api().ajax({
            type: 'GET', url: api().getUrl('NoPayNoPlay/Strings'), dataType: 'json'
        }).then(function (data) {
            i18n.lang = data.lang || 'en';
            i18n.strings = data.strings || {};
        }).catch(function () { /* ignore */ });
    }

    function applyStaticI18n(page) {
        page.querySelectorAll('[data-i18n]').forEach(function (el) {
            var key = el.getAttribute('data-i18n');
            var fallback = el.textContent;
            el.textContent = t(key, fallback);
        });
    }

    function stateColor(state) {
        return ({
            Ok: '#2ecc71',
            WarningSoon: '#f39c12',
            InGrace: '#e67e22',
            Blocked: '#e74c3c',
            Exempt: '#95a5a6'
        })[state] || '#888';
    }

    function lcfirst(s) { return s ? s.charAt(0).toLowerCase() + s.slice(1) : s; }

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
            var ui = page.querySelector('#npnpUiCulture');
            if (ui) ui.value = cfg.UiCultureOverride || '';
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
            CustomNote: page.querySelector('#npnpNote').value,
            UiCultureOverride: (page.querySelector('#npnpUiCulture') ? page.querySelector('#npnpUiCulture').value : '') || ''
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

    function stateLabel(state, isExempt) {
        if (isExempt) return t('state.exempt', 'Exempt');
        return t('state.' + lcfirst(state), state);
    }

    function renderUsers(page, users) {
        var rows = users.map(function (u) {
            var color = u.IsExempt ? stateColor('Exempt') : stateColor(u.State);
            var label = stateLabel(u.State, u.IsExempt);
            var expiry = new Date(u.ExpiryDate).toLocaleDateString(i18n.lang || 'en');
            var actPay = t('admin.users.action.pay', 'Record payment');
            var actExempt = u.IsExempt
                ? t('admin.users.action.unexempt', 'Remove exemption')
                : t('admin.users.action.exempt', 'Mark exempt');
            var actReset = t('admin.users.action.reset', 'Reset trial');

            return ''
                + '<tr data-userid="' + u.UserId + '">'
                + '<td><span style="display:inline-block;width:10px;height:10px;border-radius:50%;background:' + color + ';margin-right:6px;"></span>' + escapeHtml(u.Username) + '</td>'
                + '<td>' + escapeHtml(label) + '</td>'
                + '<td>' + expiry + '</td>'
                + '<td>' + u.DaysLeft + '</td>'
                + '<td>'
                + '<button is="emby-button" type="button" class="raised npnp-pay">' + escapeHtml(actPay) + '</button> '
                + '<button is="emby-button" type="button" class="npnp-exempt">' + escapeHtml(actExempt) + '</button> '
                + '<button is="emby-button" type="button" class="npnp-reset">' + escapeHtml(actReset) + '</button>'
                + '</td>'
                + '</tr>';
        }).join('');

        var html = ''
            + '<table style="width:100%;border-collapse:collapse;" class="detailTable">'
            + '<thead><tr>'
            + '<th style="text-align:left;padding:6px;">' + escapeHtml(t('admin.users.col.user', 'User')) + '</th>'
            + '<th style="text-align:left;padding:6px;">' + escapeHtml(t('admin.users.col.state', 'State')) + '</th>'
            + '<th style="text-align:left;padding:6px;">' + escapeHtml(t('admin.users.col.expiry', 'Expiry')) + '</th>'
            + '<th style="text-align:left;padding:6px;">' + escapeHtml(t('admin.users.col.daysLeft', 'Days left')) + '</th>'
            + '<th style="text-align:left;padding:6px;">' + escapeHtml(t('admin.users.col.actions', 'Actions')) + '</th>'
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
                if (!confirm(t('admin.users.confirm.reset', 'Reset this user to a fresh trial?'))) return;
                api().ajax({
                    type: 'POST',
                    url: api().getUrl('NoPayNoPlay/Users/' + userId + '/Reset')
                }).then(function () { loadUsers(page); });
            });
        });
    }

    function promptPayment(page, user) {
        var amount = prompt(t('admin.users.prompt.amount', 'Amount received?'), '10');
        if (amount === null) return;
        var method = prompt(t('admin.users.prompt.method', 'Method (PayPal, Lydia, Bank, Cash, Other)?'), 'PayPal') || '';
        var months = prompt(t('admin.users.prompt.months', 'Number of months to add?'), '1');
        if (months === null) return;
        var note = prompt(t('admin.users.prompt.note', 'Note (optional)?'), '') || '';

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
            Dashboard.alert(t('admin.users.payment.saved', 'Payment recorded.'));
            loadUsers(page);
        });
    }

    document.querySelectorAll('#NoPayNoPlayConfigPage').forEach(function (page) {
        page.addEventListener('pageshow', function () {
            Dashboard.showLoadingMsg();
            loadStrings()
                .then(function () { applyStaticI18n(page); })
                .then(function () { return Promise.all([loadSettings(page), loadUsers(page)]); })
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
