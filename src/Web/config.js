/* NoPayNoPlay admin configuration page. */
(function () {
    'use strict';

    var i18n = { lang: 'en', strings: {}, available: ['en'] };
    var state = {
        users: [],
        activity: [],
        settings: { MonthlyPrice: 10, Currency: 'EUR' },
        sortKey: 'Username',
        sortDir: 1,
        userFilter: '',
        stateFilter: '',
        activityFilter: '',
        diagnostics: null
    };

    var STATE_COLORS = {
        Ok: '#2ecc71',
        WarningSoon: '#f39c12',
        InGrace: '#e67e22',
        Blocked: '#e74c3c',
        Exempt: '#95a5a6'
    };

    function api() { return window.ApiClient; }

    function detectLang() {
        try {
            var docLang = document.documentElement && document.documentElement.lang;
            if (docLang) return String(docLang).split('-')[0].toLowerCase();
        } catch (_) {}
        return null;
    }

    function urlWithLang(path) {
        var base = api().getUrl(path);
        var lang = detectLang();
        if (!lang) return base;
        return base + (base.indexOf('?') >= 0 ? '&' : '?') + 'lang=' + encodeURIComponent(lang);
    }

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

    function escapeHtml(s) {
        return String(s == null ? '' : s).replace(/[&<>"']/g, function (c) {
            return ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', '\'': '&#39;' })[c];
        });
    }

    function lcfirst(s) { return s ? s.charAt(0).toLowerCase() + s.slice(1) : s; }

    function loadStrings() {
        return api().ajax({
            type: 'GET', url: urlWithLang('NoPayNoPlay/Strings'), dataType: 'json'
        }).then(function (data) {
            i18n.lang = data.lang || 'en';
            i18n.strings = data.strings || {};
            i18n.available = data.available || ['en'];
        }).catch(function () { /* ignore */ });
    }

    function applyStaticI18n(page) {
        page.querySelectorAll('[data-i18n]').forEach(function (el) {
            var key = el.getAttribute('data-i18n');
            var fallback = el.textContent;
            el.textContent = t(key, fallback);
        });
    }

    function flash(page, message, isError) {
        var el = page.querySelector('#npnpFlash');
        if (!el) return;
        el.textContent = message;
        el.classList.toggle('error', !!isError);
        el.classList.add('show');
        clearTimeout(flash._timer);
        flash._timer = setTimeout(function () { el.classList.remove('show'); }, 3500);
    }

    function fillCultureSelect(page) {
        var sel = page.querySelector('#npnpUiCulture');
        if (!sel) return;
        var cur = sel.value;
        var options = ['<option value="">' + escapeHtml(t('admin.settings.uiCulture.auto', 'Auto')) + '</option>'];
        i18n.available.slice().sort().forEach(function (code) {
            options.push('<option value="' + escapeHtml(code) + '">' + escapeHtml(code) + '</option>');
        });
        sel.innerHTML = options.join('');
        if (cur) sel.value = cur;
    }

    function loadSettings(page) {
        return api().ajax({
            type: 'GET', url: api().getUrl('NoPayNoPlay/Settings'), dataType: 'json'
        }).then(function (cfg) {
            state.settings = cfg;
            page.querySelector('#npnpPrice').value = cfg.MonthlyPrice;
            page.querySelector('#npnpCurrency').value = cfg.Currency || 'EUR';
            page.querySelector('#npnpGrace').value = cfg.GraceDays;
            page.querySelector('#npnpTrial').value = cfg.TrialDays;
            page.querySelector('#npnpWarning').value = cfg.WarningDaysBefore;
            page.querySelector('#npnpPaypal').value = cfg.PaypalMeUrl || '';
            page.querySelector('#npnpLydia').value = cfg.LydiaUrl || '';
            page.querySelector('#npnpIban').value = cfg.IbanText || '';
            page.querySelector('#npnpNote').value = cfg.CustomNote || '';
            fillCultureSelect(page);
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
            state.users = users || [];
            renderUsers(page);
        });
    }

    function loadActivity(page) {
        return api().ajax({
            type: 'GET', url: api().getUrl('NoPayNoPlay/Activity'), dataType: 'json'
        }).then(function (rows) {
            state.activity = rows || [];
            renderActivity(page);
        });
    }

    function loadStatus(page) {
        return api().ajax({
            type: 'GET', url: api().getUrl('NoPayNoPlay/Status'), dataType: 'json'
        }).then(function (s) {
            var el = page.querySelector('#npnpStatus');
            if (!el) return;
            if (s && s.fileTransformationRegistered) {
                el.className = 'npnp-status ok';
                el.innerHTML = '<span class="material-icons" aria-hidden="true">check_circle</span>'
                    + '<span>' + escapeHtml(t('admin.status.ftOk', 'File Transformation OK.')) + '</span>';
            } else {
                el.className = 'npnp-status warn';
                el.innerHTML = '<span class="material-icons" aria-hidden="true">warning</span>'
                    + '<span>' + escapeHtml(t('admin.status.ftMissing', 'File Transformation plugin not detected.')) + '</span>';
            }
            el.style.display = '';
        }).catch(function () { /* ignore */ });
    }

    function loadDiagnostics(page) {
        return api().ajax({
            type: 'GET', url: api().getUrl('NoPayNoPlay/Diagnostics'), dataType: 'json'
        }).then(function (d) {
            state.diagnostics = d || null;
            renderDiagnostics(page);
        }).catch(function () {
            state.diagnostics = null;
            renderDiagnostics(page);
        });
    }

    function renderDiagnostics(page) {
        var badge = page.querySelector('#npnpDiagBadge');
        var body = page.querySelector('#npnpDiagBody');
        if (!badge || !body) return;
        var d = state.diagnostics;
        if (!d) {
            badge.style.display = '';
            badge.className = 'npnp-status warn';
            badge.innerHTML = '<span class="material-icons" aria-hidden="true">warning</span><span>'
                + escapeHtml(t('admin.diag.none', 'No diagnostics available yet.')) + '</span>';
            body.innerHTML = '';
            return;
        }
        var ok = !!d.registered;
        badge.style.display = '';
        badge.className = 'npnp-status ' + (ok ? 'ok' : 'warn');
        badge.innerHTML = '<span class="material-icons" aria-hidden="true">' + (ok ? 'check_circle' : 'warning') + '</span>'
            + '<span>' + escapeHtml(ok
                ? t('admin.diag.ok', 'index.html transformation is registered.')
                : t('admin.diag.ko', 'index.html transformation is NOT registered — user UI will not appear.')) + '</span>';

        function row(labelKey, fallback, value) {
            return '<tr><th style="width:220px;">' + escapeHtml(t(labelKey, fallback)) + '</th>'
                + '<td><code style="word-break:break-all;">' + escapeHtml(value || '—') + '</code></td></tr>';
        }
        var ack = d.needsTransformationAck;
        var ackTxt = ack === true ? t('admin.diag.ack.yes', 'Yes')
            : ack === false ? t('admin.diag.ack.no', 'No')
                : t('admin.diag.ack.unknown', 'Unknown');
        var matchList = (d.matchingAssemblies && d.matchingAssemblies.length)
            ? d.matchingAssemblies.map(function (a) { return escapeHtml(a); }).join('<br>')
            : '<em style="opacity:.6;">' + escapeHtml(t('admin.diag.matching.empty', 'No assembly matched ".FileTransformation".')) + '</em>';
        var notesList = (d.notes && d.notes.length)
            ? '<ul style="margin:0; padding-left:18px;">' + d.notes.map(function (n) {
                return '<li>' + escapeHtml(n) + '</li>';
            }).join('') + '</ul>'
            : '<em style="opacity:.6;">' + escapeHtml(t('admin.diag.notes.empty', 'No notes.')) + '</em>';

        body.innerHTML = '<table class="npnp-table">'
            + row('admin.diag.timestamp', 'Last attempt', d.timestamp ? formatDateTime(d.timestamp) : '')
            + row('admin.diag.registered', 'Registered', ok ? t('admin.diag.ack.yes', 'Yes') : t('admin.diag.ack.no', 'No'))
            + row('admin.diag.foundAssembly', 'Found FT assembly', d.foundAssembly)
            + row('admin.diag.callbackAssembly', 'Our callback assembly', d.callbackAssembly)
            + row('admin.diag.callbackClass', 'Callback class', d.callbackClass)
            + row('admin.diag.callbackMethod', 'Callback method', d.callbackMethod)
            + row('admin.diag.ack', 'FT acknowledged pattern', ackTxt)
            + '<tr><th>' + escapeHtml(t('admin.diag.matching', 'Matching assemblies')) + '</th><td>' + matchList + '</td></tr>'
            + '<tr><th>' + escapeHtml(t('admin.diag.notes', 'Notes')) + '</th><td>' + notesList + '</td></tr>'
            + '</table>';
    }

    function formatDate(iso) {
        try { return new Intl.DateTimeFormat(i18n.lang || 'en', { day: '2-digit', month: '2-digit', year: 'numeric' }).format(new Date(iso)); }
        catch (_) { return iso; }
    }

    function formatDateTime(iso) {
        try { return new Intl.DateTimeFormat(i18n.lang || 'en', { dateStyle: 'short', timeStyle: 'short' }).format(new Date(iso)); }
        catch (_) { return iso; }
    }

    function stateLabel(stateName, isExempt) {
        if (isExempt) return t('state.exempt', 'Exempt');
        return t('state.' + lcfirst(stateName), stateName);
    }

    function getSortValue(u, key) {
        switch (key) {
            case 'Username': return (u.Username || '').toLowerCase();
            case 'State': return u.IsExempt ? 'Exempt' : (u.State || '');
            case 'ExpiryDate': return new Date(u.ExpiryDate).getTime() || 0;
            case 'DaysLeft': return u.DaysLeft || 0;
            case 'LastPayment': {
                var tx = (u.Transactions || [])[0];
                return tx ? new Date(tx.Date).getTime() : 0;
            }
            default: return '';
        }
    }

    function filterUsers() {
        var q = state.userFilter.toLowerCase();
        var sf = state.stateFilter;
        return state.users.filter(function (u) {
            if (q && (u.Username || '').toLowerCase().indexOf(q) < 0) return false;
            if (sf) {
                var s = u.IsExempt ? 'Exempt' : u.State;
                if (s !== sf) return false;
            }
            return true;
        }).sort(function (a, b) {
            var av = getSortValue(a, state.sortKey);
            var bv = getSortValue(b, state.sortKey);
            if (av < bv) return -1 * state.sortDir;
            if (av > bv) return 1 * state.sortDir;
            return 0;
        });
    }

    function arrowFor(key) {
        if (state.sortKey !== key) return '';
        return '<span class="npnp-sort-arrow">' + (state.sortDir > 0 ? '▲' : '▼') + '</span>';
    }

    function renderSummary(page) {
        var counts = { Ok: 0, WarningSoon: 0, InGrace: 0, Blocked: 0, Exempt: 0 };
        state.users.forEach(function (u) {
            var k = u.IsExempt ? 'Exempt' : (u.State || 'Ok');
            counts[k] = (counts[k] || 0) + 1;
        });
        var summary = page.querySelector('#npnpUsersSummary');
        if (!summary) return;
        var cards = ['Ok', 'WarningSoon', 'InGrace', 'Blocked', 'Exempt'].map(function (k) {
            return '<button type="button" class="npnp-stat" data-state="' + k + '" style="--c:' + STATE_COLORS[k] + '">'
                + '<span class="npnp-stat-num">' + counts[k] + '</span>'
                + '<span class="npnp-stat-lbl">' + escapeHtml(t('state.' + lcfirst(k), k)) + '</span>'
                + '</button>';
        }).join('');
        summary.innerHTML = cards;
        summary.querySelectorAll('.npnp-stat').forEach(function (b) {
            b.addEventListener('click', function () {
                var s = b.getAttribute('data-state');
                var sel = page.querySelector('#npnpStateFilter');
                state.stateFilter = (state.stateFilter === s) ? '' : s;
                if (sel) sel.value = state.stateFilter;
                renderUsers(page);
            });
        });
        summary.querySelectorAll('.npnp-stat').forEach(function (b) {
            b.classList.toggle('active', b.getAttribute('data-state') === state.stateFilter);
        });
    }

    function renderUsers(page) {
        renderSummary(page);
        var rows = filterUsers();
        var container = page.querySelector('#npnpUsersTable');

        if (!rows.length) {
            var msg = (state.userFilter || state.stateFilter)
                ? t('admin.users.empty.filter', 'No member matches the current filter.')
                : t('admin.users.empty', 'No member to display.');
            container.innerHTML = '<div class="npnp-empty">' + escapeHtml(msg) + '</div>';
            return;
        }

        var html = '<table class="npnp-table">'
            + '<thead><tr>'
            + '<th data-sort="Username">' + escapeHtml(t('admin.users.col.user', 'User')) + arrowFor('Username') + '</th>'
            + '<th data-sort="State">' + escapeHtml(t('admin.users.col.state', 'State')) + arrowFor('State') + '</th>'
            + '<th data-sort="ExpiryDate">' + escapeHtml(t('admin.users.col.expiry', 'Expiry')) + arrowFor('ExpiryDate') + '</th>'
            + '<th data-sort="DaysLeft">' + escapeHtml(t('admin.users.col.daysLeft', 'Days left')) + arrowFor('DaysLeft') + '</th>'
            + '<th data-sort="LastPayment">' + escapeHtml(t('admin.users.col.lastPayment', 'Last payment')) + arrowFor('LastPayment') + '</th>'
            + '<th>' + escapeHtml(t('admin.users.col.actions', 'Actions')) + '</th>'
            + '</tr></thead><tbody>';

        rows.forEach(function (u) {
            var color = u.IsExempt ? STATE_COLORS.Exempt : (STATE_COLORS[u.State] || '#888');
            var label = stateLabel(u.State, u.IsExempt);
            var lastTx = (u.Transactions || [])[0];
            var lastLabel = lastTx ? formatDate(lastTx.Date) : t('admin.users.never', 'Never');
            var actExempt = u.IsExempt
                ? t('admin.users.action.unexempt', 'Remove exemption')
                : t('admin.users.action.exempt', 'Mark exempt');

            html += '<tr data-userid="' + escapeHtml(u.UserId) + '">'
                + '<td>' + escapeHtml(u.Username) + '</td>'
                + '<td><span class="npnp-state-pill" style="background:' + color + '">' + escapeHtml(label) + '</span></td>'
                + '<td>' + escapeHtml(formatDate(u.ExpiryDate)) + '</td>'
                + '<td>' + escapeHtml(String(u.DaysLeft)) + '</td>'
                + '<td>' + escapeHtml(lastLabel) + '</td>'
                + '<td><div class="npnp-actions">'
                + '<button is="emby-button" type="button" class="raised npnp-pay" title="' + escapeHtml(t('admin.users.action.pay', 'Record payment')) + '"><span class="material-icons" aria-hidden="true">payments</span></button>'
                + '<button is="emby-button" type="button" class="npnp-history" title="' + escapeHtml(t('admin.users.action.history', 'History')) + '"><span class="material-icons" aria-hidden="true">history</span></button>'
                + '<button is="emby-button" type="button" class="npnp-exempt" title="' + escapeHtml(actExempt) + '"><span class="material-icons" aria-hidden="true">' + (u.IsExempt ? 'lock' : 'lock_open') + '</span></button>'
                + '<button is="emby-button" type="button" class="npnp-reset npnp-danger" title="' + escapeHtml(t('admin.users.action.reset', 'Reset trial')) + '"><span class="material-icons" aria-hidden="true">restart_alt</span></button>'
                + '</div></td>'
                + '</tr>';
        });

        html += '</tbody></table>';
        container.innerHTML = html;

        container.querySelectorAll('th[data-sort]').forEach(function (th) {
            th.addEventListener('click', function () {
                var key = th.getAttribute('data-sort');
                if (state.sortKey === key) {
                    state.sortDir = -state.sortDir;
                } else {
                    state.sortKey = key;
                    state.sortDir = 1;
                }
                renderUsers(page);
            });
        });

        container.querySelectorAll('tr[data-userid]').forEach(function (tr) {
            var userId = tr.getAttribute('data-userid');
            var user = state.users.find(function (x) { return x.UserId === userId; });
            tr.querySelector('.npnp-pay').addEventListener('click', function () { openPaymentModal(page, user); });
            tr.querySelector('.npnp-history').addEventListener('click', function () { openHistoryModal(page, user); });
            tr.querySelector('.npnp-exempt').addEventListener('click', function () {
                api().ajax({
                    type: 'POST',
                    url: api().getUrl('NoPayNoPlay/Users/' + userId + '/Exempt'),
                    data: JSON.stringify({ IsExempt: !user.IsExempt }),
                    contentType: 'application/json'
                }).then(function () { return loadUsers(page); });
            });
            tr.querySelector('.npnp-reset').addEventListener('click', function () {
                openConfirmModal(page,
                    t('admin.users.action.reset', 'Reset trial'),
                    t('admin.users.confirm.reset', 'Reset this user back to a fresh trial?'),
                    function () {
                        api().ajax({
                            type: 'POST',
                            url: api().getUrl('NoPayNoPlay/Users/' + userId + '/Reset')
                        }).then(function () { return loadUsers(page); });
                    });
            });
        });
    }

    function renderActivity(page) {
        var q = state.activityFilter.toLowerCase();
        var rows = state.activity.filter(function (r) {
            if (!q) return true;
            return (r.Username || '').toLowerCase().indexOf(q) >= 0
                || (r.Method || '').toLowerCase().indexOf(q) >= 0
                || (r.AdminNote || '').toLowerCase().indexOf(q) >= 0;
        });
        var container = page.querySelector('#npnpActivityTable');
        if (!rows.length) {
            container.innerHTML = '<div class="npnp-empty">' + escapeHtml(t('admin.activity.empty', 'No activity yet.')) + '</div>';
            return;
        }
        var html = '<table class="npnp-table">'
            + '<thead><tr>'
            + '<th>' + escapeHtml(t('admin.activity.col.date', 'Date')) + '</th>'
            + '<th>' + escapeHtml(t('admin.activity.col.user', 'User')) + '</th>'
            + '<th>' + escapeHtml(t('admin.activity.col.amount', 'Amount')) + '</th>'
            + '<th>' + escapeHtml(t('admin.activity.col.method', 'Method')) + '</th>'
            + '<th>' + escapeHtml(t('admin.activity.col.months', 'Months')) + '</th>'
            + '<th>' + escapeHtml(t('admin.activity.col.note', 'Note')) + '</th>'
            + '</tr></thead><tbody>';
        rows.forEach(function (r) {
            html += '<tr>'
                + '<td>' + escapeHtml(formatDateTime(r.Date)) + '</td>'
                + '<td>' + escapeHtml(r.Username || '') + '</td>'
                + '<td>' + escapeHtml(Number(r.Amount || 0).toFixed(2)) + '</td>'
                + '<td>' + escapeHtml(r.Method || '') + '</td>'
                + '<td>' + escapeHtml(String(r.MonthsAdded || 0)) + '</td>'
                + '<td>' + escapeHtml(r.AdminNote || '') + '</td>'
                + '</tr>';
        });
        html += '</tbody></table>';
        container.innerHTML = html;
    }

    /* --- modals --- */

    function closeModal(page) {
        var root = page.querySelector('#npnpModalRoot');
        if (root) root.innerHTML = '';
    }

    function buildModal(page, titleHtml, bodyHtml, footerHtml) {
        var root = page.querySelector('#npnpModalRoot');
        root.innerHTML = ''
            + '<div class="npnp-modal-backdrop" data-npnp-backdrop>'
            + '  <div class="npnp-modal" role="dialog" aria-modal="true">'
            + '    <button type="button" class="close" aria-label="' + escapeHtml(t('common.close', 'Close')) + '" data-npnp-close>&times;</button>'
            + '    <h2>' + titleHtml + '</h2>'
            + '    <div>' + bodyHtml + '</div>'
            + (footerHtml ? '<div class="modal-actions">' + footerHtml + '</div>' : '')
            + '  </div>'
            + '</div>';
        var backdrop = root.querySelector('[data-npnp-backdrop]');
        backdrop.addEventListener('click', function (e) {
            if (e.target === backdrop) closeModal(page);
        });
        root.querySelector('[data-npnp-close]').addEventListener('click', function () { closeModal(page); });
        return root.querySelector('.npnp-modal');
    }

    function openConfirmModal(page, title, message, onConfirm) {
        var modal = buildModal(page,
            escapeHtml(title),
            '<p>' + escapeHtml(message) + '</p>',
            '<button is="emby-button" type="button" data-npnp-cancel>' + escapeHtml(t('common.cancel', 'Cancel')) + '</button>'
            + '<button is="emby-button" type="button" class="raised button-submit" data-npnp-confirm>OK</button>');
        modal.querySelector('[data-npnp-cancel]').addEventListener('click', function () { closeModal(page); });
        modal.querySelector('[data-npnp-confirm]').addEventListener('click', function () {
            closeModal(page);
            onConfirm();
        });
    }

    function openHistoryModal(page, user) {
        var tx = user.Transactions || [];
        var body;
        if (!tx.length) {
            body = '<div class="npnp-empty">' + escapeHtml(t('admin.users.history.empty', 'No payment recorded yet.')) + '</div>';
        } else {
            var rows = tx.map(function (e) {
                return '<tr>'
                    + '<td>' + escapeHtml(formatDateTime(e.Date)) + '</td>'
                    + '<td>' + escapeHtml(Number(e.Amount || 0).toFixed(2)) + '</td>'
                    + '<td>' + escapeHtml(e.Method || '') + '</td>'
                    + '<td>' + escapeHtml(String(e.MonthsAdded || 0)) + '</td>'
                    + '<td>' + escapeHtml(e.AdminNote || '') + '</td>'
                    + '</tr>';
            }).join('');
            body = '<table class="npnp-table">'
                + '<thead><tr>'
                + '<th>' + escapeHtml(t('admin.activity.col.date', 'Date')) + '</th>'
                + '<th>' + escapeHtml(t('admin.activity.col.amount', 'Amount')) + '</th>'
                + '<th>' + escapeHtml(t('admin.activity.col.method', 'Method')) + '</th>'
                + '<th>' + escapeHtml(t('admin.activity.col.months', 'Months')) + '</th>'
                + '<th>' + escapeHtml(t('admin.activity.col.note', 'Note')) + '</th>'
                + '</tr></thead><tbody>' + rows + '</tbody></table>';
        }
        var title = format(t('admin.users.history.title', 'Payment history — {username}'), { username: user.Username });
        buildModal(page, escapeHtml(title), body, '');
    }

    function openPaymentModal(page, user) {
        var methods = [
            ['PayPal', t('admin.payment.method.paypal', 'PayPal')],
            ['Lydia', t('admin.payment.method.lydia', 'Lydia')],
            ['Bank', t('admin.payment.method.bank', 'Bank transfer')],
            ['Cash', t('admin.payment.method.cash', 'Cash')],
            ['Other', t('admin.payment.method.other', 'Other')]
        ];
        var optionsHtml = methods.map(function (m) {
            return '<option value="' + escapeHtml(m[0]) + '">' + escapeHtml(m[1]) + '</option>';
        }).join('');

        var defaultAmount = Number(state.settings.MonthlyPrice || 10).toFixed(2);
        var currency = state.settings.Currency || 'EUR';

        var body = ''
            + '<form id="npnpPayForm">'
            + '<div class="inputContainer"><label>' + escapeHtml(t('admin.payment.user', 'Member')) + '</label>'
            + '<input is="emby-input" type="text" value="' + escapeHtml(user.Username) + '" disabled /></div>'
            + '<div class="inputContainer"><label for="npnpPayAmount">' + escapeHtml(t('admin.payment.amount', 'Amount')) + ' (' + escapeHtml(currency) + ')</label>'
            + '<input is="emby-input" id="npnpPayAmount" type="number" step="0.01" min="0" value="' + escapeHtml(defaultAmount) + '" required /></div>'
            + '<div class="inputContainer"><label for="npnpPayMethod">' + escapeHtml(t('admin.payment.method', 'Method')) + '</label>'
            + '<select is="emby-select" id="npnpPayMethod">' + optionsHtml + '</select></div>'
            + '<div class="inputContainer"><label for="npnpPayMonths">' + escapeHtml(t('admin.payment.months', 'Months to add')) + '</label>'
            + '<input is="emby-input" id="npnpPayMonths" type="number" min="1" max="60" value="1" required /></div>'
            + '<div class="inputContainer"><label for="npnpPayNote">' + escapeHtml(t('admin.payment.note', 'Note (optional)')) + '</label>'
            + '<textarea is="emby-textarea" id="npnpPayNote" rows="2"></textarea></div>'
            + '</form>';

        var footer = ''
            + '<button is="emby-button" type="button" data-npnp-cancel>' + escapeHtml(t('admin.payment.cancel', 'Cancel')) + '</button>'
            + '<button is="emby-button" type="button" class="raised button-submit" data-npnp-save>' + escapeHtml(t('admin.payment.save', 'Save')) + '</button>';

        var modal = buildModal(page, escapeHtml(t('admin.payment.title', 'Record a payment')), body, footer);
        modal.querySelector('[data-npnp-cancel]').addEventListener('click', function () { closeModal(page); });
        modal.querySelector('[data-npnp-save]').addEventListener('click', function () {
            var payload = {
                Amount: parseFloat(modal.querySelector('#npnpPayAmount').value || '0'),
                Method: modal.querySelector('#npnpPayMethod').value || 'Other',
                MonthsAdded: parseInt(modal.querySelector('#npnpPayMonths').value || '1', 10),
                Note: modal.querySelector('#npnpPayNote').value || ''
            };
            api().ajax({
                type: 'POST',
                url: api().getUrl('NoPayNoPlay/Users/' + user.UserId + '/Pay'),
                data: JSON.stringify(payload),
                contentType: 'application/json'
            }).then(function () {
                closeModal(page);
                flash(page, t('admin.users.payment.saved', 'Payment recorded.'));
                return Promise.all([loadUsers(page), loadActivity(page)]);
            });
        });
    }

    /* --- tabs --- */

    function bindTabs(page) {
        page.querySelectorAll('.npnp-tab-btn').forEach(function (btn) {
            btn.addEventListener('click', function () {
                var tab = btn.getAttribute('data-tab');
                page.querySelectorAll('.npnp-tab-btn').forEach(function (b) { b.classList.toggle('active', b === btn); });
                page.querySelectorAll('.npnp-tab-panel').forEach(function (p) {
                    p.classList.toggle('active', p.id === 'npnpTab-' + tab);
                });
                if (tab === 'activity') loadActivity(page);
                if (tab === 'diagnostics') loadDiagnostics(page);
            });
        });
    }

    function bindTestMode(page) {
        function refreshUrl() {
            var s = page.querySelector('#npnpTestState').value || 'warningSoon';
            var url = window.location.origin + '/web/index.html?npnpTest=' + encodeURIComponent(s);
            page.querySelector('#npnpTestUrl').value = url;
        }
        page.querySelector('#npnpTestState').addEventListener('change', refreshUrl);
        page.querySelector('#npnpTestPreview').addEventListener('click', function () {
            refreshUrl();
            window.open(page.querySelector('#npnpTestUrl').value, '_blank', 'noopener');
        });
        page.querySelector('#npnpTestCopy').addEventListener('click', function () {
            refreshUrl();
            var url = page.querySelector('#npnpTestUrl').value;
            if (navigator.clipboard) navigator.clipboard.writeText(url);
            flash(page, t('admin.test.copied', 'URL copied!'));
        });
        refreshUrl();
    }

    function bindFilters(page) {
        page.querySelector('#npnpUserSearch').addEventListener('input', function (e) {
            state.userFilter = e.target.value || '';
            renderUsers(page);
        });
        page.querySelector('#npnpStateFilter').addEventListener('change', function (e) {
            state.stateFilter = e.target.value || '';
            renderUsers(page);
        });
        page.querySelector('#npnpActivitySearch').addEventListener('input', function (e) {
            state.activityFilter = e.target.value || '';
            renderActivity(page);
        });
    }

    function bindDiagnostics(page) {
        var retryBtn = page.querySelector('#npnpDiagRetry');
        var refreshBtn = page.querySelector('#npnpDiagRefresh');
        var copyBtn = page.querySelector('#npnpDiagCopy');
        if (retryBtn) {
            retryBtn.addEventListener('click', function () {
                Dashboard.showLoadingMsg();
                api().ajax({
                    type: 'POST', url: api().getUrl('NoPayNoPlay/RetryRegistration'), dataType: 'json'
                }).then(function (resp) {
                    state.diagnostics = resp && resp.diagnostics ? resp.diagnostics : null;
                    renderDiagnostics(page);
                    flash(page, resp && resp.ok
                        ? t('admin.diag.retry.ok', 'Registration succeeded.')
                        : t('admin.diag.retry.ko', 'Registration failed — see notes below.'),
                        !(resp && resp.ok));
                    return loadStatus(page);
                }).finally(function () { Dashboard.hideLoadingMsg(); });
            });
        }
        if (refreshBtn) {
            refreshBtn.addEventListener('click', function () { loadDiagnostics(page); });
        }
        if (copyBtn) {
            copyBtn.addEventListener('click', function () {
                var json = JSON.stringify(state.diagnostics || {}, null, 2);
                if (navigator.clipboard) navigator.clipboard.writeText(json);
                flash(page, t('admin.diag.copied', 'Diagnostics copied to clipboard.'));
            });
        }
    }

    document.querySelectorAll('#NoPayNoPlayConfigPage').forEach(function (page) {
        page.addEventListener('pageshow', function () {
            Dashboard.showLoadingMsg();
            loadStrings()
                .then(function () { applyStaticI18n(page); })
                .then(function () { return Promise.all([loadSettings(page), loadUsers(page), loadActivity(page), loadStatus(page)]); })
                .finally(function () { Dashboard.hideLoadingMsg(); });
        });

        bindTabs(page);
        bindFilters(page);
        bindTestMode(page);
        bindDiagnostics(page);

        page.querySelector('#npnpSettingsForm').addEventListener('submit', function (e) {
            e.preventDefault();
            Dashboard.showLoadingMsg();
            saveSettings(page).then(function () {
                Dashboard.hideLoadingMsg();
                Dashboard.processPluginConfigurationUpdateResult();
                flash(page, t('admin.settings.saved', 'Settings saved.'));
                // Reload strings/users in case the language override changed.
                loadStrings().then(function () {
                    applyStaticI18n(page);
                    renderUsers(page);
                    renderActivity(page);
                });
            });
            return false;
        });
    });
})();
