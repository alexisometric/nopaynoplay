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
        methodFilter: '',
        activityFilter: '',
        activityFrom: '',
        activityTo: '',
        selected: {},
        stats: null,
        diagnostics: null,
        promoCodes: [],
        tiers: [],
        tags: [],
        audit: [],
        page: 1,
        pageSize: 50
    };

    var STATE_COLORS = {
        Ok: '#2ecc71',
        WarningSoon: '#f39c12',
        InGrace: '#e67e22',
        Blocked: '#e74c3c',
        Exempt: '#6c7a89'
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

    function formatPrice(value) {
        var n = Number(value || 0);
        if (!isFinite(n)) return '0';
        if (Math.abs(n - Math.round(n)) < 0.005) return String(Math.round(n));
        return n.toFixed(2);
    }

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
            page.querySelector('#npnpNote').value = cfg.CustomNote || '';
            var ce = page.querySelector('#npnpContactEmail');
            if (ce) ce.value = cfg.ContactEmail || '';
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
            CustomNote: page.querySelector('#npnpNote').value,
            ContactEmail: (page.querySelector('#npnpContactEmail') ? page.querySelector('#npnpContactEmail').value : '') || '',
            UiCultureOverride: (page.querySelector('#npnpUiCulture') ? page.querySelector('#npnpUiCulture').value : '') || ''
        };
        return api().ajax({
            type: 'POST',
            url: api().getUrl('NoPayNoPlay/Settings'),
            data: JSON.stringify(body),
            contentType: 'application/json'
        });
    }

    // Client-side validation for settings fields that the server may silently sanitize.
    function validateSettingsFields(page) {
        var warnings = [];
        var paypal = (page.querySelector('#npnpPaypal').value || '').trim();
        var lydia = (page.querySelector('#npnpLydia').value || '').trim();
        var email = (page.querySelector('#npnpContactEmail') ? page.querySelector('#npnpContactEmail').value : '') || '';
        var emailTrimmed = email.trim();

        if (paypal && !/^https?:\/\//i.test(paypal)) {
            warnings.push(t('admin.settings.validation.url', 'PayPal link must start with http:// or https://'));
        }
        if (lydia && !/^https?:\/\//i.test(lydia)) {
            warnings.push(t('admin.settings.validation.url', 'Lydia link must start with http:// or https://'));
        }
        if (emailTrimmed && !/^[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}$/.test(emailTrimmed)) {
            warnings.push(t('admin.settings.validation.email', 'Contact email does not look valid.'));
        }
        return warnings;
    }

    function loadUsers(page) {
        return api().ajax({
            type: 'GET', url: api().getUrl('NoPayNoPlay/Users'), dataType: 'json'
        }).then(function (users) {
            state.users = users || [];
            // Drop selection entries pointing to users that no longer exist.
            var keep = {};
            state.users.forEach(function (u) { if (state.selected[u.UserId]) keep[u.UserId] = true; });
            state.selected = keep;
            renderUsers(page);
            return loadStats(page);
        });
    }

    function loadStats(page) {
        return api().ajax({
            type: 'GET', url: api().getUrl('NoPayNoPlay/Stats'), dataType: 'json'
        }).then(function (s) {
            state.stats = s;
            renderStats(page);
        }).catch(function () { /* best-effort */ });
    }

    function renderStats(page) {
        var el = page.querySelector('#npnpStats');
        if (!el || !state.stats) return;
        var s = state.stats;
        var c = s.currency || 'EUR';
        var cards = [
            ['admin.stats.thisMonth', 'Revenue this month', s.revenueThisMonth],
            ['admin.stats.last12Months', 'Last 12 months', s.revenueLast12Months],
            ['admin.stats.allTime', 'All time', s.revenueAllTime],
            ['admin.stats.transactions', 'Transactions', s.transactionCount, true]
        ];
        el.innerHTML = cards.map(function (card) {
            var val = card[3] ? String(card[2] || 0) : (formatPrice(card[2]) + ' ' + escapeHtml(c));
            return '<div class="npnp-stat-card">'
                + '<span class="npnp-stat-num">' + escapeHtml(val) + '</span>'
                + '<span class="npnp-stat-lbl">' + escapeHtml(t(card[0], card[1])) + '</span>'
                + '</div>';
        }).join('');

        // Inline 12-month revenue chart, drawn as a tiny SVG. No external libs.
        var chart = page.querySelector('#npnpRevenueChart');
        if (chart && Array.isArray(s.monthlyAmounts) && s.monthlyAmounts.length) {
            var labels = s.monthlyLabels || [];
            var amounts = s.monthlyAmounts.map(function (n) { return Number(n || 0); });
            var max = Math.max.apply(null, amounts.concat([1]));
            var w = 480, h = 140, padL = 30, padB = 26, padT = 12, padR = 10;
            var innerW = w - padL - padR;
            var innerH = h - padT - padB;
            var barW = innerW / amounts.length * 0.7;
            var step = innerW / amounts.length;
            var bars = amounts.map(function (v, i) {
                var bh = max > 0 ? (v / max) * innerH : 0;
                var x = padL + i * step + (step - barW) / 2;
                var y = padT + innerH - bh;
                var lbl = (labels[i] || '').slice(5); // show MM only
                var title = (labels[i] || '') + ' \u2192 ' + formatPrice(v) + ' ' + c;
                return '<g><title>' + escapeHtml(title) + '</title>'
                    + '<rect x="' + x.toFixed(1) + '" y="' + y.toFixed(1)
                    + '" width="' + barW.toFixed(1) + '" height="' + bh.toFixed(1)
                    + '" rx="2" fill="#00a4dc" opacity="' + (v > 0 ? 0.85 : 0.25) + '"></rect>'
                    + '<text x="' + (x + barW / 2).toFixed(1) + '" y="' + (h - 8)
                    + '" text-anchor="middle" font-size="10" fill="currentColor" opacity=".7">'
                    + escapeHtml(lbl) + '</text>'
                    + '</g>';
            }).join('');
            var yMaxLabel = '<text x="4" y="' + (padT + 4) + '" font-size="10" fill="currentColor" opacity=".6">'
                + escapeHtml(formatPrice(max) + ' ' + c) + '</text>';
            var axis = '<line x1="' + padL + '" y1="' + (padT + innerH) + '" x2="' + (w - padR)
                + '" y2="' + (padT + innerH) + '" stroke="currentColor" stroke-opacity=".25"></line>';
            chart.innerHTML = '<div class="npnp-chart-title">'
                + escapeHtml(t('admin.stats.chart.title', 'Revenue \u2014 last 12 months'))
                + '</div>'
                + '<svg viewBox="0 0 ' + w + ' ' + h + '" preserveAspectRatio="xMidYMid meet" '
                + 'role="img" aria-label="' + escapeHtml(t('admin.stats.chart.title', 'Revenue \u2014 last 12 months')) + '" '
                + 'style="width:100%;height:auto;max-width:560px;display:block;">'
                + axis + bars + yMaxLabel + '</svg>';
        }
    }

    // Shows a brief "Loading…" placeholder for lazy-loaded tabs, but only when the
    // target is still empty — so revisiting a tab doesn't flicker its content.
    function setBusy(page, sel) {
        var el = page.querySelector(sel);
        if (el && !el.querySelector('table')) {
            el.innerHTML = '<div class="npnp-empty">' + escapeHtml(t('common.loading', 'Loading…')) + '</div>';
        }
    }

    function loadActivity(page) {
        setBusy(page, '#npnpActivityTable');
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
                el.className = 'npnp-status warn npnp-status-strong';
                el.innerHTML = '<div style="display:flex;align-items:flex-start;gap:10px;flex:1;">'
                    + '<span class="material-icons" aria-hidden="true" style="color:#e74c3c;font-size:28px;">error</span>'
                    + '<div style="flex:1;">'
                    + '<div style="font-weight:700;font-size:15px;margin-bottom:4px;">'
                    + escapeHtml(t('admin.status.ftMissing.title', 'File Transformation plugin missing'))
                    + '</div>'
                    + '<div style="font-size:13px;line-height:1.5;opacity:.95;">'
                    + escapeHtml(t('admin.status.ftMissing', 'File Transformation plugin not detected.'))
                    + '</div>'
                    + '<div style="margin-top:8px;display:flex;gap:8px;flex-wrap:wrap;">'
                    + '<a href="https://github.com/IAmParadox27/jellyfin-plugin-file-transformation" '
                    + 'target="_blank" rel="noopener" class="emby-button raised">'
                    + escapeHtml(t('admin.status.ftMissing.docs', 'Install instructions'))
                    + '</a>'
                    + '<button is="emby-button" type="button" id="npnpStatusRetry" class="button-alt">'
                    + escapeHtml(t('admin.diag.retry', 'Retry registration'))
                    + '</button>'
                    + '</div>'
                    + '</div></div>';
                var btn = el.querySelector('#npnpStatusRetry');
                if (btn) {
                    btn.addEventListener('click', function () {
                        api().ajax({ type: 'POST', url: api().getUrl('NoPayNoPlay/Diagnostics/Retry') })
                            .then(function () { return loadStatus(page); });
                    });
                }
            }
            el.style.display = '';
        }).catch(function () { /* ignore */ });
    }

    function loadDiagnostics(page) {
        setBusy(page, '#npnpDiagBody');
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
            case 'LastMethod': {
                var txm = (u.Transactions || [])[0];
                return txm ? (txm.Method || '').toLowerCase() : '';
            }
            case 'TotalPaid': return Number(u.TotalPaid || 0);
            case 'Tag': return (u.Tag || '').toLowerCase();
            default: return '';
        }
    }

    function lastMethodOf(u) {
        var tx = (u.Transactions || [])[0];
        return tx ? (tx.Method || '') : '';
    }

    // Rebuild the "filter by method" dropdown from the methods actually present
    // in the latest transaction of each user. Preserves the current selection
    // when possible.
    function refreshMethodFilter(page) {
        var sel = page.querySelector('#npnpMethodFilter');
        if (!sel) return;
        var seen = {};
        state.users.forEach(function (u) {
            var m = lastMethodOf(u);
            if (m) seen[m] = true;
        });
        var methods = Object.keys(seen).sort(function (a, b) {
            return a.toLowerCase().localeCompare(b.toLowerCase());
        });
        var current = state.methodFilter;
        var html = '<option value="">' + escapeHtml(t('admin.users.filter.allMethods', 'All methods')) + '</option>';
        html += '<option value="__none__">' + escapeHtml(t('admin.users.filter.noMethod', 'No payment yet')) + '</option>';
        methods.forEach(function (m) {
            html += '<option value="' + escapeHtml(m) + '">' + escapeHtml(m) + '</option>';
        });
        sel.innerHTML = html;
        // Restore selection if still valid; otherwise fall back to "all".
        if (current === '__none__' || current === '' || methods.indexOf(current) >= 0) {
            sel.value = current;
        } else {
            sel.value = '';
            state.methodFilter = '';
        }
    }

    function filterUsers() {
        var q = state.userFilter.toLowerCase();
        var sf = state.stateFilter;
        var mf = state.methodFilter;
        return state.users.filter(function (u) {
            if (q && (u.Username || '').toLowerCase().indexOf(q) < 0) return false;
            if (sf) {
                var s = u.IsExempt ? 'Exempt' : u.State;
                if (s !== sf) return false;
            }
            if (mf) {
                var m = lastMethodOf(u);
                if (mf === '__none__') {
                    if (m) return false;
                } else if (m !== mf) {
                    return false;
                }
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
        refreshMethodFilter(page);
        var allRows = filterUsers();
        var container = page.querySelector('#npnpUsersTable');

        if (!allRows.length) {
            var msg = (state.userFilter || state.stateFilter)
                ? t('admin.users.empty.filter', 'No member matches the current filter.')
                : t('admin.users.empty', 'No member to display.');
            container.innerHTML = '<div class="npnp-empty">' + escapeHtml(msg) + '</div>';
            return;
        }

        // Pagination: clamp page and slice rows.
        var totalPages = Math.ceil(allRows.length / state.pageSize);
        if (state.page < 1) state.page = 1;
        if (state.page > totalPages) state.page = totalPages;
        var start = (state.page - 1) * state.pageSize;
        var rows = allRows.slice(start, start + state.pageSize);

        var selectAllLabel = escapeHtml(t('admin.users.selectAll', 'Select all'));
        var html = '<table class="npnp-table">'
            + '<thead><tr>'
            + '<th class="npnp-row-check"><label class="emby-checkbox-label"><input is="emby-checkbox" type="checkbox" id="npnpSelectAll" aria-label="' + selectAllLabel + '" /><span></span></label></th>'
            + '<th data-sort="Username">' + escapeHtml(t('admin.users.col.user', 'User')) + arrowFor('Username') + '</th>'
            + '<th data-sort="State">' + escapeHtml(t('admin.users.col.state', 'State')) + arrowFor('State') + '</th>'
            + '<th data-sort="ExpiryDate">' + escapeHtml(t('admin.users.col.expiry', 'Expiry')) + arrowFor('ExpiryDate') + '</th>'
            + '<th data-sort="DaysLeft">' + escapeHtml(t('admin.users.col.daysLeft', 'Days left')) + arrowFor('DaysLeft') + '</th>'
            + '<th data-sort="LastPayment">' + escapeHtml(t('admin.users.col.lastPayment', 'Last payment')) + arrowFor('LastPayment') + '</th>'
            + '<th data-sort="LastMethod">' + escapeHtml(t('admin.users.col.lastMethod', 'Last method')) + arrowFor('LastMethod') + '</th>'
            + '<th data-sort="TotalPaid">' + escapeHtml(t('admin.users.col.totalPaid', 'Total paid')) + arrowFor('TotalPaid') + '</th>'
            + '<th data-sort="Tag">' + escapeHtml(t('admin.users.col.tag', 'Tag')) + arrowFor('Tag') + '</th>'
            + '<th>' + escapeHtml(t('admin.users.col.actions', 'Actions')) + '</th>'
            + '</tr></thead><tbody>';

        rows.forEach(function (u) {
            var color = u.IsExempt ? STATE_COLORS.Exempt : (STATE_COLORS[u.State] || '#888');
            var label = stateLabel(u.State, u.IsExempt);
            var lastTx = (u.Transactions || [])[0];
            var lastLabel = lastTx ? formatDate(lastTx.Date) : t('admin.users.never', 'Never');
            var lastMethod = lastTx && lastTx.Method ? lastTx.Method : '—';
            var actExempt = u.IsExempt
                ? t('admin.users.action.unexempt', 'Remove exemption')
                : t('admin.users.action.exempt', 'Mark exempt');
            var checked = state.selected[u.UserId] ? ' checked' : '';

            var pendingBadge = u.HasPendingPaymentClaim
                ? '<span class="npnp-state-pill" style="background:#f39c12;margin-left:6px;" title="'
                    + escapeHtml(format(t('admin.users.pending.tooltip',
                        'User declared payment on {date} via {method}'),
                        { date: formatDate(u.PendingPaymentClaimAt), method: u.PendingPaymentMethod || '—' }))
                    + '">' + escapeHtml(t('admin.users.pending.badge', 'Pending claim')) + '</span>'
                : '';

            var arrearsBadge = (u.ArrearsMonths && u.ArrearsMonths > 0)
                ? '<span class="npnp-state-pill" style="background:#e74c3c;margin-left:6px;">'
                    + escapeHtml(format(t('admin.users.arrears.badge', '{n}m late'), { n: u.ArrearsMonths }))
                    + '</span>'
                : '';

            var totalPaidStr = formatPrice(u.TotalPaid || 0) + ' ' + ((state.settings && state.settings.Currency) || 'EUR');

            var tagsList = (state.tags || []);
            var currentTag = u.Tag || '';
            var tagOpts = '<option value="">' + escapeHtml(t('admin.users.tag.none', '—')) + '</option>'
                + tagsList.map(function (tg) {
                    return '<option value="' + escapeHtml(tg.Key) + '"'
                        + (tg.Key === currentTag ? ' selected' : '') + '>'
                        + escapeHtml(tg.Label || tg.Key) + '</option>';
                }).join('');
            var tagCell = '<select is="emby-select" class="emby-select npnp-tag-select" data-userid="' + escapeHtml(u.UserId) + '">'
                + tagOpts + '</select>';

            var pendingActions = u.HasPendingPaymentClaim
                ? '<button is="emby-button" type="button" class="raised npnp-confirm-pending" title="'
                    + escapeHtml(t('admin.users.pending.confirm', 'Confirm payment'))
                    + '"><span class="material-icons" aria-hidden="true">check_circle</span></button>'
                    + '<button is="emby-button" type="button" class="button-alt npnp-reject-pending npnp-danger" title="'
                    + escapeHtml(t('admin.users.pending.reject', 'Reject claim'))
                    + '"><span class="material-icons" aria-hidden="true">cancel</span></button>'
                : '';

            var rowCheckLabel = escapeHtml(format(t('admin.users.selectRow', 'Select {username}'), { username: u.Username }));
            html += '<tr data-userid="' + escapeHtml(u.UserId) + '">'
                + '<td class="npnp-row-check"><label class="emby-checkbox-label"><input is="emby-checkbox" type="checkbox" class="npnp-rowcheck"' + checked + ' aria-label="' + rowCheckLabel + '" /><span></span></label></td>'
                + '<td>' + escapeHtml(u.Username) + '</td>'
                + '<td><span class="npnp-state-pill" style="background:' + color + '">' + escapeHtml(label) + '</span>' + pendingBadge + arrearsBadge + '</td>'
                + '<td>' + escapeHtml(u.IsExempt ? '—' : formatDate(u.ExpiryDate)) + '</td>'
                + '<td>' + escapeHtml(u.IsExempt ? '—' : String(u.DaysLeft)) + '</td>'
                + '<td>' + escapeHtml(lastLabel) + '</td>'
                + '<td>' + escapeHtml(lastMethod) + '</td>'
                + '<td>' + escapeHtml(totalPaidStr) + '</td>'
                + '<td>' + tagCell + '</td>'
                + '<td><div class="npnp-actions">'
                + pendingActions
                + '<button is="emby-button" type="button" class="raised npnp-pay" title="' + escapeHtml(t('admin.users.action.pay', 'Record payment')) + '"><span class="material-icons" aria-hidden="true">payments</span></button>'
                + '<button is="emby-button" type="button" class="raised npnp-history" title="' + escapeHtml(t('admin.users.action.history', 'History')) + '"><span class="material-icons" aria-hidden="true">history</span></button>'
                + '<button is="emby-button" type="button" class="raised npnp-exempt" title="' + escapeHtml(actExempt) + '"><span class="material-icons" aria-hidden="true">' + (u.IsExempt ? 'lock' : 'lock_open') + '</span></button>'
                + '<button is="emby-button" type="button" class="raised npnp-reset npnp-danger" title="' + escapeHtml(t('admin.users.action.reset', 'Reset trial')) + '"><span class="material-icons" aria-hidden="true">restart_alt</span></button>'
                + '</div></td>'
                + '</tr>';
        });

        html += '</tbody></table>';

        // Pagination controls (only when more than one page).
        if (totalPages > 1) {
            var prevDisabled = state.page <= 1 ? ' disabled' : '';
            var nextDisabled = state.page >= totalPages ? ' disabled' : '';
            html += '<div class="npnp-pagination" style="display:flex;align-items:center;justify-content:center;gap:8px;margin-top:12px;font-size:13px;">'
                + '<button is="emby-button" type="button" class="button-alt npnp-page-prev"' + prevDisabled + ' aria-label="' + escapeHtml(t('admin.pagination.prev', 'Previous page')) + '">'
                + '<span class="material-icons" aria-hidden="true">chevron_left</span></button>'
                + '<span style="opacity:.8;">' + escapeHtml(format(t('admin.pagination.info', 'Page {page} of {total}'), { page: state.page, total: totalPages })) + '</span>'
                + '<button is="emby-button" type="button" class="button-alt npnp-page-next"' + nextDisabled + ' aria-label="' + escapeHtml(t('admin.pagination.next', 'Next page')) + '">'
                + '<span class="material-icons" aria-hidden="true">chevron_right</span></button>'
                + '</div>';
        }

        container.innerHTML = html;
        renderBulkBar(page);

        // Pagination button handlers.
        var prevBtn = container.querySelector('.npnp-page-prev');
        var nextBtn = container.querySelector('.npnp-page-next');
        if (prevBtn) prevBtn.addEventListener('click', function () {
            if (state.page > 1) { state.page--; renderUsers(page); }
        });
        if (nextBtn) nextBtn.addEventListener('click', function () {
            if (state.page < totalPages) { state.page++; renderUsers(page); }
        });

        var selectAll = container.querySelector('#npnpSelectAll');
        if (selectAll) {
            var visibleIds = rows.map(function (r) { return r.UserId; });
            var allChecked = visibleIds.length > 0 && visibleIds.every(function (id) { return state.selected[id]; });
            selectAll.checked = allChecked;
            selectAll.addEventListener('change', function () {
                visibleIds.forEach(function (id) {
                    if (selectAll.checked) state.selected[id] = true;
                    else delete state.selected[id];
                });
                renderUsers(page);
            });
        }
        container.querySelectorAll('tr[data-userid]').forEach(function (tr) {
            var userId = tr.getAttribute('data-userid');
            var rc = tr.querySelector('.npnp-rowcheck');
            if (rc) rc.addEventListener('change', function () {
                if (rc.checked) state.selected[userId] = true;
                else delete state.selected[userId];
                renderBulkBar(page);
                var sa = page.querySelector('#npnpSelectAll');
                if (sa) sa.checked = rows.every(function (r) { return state.selected[r.UserId]; });
            });
        });

        container.querySelectorAll('th[data-sort]').forEach(function (th) {
            th.addEventListener('click', function () {
                var key = th.getAttribute('data-sort');
                if (state.sortKey === key) {
                    state.sortDir = -state.sortDir;
                } else {
                    state.sortKey = key;
                    state.sortDir = 1;
                }
                state.page = 1;
                renderUsers(page);
            });
        });

        container.querySelectorAll('tr[data-userid]').forEach(function (tr) {
            var userId = tr.getAttribute('data-userid');
            var user = state.users.find(function (x) { return x.UserId === userId; });
            var tagSel = tr.querySelector('.npnp-tag-select');
            if (tagSel) {
                tagSel.addEventListener('change', function () {
                    var newTag = tagSel.value || '';
                    api().ajax({
                        type: 'POST',
                        url: api().getUrl('NoPayNoPlay/Users/' + userId + '/Tag'),
                        data: JSON.stringify({ Tag: newTag }),
                        contentType: 'application/json'
                    }).then(function () {
                        flash(page, t(newTag ? 'admin.users.tag.set' : 'admin.users.tag.cleared',
                            newTag ? 'Tag updated.' : 'Tag removed.'));
                        return loadUsers(page);
                    });
                });
            }
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
            var confirmBtn = tr.querySelector('.npnp-confirm-pending');
            if (confirmBtn) {
                confirmBtn.addEventListener('click', function () {
                    openPaymentModal(page, user, /* fromPending */ true);
                });
            }
            var rejectBtn = tr.querySelector('.npnp-reject-pending');
            if (rejectBtn) {
                rejectBtn.addEventListener('click', function () {
                    api().ajax({
                        type: 'POST',
                        url: api().getUrl('NoPayNoPlay/Users/' + userId + '/RejectPending')
                    }).then(function () {
                        flash(page, t('admin.users.pending.rejected', 'Claim rejected.'));
                        return loadUsers(page);
                    });
                });
            }
        });
    }

    function renderActivity(page) {
        var q = state.activityFilter.toLowerCase();
        var fromTs = state.activityFrom ? new Date(state.activityFrom + 'T00:00:00').getTime() : null;
        var toTs = state.activityTo ? new Date(state.activityTo + 'T23:59:59').getTime() : null;
        var rows = state.activity.filter(function (r) {
            var d = new Date(r.Date).getTime();
            if (fromTs !== null && d < fromTs) return false;
            if (toTs !== null && d > toTs) return false;
            if (!q) return true;
            return (r.Username || '').toLowerCase().indexOf(q) >= 0
                || (r.Method || '').toLowerCase().indexOf(q) >= 0
                || (r.AdminNote || '').toLowerCase().indexOf(q) >= 0;
        });

        // Sort activity rows.
        var actSort = state.activitySortKey || 'Date';
        var actDir = state.activitySortDir || -1;
        rows.sort(function (a, b) {
            var av, bv;
            switch (actSort) {
                case 'Date': av = new Date(a.Date).getTime(); bv = new Date(b.Date).getTime(); break;
                case 'Username': av = (a.Username || '').toLowerCase(); bv = (b.Username || '').toLowerCase(); break;
                case 'Amount': av = Number(a.Amount || 0); bv = Number(b.Amount || 0); break;
                case 'Method': av = (a.Method || '').toLowerCase(); bv = (b.Method || '').toLowerCase(); break;
                case 'MonthsAdded': av = Number(a.MonthsAdded || 0); bv = Number(b.MonthsAdded || 0); break;
                default: return 0;
            }
            if (av < bv) return -1 * actDir;
            if (av > bv) return 1 * actDir;
            return 0;
        });

        function actArrow(key) {
            if (actSort !== key) return '';
            return '<span class="npnp-sort-arrow">' + (actDir > 0 ? '▲' : '▼') + '</span>';
        }

        var container = page.querySelector('#npnpActivityTable');
        if (!rows.length) {
            container.innerHTML = '<div class="npnp-empty">' + escapeHtml(t('admin.activity.empty', 'No activity yet.')) + '</div>';
            return;
        }
        var html = '<table class="npnp-table">'
            + '<thead><tr>'
            + '<th data-sort="Date">' + escapeHtml(t('admin.activity.col.date', 'Date')) + actArrow('Date') + '</th>'
            + '<th data-sort="Username">' + escapeHtml(t('admin.activity.col.user', 'User')) + actArrow('Username') + '</th>'
            + '<th data-sort="Amount">' + escapeHtml(t('admin.activity.col.amount', 'Amount')) + actArrow('Amount') + '</th>'
            + '<th data-sort="Method">' + escapeHtml(t('admin.activity.col.method', 'Method')) + actArrow('Method') + '</th>'
            + '<th data-sort="MonthsAdded">' + escapeHtml(t('admin.activity.col.months', 'Months')) + actArrow('MonthsAdded') + '</th>'
            + '<th>' + escapeHtml(t('admin.activity.col.note', 'Note')) + '</th>'
            + '</tr></thead><tbody>';
        rows.forEach(function (r) {
            html += '<tr>'
                + '<td>' + escapeHtml(formatDateTime(r.Date)) + '</td>'
                + '<td>' + escapeHtml(r.Username || '') + '</td>'
                + '<td>' + escapeHtml(formatPrice(r.Amount)) + '</td>'
                + '<td>' + escapeHtml(r.Method || '') + '</td>'
                + '<td>' + escapeHtml(String(r.MonthsAdded || 0)) + '</td>'
                + '<td>' + escapeHtml(r.AdminNote || '') + '</td>'
                + '</tr>';
        });
        html += '</tbody></table>';
        container.innerHTML = html;

        // Bind sort on activity table headers.
        container.querySelectorAll('th[data-sort]').forEach(function (th) {
            th.addEventListener('click', function () {
                var key = th.getAttribute('data-sort');
                if (state.activitySortKey === key) {
                    state.activitySortDir = -(state.activitySortDir || -1);
                } else {
                    state.activitySortKey = key;
                    state.activitySortDir = key === 'Date' ? -1 : 1;
                }
                renderActivity(page);
            });
        });
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
            + '  <div class="npnp-modal" role="dialog" aria-modal="true" tabindex="-1">'
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

        var modal = root.querySelector('.npnp-modal');

        // --- Accessibility: Escape to close, focus trap, restore focus on close ---
        var opener = document.activeElement;
        function onKey(e) {
            if (!page.contains(modal)) { document.removeEventListener('keydown', onKey); return; }
            if (e.key === 'Escape') {
                e.preventDefault();
                closeModal(page);
            } else if (e.key === 'Tab') {
                var f = Array.prototype.slice.call(modal.querySelectorAll(
                    'a[href],button:not([disabled]),input:not([disabled]),select:not([disabled]),'
                    + 'textarea:not([disabled]),[tabindex]:not([tabindex="-1"])'))
                    .filter(function (el) { return el.offsetParent !== null; });
                if (!f.length) return;
                var first = f[0], last = f[f.length - 1];
                if (e.shiftKey && document.activeElement === first) { e.preventDefault(); last.focus(); }
                else if (!e.shiftKey && document.activeElement === last) { e.preventDefault(); first.focus(); }
            }
        }
        document.addEventListener('keydown', onKey);
        try {
            var mo = new MutationObserver(function () {
                if (!page.contains(modal)) {
                    mo.disconnect();
                    document.removeEventListener('keydown', onKey);
                    if (opener && typeof opener.focus === 'function') {
                        try { opener.focus(); } catch (_) {}
                    }
                }
            });
            mo.observe(root, { childList: true });
        } catch (_) {}

        // Focus the first real field (not the corner close button), else the dialog.
        var firstField = modal.querySelector('input:not([type="hidden"]),select,textarea,[data-npnp-cancel]');
        try { (firstField || modal).focus(); } catch (_) {}

        return modal;
    }

    function openConfirmModal(page, title, message, onConfirm) {
        var modal = buildModal(page,
            escapeHtml(title),
            '<p>' + escapeHtml(message) + '</p>',
            '<button is="emby-button" type="button" class="button-alt" data-npnp-cancel>' + escapeHtml(t('common.cancel', 'Cancel')) + '</button>'
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
                var id = e.Id || '';
                return '<tr data-tx="' + escapeHtml(id) + '">'
                    + '<td>' + escapeHtml(formatDateTime(e.Date)) + '</td>'
                    + '<td>' + escapeHtml(formatPrice(e.Amount)) + '</td>'
                    + '<td>' + escapeHtml(e.Method || '') + '</td>'
                    + '<td>' + escapeHtml(String(e.MonthsAdded || 0)) + '</td>'
                    + '<td>' + escapeHtml(e.AdminNote || '') + '</td>'
                    + '<td><div class="npnp-actions">'
                    + (id ? '<button is="emby-button" type="button" class="button-alt npnp-tx-edit" title="' + escapeHtml(t('admin.users.tx.edit', 'Edit')) + '"><span class="material-icons" aria-hidden="true">edit</span></button>' : '')
                    + (id ? '<button is="emby-button" type="button" class="button-alt npnp-tx-del npnp-danger" title="' + escapeHtml(t('admin.users.tx.delete', 'Delete')) + '"><span class="material-icons" aria-hidden="true">delete</span></button>' : '')
                    + '</div></td>'
                    + '</tr>';
            }).join('');
            body = '<table class="npnp-table">'
                + '<thead><tr>'
                + '<th>' + escapeHtml(t('admin.activity.col.date', 'Date')) + '</th>'
                + '<th>' + escapeHtml(t('admin.activity.col.amount', 'Amount')) + '</th>'
                + '<th>' + escapeHtml(t('admin.activity.col.method', 'Method')) + '</th>'
                + '<th>' + escapeHtml(t('admin.activity.col.months', 'Months')) + '</th>'
                + '<th>' + escapeHtml(t('admin.activity.col.note', 'Note')) + '</th>'
                + '<th>' + escapeHtml(t('admin.users.col.actions', 'Actions')) + '</th>'
                + '</tr></thead><tbody>' + rows + '</tbody></table>';
        }
        var title = format(t('admin.users.history.title', 'Payment history — {username}'), { username: user.Username });
        var modal = buildModal(page, escapeHtml(title), body, '');

        modal.querySelectorAll('tr[data-tx]').forEach(function (tr) {
            var txId = tr.getAttribute('data-tx');
            var entry = (user.Transactions || []).find(function (e) { return e.Id === txId; });
            var editBtn = tr.querySelector('.npnp-tx-edit');
            var delBtn = tr.querySelector('.npnp-tx-del');
            if (editBtn) editBtn.addEventListener('click', function () { openEditTxModal(page, user, entry); });
            if (delBtn) delBtn.addEventListener('click', function () {
                openConfirmModal(page,
                    t('admin.users.tx.delete', 'Delete'),
                    t('admin.users.tx.confirm.delete', 'Delete this transaction? The expiry date will be recomputed.'),
                    function () {
                        api().ajax({
                            type: 'DELETE',
                            url: api().getUrl('NoPayNoPlay/Users/' + user.UserId + '/Transactions/' + txId)
                        }).then(function () {
                            closeModal(page);
                            return loadUsers(page).then(function () { return loadActivity(page); });
                        });
                    });
            });
        });
    }

    function openEditTxModal(page, user, entry) {
        if (!entry) return;
        var d = new Date(entry.Date);
        var iso = d.getFullYear() + '-' + String(d.getMonth() + 1).padStart(2, '0') + '-' + String(d.getDate()).padStart(2, '0');
        var body = ''
            + '<form id="npnpEditTxForm">'
            + '<div class="inputContainer"><label for="npnpEditTxDate">' + escapeHtml(t('admin.payment.date', 'Date')) + '</label>'
            + '<input is="emby-input" id="npnpEditTxDate" type="date" value="' + escapeHtml(iso) + '" /></div>'
            + '<div class="inputContainer"><label for="npnpEditTxAmount">' + escapeHtml(t('admin.payment.amount', 'Amount')) + '</label>'
            + '<input is="emby-input" id="npnpEditTxAmount" type="number" step="0.01" min="0" value="' + escapeHtml(String(entry.Amount || 0)) + '" /></div>'
            + '<div class="inputContainer"><label for="npnpEditTxMonths">' + escapeHtml(t('admin.payment.months', 'Months')) + '</label>'
            + '<input is="emby-input" id="npnpEditTxMonths" type="number" min="1" max="60" value="' + escapeHtml(String(entry.MonthsAdded || 1)) + '" /></div>'
            + '<div class="inputContainer"><label for="npnpEditTxMethod">' + escapeHtml(t('admin.payment.method', 'Method')) + '</label>'
            + '<input is="emby-input" id="npnpEditTxMethod" type="text" value="' + escapeHtml(entry.Method || '') + '" /></div>'
            + '<div class="inputContainer"><label for="npnpEditTxNote">' + escapeHtml(t('admin.payment.note', 'Note')) + '</label>'
            + '<input is="emby-input" id="npnpEditTxNote" type="text" value="' + escapeHtml(entry.AdminNote || '') + '" /></div>'
            + '</form>';
        var modal = buildModal(page,
            escapeHtml(t('admin.users.tx.edit', 'Edit transaction')),
            body,
            '<button is="emby-button" type="button" class="button-alt" data-npnp-cancel>' + escapeHtml(t('common.cancel', 'Cancel')) + '</button>'
            + '<button is="emby-button" type="button" class="raised button-submit" data-npnp-confirm>OK</button>');
        modal.querySelector('[data-npnp-cancel]').addEventListener('click', function () { closeModal(page); });
        modal.querySelector('[data-npnp-confirm]').addEventListener('click', function () {
            var dv = modal.querySelector('#npnpEditTxDate').value;
            var payload = {
                Amount: parseFloat(modal.querySelector('#npnpEditTxAmount').value || '0'),
                MonthsAdded: parseInt(modal.querySelector('#npnpEditTxMonths').value || '1', 10),
                Method: modal.querySelector('#npnpEditTxMethod').value || '',
                Note: modal.querySelector('#npnpEditTxNote').value || '',
                Date: dv ? (dv + 'T12:00:00Z') : null
            };
            api().ajax({
                type: 'PATCH',
                url: api().getUrl('NoPayNoPlay/Users/' + user.UserId + '/Transactions/' + entry.Id),
                data: JSON.stringify(payload),
                contentType: 'application/json'
            }).then(function () {
                closeModal(page);
                return loadUsers(page).then(function () { return loadActivity(page); });
            });
        });
    }


    function openPaymentModal(page, user, fromPending) {
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
        // Today as YYYY-MM-DD for the <input type="date"> default.
        var today = new Date();
        var todayIso = today.getFullYear() + '-'
            + String(today.getMonth() + 1).padStart(2, '0') + '-'
            + String(today.getDate()).padStart(2, '0');

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
            + '<div class="inputContainer"><label for="npnpPayDate">' + escapeHtml(t('admin.payment.date', 'Payment date')) + '</label>'
            + '<input is="emby-input" id="npnpPayDate" type="date" min="2020-01-01" max="' + escapeHtml(todayIso) + '" value="' + escapeHtml(todayIso) + '" />'
            + '<div class="fieldDescription" style="opacity:.7;font-size:12px;margin-top:4px;">' + escapeHtml(t('admin.payment.date.help', 'Leave empty for today. Use a past date to backfill payments already received.')) + '</div></div>'
            + '<div class="inputContainer"><label for="npnpPayNote">' + escapeHtml(t('admin.payment.note', 'Note (optional)')) + '</label>'
            + '<textarea is="emby-textarea" id="npnpPayNote" rows="2"></textarea></div>'
            + '</form>';

        var footer = ''
            + '<button is="emby-button" type="button" class="button-alt" data-npnp-cancel>' + escapeHtml(t('admin.payment.cancel', 'Cancel')) + '</button>'
            + '<button is="emby-button" type="button" class="raised button-submit" data-npnp-save>' + escapeHtml(t('admin.payment.save', 'Save')) + '</button>';

        var modal = buildModal(page, escapeHtml(t('admin.payment.title', 'Record a payment')), body, footer);
        modal.querySelector('[data-npnp-cancel]').addEventListener('click', function () { closeModal(page); });
        modal.querySelector('[data-npnp-save]').addEventListener('click', function () {
            var dateVal = (modal.querySelector('#npnpPayDate').value || '').trim();
            var payload = {
                Amount: parseFloat(modal.querySelector('#npnpPayAmount').value || '0'),
                Method: modal.querySelector('#npnpPayMethod').value || 'Other',
                MonthsAdded: parseInt(modal.querySelector('#npnpPayMonths').value || '1', 10),
                Note: modal.querySelector('#npnpPayNote').value || ''
            };
            if (dateVal) {
                // Send midday UTC to avoid timezone-related off-by-one when the server clamps to today.
                payload.Date = dateVal + 'T12:00:00Z';
            }
            api().ajax({
                type: 'POST',
                url: api().getUrl('NoPayNoPlay/Users/' + user.UserId + (fromPending ? '/ConfirmPending' : '/Pay')),
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

    function loadPromoCodes(page) {
        setBusy(page, '#npnpPromoTable');
        return api().ajax({
            type: 'GET', url: api().getUrl('NoPayNoPlay/PromoCodes'), dataType: 'json'
        }).then(function (rows) {
            state.promoCodes = rows || [];
            renderPromoTable(page);
        }).catch(function () {
            state.promoCodes = [];
            renderPromoTable(page);
        });
    }

    function renderPromoTable(page) {
        var container = page.querySelector('#npnpPromoTable');
        if (!container) return;
        var rows = state.promoCodes || [];
        if (!rows.length) {
            container.innerHTML = '<div class="npnp-empty" style="opacity:.6;font-style:italic;padding:8px 0;">'
                + escapeHtml(t('admin.promo.empty', 'No promo codes yet.')) + '</div>';
            return;
        }
        var unlimited = t('admin.promo.unlimited', 'Unlimited');
        var never = t('admin.promo.never', 'Never');
        var html = '<table class="npnp-table"><thead><tr>'
            + '<th>' + escapeHtml(t('admin.promo.col.code', 'Code')) + '</th>'
            + '<th>' + escapeHtml(t('admin.promo.col.months', 'Months')) + '</th>'
            + '<th>' + escapeHtml(t('admin.promo.col.uses', 'Uses')) + '</th>'
            + '<th>' + escapeHtml(t('admin.promo.col.expires', 'Expires')) + '</th>'
            + '<th>' + escapeHtml(t('admin.promo.col.created', 'Created')) + '</th>'
            + '<th>' + escapeHtml(t('admin.promo.col.actions', 'Actions')) + '</th>'
            + '</tr></thead><tbody>';
        rows.forEach(function (p) {
            var maxUses = Number(p.MaxUses || 0);
            var usedCount = Number(p.UsedCount || 0);
            var usesText = maxUses > 0
                ? (usedCount + ' / ' + maxUses)
                : (usedCount + ' / ' + unlimited);
            var expiresText = p.ExpiresAt ? formatDate(p.ExpiresAt) : never;
            var createdText = p.CreatedAt ? formatDate(p.CreatedAt) : '—';
            html += '<tr data-promoid="' + escapeHtml(p.Id) + '">'
                + '<td><code style="font-weight:700;">' + escapeHtml(p.Code) + '</code></td>'
                + '<td>' + escapeHtml(String(p.MonthsGranted || 1)) + '</td>'
                + '<td>' + escapeHtml(usesText) + '</td>'
                + '<td>' + escapeHtml(expiresText) + '</td>'
                + '<td>' + escapeHtml(createdText) + '</td>'
                + '<td><button is="emby-button" type="button" class="button-alt npnp-promo-delete npnp-danger" title="'
                + escapeHtml(t('admin.promo.delete', 'Delete')) + '"><span class="material-icons" aria-hidden="true">delete</span></button></td>'
                + '</tr>';
        });
        html += '</tbody></table>';
        container.innerHTML = html;

        container.querySelectorAll('tr[data-promoid]').forEach(function (tr) {
            var id = tr.getAttribute('data-promoid');
            tr.querySelector('.npnp-promo-delete').addEventListener('click', function () {
                openConfirmModal(page,
                    t('admin.promo.delete', 'Delete'),
                    t('admin.promo.deleteConfirm', 'Delete this promo code?'),
                    function () {
                        api().ajax({
                            type: 'DELETE',
                            url: api().getUrl('NoPayNoPlay/PromoCodes/' + encodeURIComponent(id))
                        }).then(function () {
                            flash(page, t('admin.promo.deleted', 'Promo code deleted.'));
                            return loadPromoCodes(page);
                        });
                    });
            });
        });
    }

    function bindPromoForm(page) {
        var form = page.querySelector('#npnpPromoForm');
        if (!form) return;
        form.addEventListener('submit', function (e) {
            e.preventDefault();
            var code = (page.querySelector('#npnpPromoCode').value || '').trim().toUpperCase();
            var months = parseInt(page.querySelector('#npnpPromoMonths').value || '1', 10);
            var maxUses = parseInt(page.querySelector('#npnpPromoMaxUses').value || '0', 10);
            var expiresVal = (page.querySelector('#npnpPromoExpires').value || '').trim();
            var payload = {
                Code: code,
                MonthsGranted: months,
                MaxUses: maxUses
            };
            if (expiresVal) {
                payload.ExpiresAt = expiresVal + 'T23:59:59Z';
            }
            api().ajax({
                type: 'POST',
                url: api().getUrl('NoPayNoPlay/PromoCodes'),
                data: JSON.stringify(payload),
                contentType: 'application/json',
                dataType: 'json'
            }).then(function () {
                flash(page, t('admin.promo.created', 'Promo code created.'));
                form.reset();
                page.querySelector('#npnpPromoMonths').value = '1';
                page.querySelector('#npnpPromoMaxUses').value = '0';
                return loadPromoCodes(page);
            }).catch(function (err) {
                var msg = (err && err.status === 409)
                    ? t('admin.promo.duplicate', 'A promo code with this name already exists.')
                    : t('admin.promo.invalid', 'Invalid promo code (use 6-32 chars, A-Z 0-9 _ -).');
                flash(page, msg, true);
            });
            return false;
        });
    }

    /* --- tabs --- */

    // Detect unsaved changes in the Tiers or Tags inline editors by comparing
    // the current DOM values against the last-saved state snapshot.
    function hasUnsavedEdits() {
        try {
            var tiersTable = document.querySelector('#npnpTiersTable');
            if (tiersTable) {
                var tierRows = tiersTable.querySelectorAll('tbody tr');
                if (tierRows.length !== (state._savedTiers || []).length) return true;
                for (var i = 0; i < tierRows.length; i++) {
                    var tr = tierRows[i];
                    var saved = (state._savedTiers || [])[i] || {};
                    var monthsEl = tr.querySelector('.npnp-tier-months');
                    var priceEl = tr.querySelector('.npnp-tier-price');
                    var labelEl = tr.querySelector('.npnp-tier-label');
                    var hlEl = tr.querySelector('.npnp-tier-highlight');
                    if ((monthsEl && parseInt(monthsEl.value || '1', 10) !== (saved.Months || 1))
                        || (priceEl && parseFloat(priceEl.value || '0') !== (saved.Price || 0))
                        || (labelEl && (labelEl.value || '') !== (saved.Label || ''))
                        || (hlEl && hlEl.checked !== !!saved.Highlight)) return true;
                }
            }
            var tagsTable = document.querySelector('#npnpTagsTable');
            if (tagsTable) {
                var tagRows = tagsTable.querySelectorAll('tbody tr');
                if (tagRows.length !== (state._savedTags || []).length) return true;
                for (var j = 0; j < tagRows.length; j++) {
                    var tr2 = tagRows[j];
                    var saved2 = (state._savedTags || [])[j] || {};
                    var keyEl = tr2.querySelector('.npnp-tag-key');
                    var labelEl2 = tr2.querySelector('.npnp-tag-label');
                    var colorEl = tr2.querySelector('.npnp-tag-color');
                    var priceEl2 = tr2.querySelector('.npnp-tag-price');
                    if ((keyEl && (keyEl.value || '') !== (saved2.Key || ''))
                        || (labelEl2 && (labelEl2.value || '') !== (saved2.Label || ''))
                        || (colorEl && (colorEl.value || '') !== (saved2.Color || ''))
                        || (priceEl2 && parseFloat(priceEl2.value || '0') !== (saved2.MonthlyPriceOverride || 0))) return true;
                }
            }
        } catch (_) {}
        return false;
    }

    function snapshotSavedState() {
        state._savedTiers = (state.tiers || []).map(function (r) { return Object.assign({}, r); });
        state._savedTags = (state.tags || []).map(function (r) { return Object.assign({}, r); });
    }

    function activateTab(page, tab, moveFocus) {
        // Warn before leaving tiers/tags tabs with unsaved edits.
        var currentActive = page.querySelector('.npnp-tab-panel.active');
        var currentTabId = currentActive ? currentActive.id.replace('npnpTab-', '') : '';
        if ((currentTabId === 'tiers' || currentTabId === 'tags') && tab !== currentTabId && hasUnsavedEdits()) {
            if (!confirm(t('admin.unsaved.confirm', 'You have unsaved changes. Leave without saving?'))) return;
        }

        var btns = Array.prototype.slice.call(page.querySelectorAll('.npnp-tab-btn'));
        btns.forEach(function (b) {
            var isActive = b.getAttribute('data-tab') === tab;
            b.classList.toggle('active', isActive);
            b.setAttribute('aria-selected', isActive ? 'true' : 'false');
            b.tabIndex = isActive ? 0 : -1;
            if (isActive && moveFocus) { try { b.focus(); } catch (_) {} }
        });
        page.querySelectorAll('.npnp-tab-panel').forEach(function (p) {
            p.classList.toggle('active', p.id === 'npnpTab-' + tab);
        });
        try { sessionStorage.setItem('npnpAdminTab', tab); } catch (_) {}
        if (tab === 'activity') loadActivity(page);
        if (tab === 'diagnostics') loadDiagnostics(page);
        if (tab === 'promo') loadPromoCodes(page);
        if (tab === 'tiers') { loadTiers(page).then(function () { snapshotSavedState(); }); }
        if (tab === 'tags') { loadTags(page).then(function () { snapshotSavedState(); }); }
        if (tab === 'audit') loadAudit(page);
    }

    function bindTabs(page) {
        var tablist = page.querySelector('.npnp-tabs');
        if (tablist) tablist.setAttribute('aria-label', t('admin.tabs.aria', 'Sections'));
        var btns = Array.prototype.slice.call(page.querySelectorAll('.npnp-tab-btn'));
        btns.forEach(function (btn) {
            var tab = btn.getAttribute('data-tab');
            btn.setAttribute('role', 'tab');
            btn.id = 'npnpTabBtn-' + tab;
            btn.setAttribute('aria-controls', 'npnpTab-' + tab);
            var active = btn.classList.contains('active');
            btn.setAttribute('aria-selected', active ? 'true' : 'false');
            btn.tabIndex = active ? 0 : -1;

            var panel = page.querySelector('#npnpTab-' + tab);
            if (panel) {
                panel.setAttribute('role', 'tabpanel');
                panel.setAttribute('aria-labelledby', btn.id);
                panel.setAttribute('tabindex', '0');
            }

            btn.addEventListener('click', function () { activateTab(page, tab, false); });
            // Roving keyboard navigation (WAI-ARIA tabs pattern).
            btn.addEventListener('keydown', function (e) {
                var idx = btns.indexOf(btn);
                var target = null;
                if (e.key === 'ArrowRight' || e.key === 'ArrowDown') target = btns[(idx + 1) % btns.length];
                else if (e.key === 'ArrowLeft' || e.key === 'ArrowUp') target = btns[(idx - 1 + btns.length) % btns.length];
                else if (e.key === 'Home') target = btns[0];
                else if (e.key === 'End') target = btns[btns.length - 1];
                if (target) { e.preventDefault(); activateTab(page, target.getAttribute('data-tab'), true); }
            });
        });

        // Restore the last-used tab within the session (defaults to Members).
        var saved = null;
        try { saved = sessionStorage.getItem('npnpAdminTab'); } catch (_) {}
        if (saved && page.querySelector('#npnpTab-' + saved)) {
            activateTab(page, saved, false);
        }
    }

    /* --- tiers --- */

    function loadTiers(page) {
        return api().ajax({
            type: 'GET', url: api().getUrl('NoPayNoPlay/Tiers'), dataType: 'json'
        }).then(function (rows) {
            state.tiers = (rows || []).map(function (r) { return Object.assign({}, r); });
            renderTiers(page);
        }).catch(function () {});
    }

    function renderTiers(page) {
        var container = page.querySelector('#npnpTiersTable');
        if (!container) return;
        var rows = state.tiers || [];
        var head = '<table class="npnp-table"><thead><tr>'
            + '<th>' + escapeHtml(t('admin.tiers.col.months', 'Months')) + '</th>'
            + '<th>' + escapeHtml(t('admin.tiers.col.price', 'Price')) + '</th>'
            + '<th>' + escapeHtml(t('admin.tiers.col.label', 'Label')) + '</th>'
            + '<th>' + escapeHtml(t('admin.tiers.col.highlight', 'Highlight')) + '</th>'
            + '<th>' + escapeHtml(t('admin.tiers.col.actions', 'Actions')) + '</th>'
            + '</tr></thead><tbody>';
        var body = rows.map(function (r, idx) {
            return '<tr data-idx="' + idx + '">'
                + '<td><input is="emby-input" type="number" min="1" max="60" class="emby-input npnp-tier-months" value="' + (r.Months || 1) + '" /></td>'
                + '<td><input is="emby-input" type="number" min="0" step="0.01" class="emby-input npnp-tier-price" value="' + (r.Price || 0) + '" /></td>'
                + '<td><input is="emby-input" type="text" maxlength="64" class="emby-input npnp-tier-label" value="' + escapeHtml(r.Label || '') + '" /></td>'
                + '<td class="npnp-cell-center"><label class="emby-checkbox-label"><input is="emby-checkbox" type="checkbox" class="npnp-tier-highlight"' + (r.Highlight ? ' checked' : '') + ' /><span></span></label></td>'
                + '<td class="npnp-cell-actions"><button is="emby-button" type="button" class="button-alt npnp-tier-del npnp-icon-btn npnp-danger" title="' + escapeHtml(t('admin.tiers.delete', 'Delete')) + '" aria-label="' + escapeHtml(t('admin.tiers.delete', 'Delete')) + '">'
                + '<span class="material-icons" aria-hidden="true">delete</span></button></td>'
                + '</tr>';
        }).join('');
        container.innerHTML = head + (body || '') + '</tbody></table>';

        container.querySelectorAll('.npnp-tier-del').forEach(function (btn) {
            btn.addEventListener('click', function () {
                var idx = parseInt(btn.closest('tr').getAttribute('data-idx'), 10);
                state.tiers.splice(idx, 1);
                renderTiers(page);
            });
        });
    }

    function collectTiers(page) {
        var trs = page.querySelectorAll('#npnpTiersTable tbody tr');
        var out = [];
        Array.prototype.forEach.call(trs, function (tr, idx) {
            var existing = state.tiers[idx] || {};
            out.push({
                Id: existing.Id || '',
                Months: parseInt(tr.querySelector('.npnp-tier-months').value || '1', 10),
                Price: parseFloat(tr.querySelector('.npnp-tier-price').value || '0'),
                Label: tr.querySelector('.npnp-tier-label').value || '',
                Highlight: tr.querySelector('.npnp-tier-highlight').checked
            });
        });
        return out;
    }

    function bindTiersTab(page) {
        var addBtn = page.querySelector('#npnpTiersAdd');
        if (addBtn) {
            addBtn.addEventListener('click', function () {
                state.tiers = collectTiers(page);
                state.tiers.push({ Id: '', Months: 1, Price: 10, Label: '', Highlight: false });
                renderTiers(page);
            });
        }
        var saveBtn = page.querySelector('#npnpTiersSave');
        if (saveBtn) {
            saveBtn.addEventListener('click', function () {
                var payload = collectTiers(page);
                api().ajax({
                    type: 'PUT',
                    url: api().getUrl('NoPayNoPlay/Tiers'),
                    data: JSON.stringify(payload),
                    contentType: 'application/json',
                    dataType: 'json'
                }).then(function (rows) {
                    state.tiers = rows || [];
                    renderTiers(page);
                    snapshotSavedState();
                    flash(page, t('admin.tiers.saved', 'Tiers saved.'));
                });
            });
        }
    }

    /* --- tags --- */

    function loadTags(page) {
        return api().ajax({
            type: 'GET', url: api().getUrl('NoPayNoPlay/Tags'), dataType: 'json'
        }).then(function (rows) {
            state.tags = (rows || []).map(function (r) { return Object.assign({}, r); });
            renderTags(page);
        }).catch(function () {});
    }

    function renderTags(page) {
        var container = page.querySelector('#npnpTagsTable');
        if (!container) return;
        var rows = state.tags || [];
        var head = '<table class="npnp-table"><thead><tr>'
            + '<th>' + escapeHtml(t('admin.tags.col.key', 'Key')) + '</th>'
            + '<th>' + escapeHtml(t('admin.tags.col.label', 'Label')) + '</th>'
            + '<th>' + escapeHtml(t('admin.tags.col.color', 'Color')) + '</th>'
            + '<th>' + escapeHtml(t('admin.tags.col.priceOverride', 'Price override')) + '</th>'
            + '<th>' + escapeHtml(t('admin.tags.col.actions', 'Actions')) + '</th>'
            + '</tr></thead><tbody>';
        var body = rows.map(function (r, idx) {
            return '<tr data-idx="' + idx + '">'
                + '<td><input is="emby-input" type="text" maxlength="32" class="emby-input npnp-tag-key" value="' + escapeHtml(r.Key || '') + '" /></td>'
                + '<td><input is="emby-input" type="text" maxlength="64" class="emby-input npnp-tag-label" value="' + escapeHtml(r.Label || '') + '" /></td>'
                + '<td class="npnp-cell-center"><input type="color" class="npnp-tag-color" value="' + escapeHtml(r.Color || '#888888') + '" aria-label="' + escapeHtml(t('admin.tags.col.color', 'Color')) + '" /><span class="npnp-tag-color-hex" style="font-size:11px;opacity:.7;margin-left:4px;vertical-align:middle;font-family:monospace;">' + escapeHtml(r.Color || '#888888') + '</span></td>'
                + '<td><input is="emby-input" type="number" min="0" step="0.01" class="emby-input npnp-tag-price" value="' + (r.MonthlyPriceOverride || 0) + '" /></td>'
                + '<td class="npnp-cell-actions"><button is="emby-button" type="button" class="button-alt npnp-tag-del npnp-icon-btn npnp-danger" title="' + escapeHtml(t('admin.tags.delete', 'Delete')) + '" aria-label="' + escapeHtml(t('admin.tags.delete', 'Delete')) + '">'
                + '<span class="material-icons" aria-hidden="true">delete</span></button></td>'
                + '</tr>';
        }).join('');
        container.innerHTML = head + (body || '') + '</tbody></table>';
        container.querySelectorAll('.npnp-tag-del').forEach(function (btn) {
            btn.addEventListener('click', function () {
                var idx = parseInt(btn.closest('tr').getAttribute('data-idx'), 10);
                state.tags.splice(idx, 1);
                renderTags(page);
            });
        });
    }

    function collectTags(page) {
        var trs = page.querySelectorAll('#npnpTagsTable tbody tr');
        var out = [];
        Array.prototype.forEach.call(trs, function (tr, idx) {
            var existing = state.tags[idx] || {};
            out.push({
                Id: existing.Id || '',
                Key: tr.querySelector('.npnp-tag-key').value || '',
                Label: tr.querySelector('.npnp-tag-label').value || '',
                Color: tr.querySelector('.npnp-tag-color').value || '',
                MonthlyPriceOverride: parseFloat(tr.querySelector('.npnp-tag-price').value || '0')
            });
        });
        return out;
    }

    function bindTagsTab(page) {
        var addBtn = page.querySelector('#npnpTagsAdd');
        if (addBtn) {
            addBtn.addEventListener('click', function () {
                state.tags = collectTags(page);
                state.tags.push({ Id: '', Key: '', Label: '', Color: '#888888', MonthlyPriceOverride: 0 });
                renderTags(page);
            });
        }
        var saveBtn = page.querySelector('#npnpTagsSave');
        if (saveBtn) {
            saveBtn.addEventListener('click', function () {
                var payload = collectTags(page);
                api().ajax({
                    type: 'PUT',
                    url: api().getUrl('NoPayNoPlay/Tags'),
                    data: JSON.stringify(payload),
                    contentType: 'application/json',
                    dataType: 'json'
                }).then(function (rows) {
                    state.tags = rows || [];
                    renderTags(page);
                    snapshotSavedState();
                    flash(page, t('admin.tags.saved', 'Tags saved.'));
                });
            });
        }

        // Keep the hex label in sync with the color picker.
        var tagsTable = page.querySelector('#npnpTagsTable');
        if (tagsTable) {
            tagsTable.addEventListener('input', function (e) {
                var input = e.target;
                if (input.classList.contains('npnp-tag-color')) {
                    var hex = input.nextElementSibling;
                    if (hex && hex.classList.contains('npnp-tag-color-hex')) {
                        hex.textContent = input.value || '#888888';
                    }
                }
            });
        }
    }

    /* --- audit log --- */

    function loadAudit(page) {
        setBusy(page, '#npnpAuditTable');
        return api().ajax({
            type: 'GET', url: api().getUrl('NoPayNoPlay/AuditLog'), dataType: 'json'
        }).then(function (rows) {
            state.audit = rows || [];
            renderAudit(page);
        }).catch(function () {});
    }

    function renderAudit(page) {
        var container = page.querySelector('#npnpAuditTable');
        if (!container) return;
        var rows = state.audit || [];
        if (!rows.length) {
            container.innerHTML = '<div class="npnp-empty">' + escapeHtml(t('admin.audit.empty', 'No audit entry yet.')) + '</div>';
            return;
        }
        var head = '<table class="npnp-table"><thead><tr>'
            + '<th>' + escapeHtml(t('admin.audit.col.timestamp', 'Timestamp')) + '</th>'
            + '<th>' + escapeHtml(t('admin.audit.col.actor', 'Actor')) + '</th>'
            + '<th>' + escapeHtml(t('admin.audit.col.action', 'Action')) + '</th>'
            + '<th>' + escapeHtml(t('admin.audit.col.target', 'Target')) + '</th>'
            + '<th>' + escapeHtml(t('admin.audit.col.details', 'Details')) + '</th>'
            + '</tr></thead><tbody>';
        var body = rows.map(function (r) {
            return '<tr>'
                + '<td>' + escapeHtml(formatDate(r.Timestamp)) + '</td>'
                + '<td>' + escapeHtml(r.Actor || '') + '</td>'
                + '<td><code>' + escapeHtml(r.Action || '') + '</code></td>'
                + '<td>' + escapeHtml(r.TargetUsername || '') + '</td>'
                + '<td>' + escapeHtml(r.Details || '') + '</td>'
                + '</tr>';
        }).join('');
        container.innerHTML = head + body + '</tbody></table>';
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
        // Restore persisted filters before binding listeners.
        try {
            var saved = JSON.parse(localStorage.getItem('npnp.adminFilters') || '{}');
            if (saved && typeof saved === 'object') {
                if (typeof saved.userFilter === 'string') state.userFilter = saved.userFilter;
                if (typeof saved.stateFilter === 'string') state.stateFilter = saved.stateFilter;
                if (typeof saved.methodFilter === 'string') state.methodFilter = saved.methodFilter;
                var us = page.querySelector('#npnpUserSearch');
                var ss = page.querySelector('#npnpStateFilter');
                if (us) us.value = state.userFilter;
                if (ss) ss.value = state.stateFilter;
            }
        } catch (_) {}

        function persistFilters() {
            try {
                localStorage.setItem('npnp.adminFilters', JSON.stringify({
                    userFilter: state.userFilter || '',
                    stateFilter: state.stateFilter || '',
                    methodFilter: state.methodFilter || ''
                }));
            } catch (_) {}
        }

        var userSearchDebounce = null;
        page.querySelector('#npnpUserSearch').addEventListener('input', function (e) {
            state.userFilter = e.target.value || '';
            state.page = 1;
            persistFilters();
            clearTimeout(userSearchDebounce);
            userSearchDebounce = setTimeout(function () { renderUsers(page); }, 200);
        });
        page.querySelector('#npnpStateFilter').addEventListener('change', function (e) {
            state.stateFilter = e.target.value || '';
            state.page = 1;
            persistFilters();
            renderUsers(page);
        });
        var methodSel = page.querySelector('#npnpMethodFilter');
        if (methodSel) {
            methodSel.addEventListener('change', function (e) {
                state.methodFilter = e.target.value || '';
                state.page = 1;
                persistFilters();
                renderUsers(page);
            });
        }
        page.querySelector('#npnpActivitySearch').addEventListener('input', function (e) {
            state.activityFilter = e.target.value || '';
            renderActivity(page);
        });
        var fromEl = page.querySelector('#npnpActivityFrom');
        if (fromEl) fromEl.addEventListener('change', function (e) {
            state.activityFrom = e.target.value || '';
            renderActivity(page);
        });
        var toEl = page.querySelector('#npnpActivityTo');
        if (toEl) toEl.addEventListener('change', function (e) {
            state.activityTo = e.target.value || '';
            renderActivity(page);
        });

        var expU = page.querySelector('#npnpExportUsers');
        if (expU) expU.addEventListener('click', function () { downloadCsv('NoPayNoPlay/Users/Export.csv', 'nopaynoplay-users.csv'); });
        var expA = page.querySelector('#npnpExportActivity');
        if (expA) expA.addEventListener('click', function () { downloadCsv('NoPayNoPlay/Activity/Export.csv', 'nopaynoplay-activity.csv'); });

        bindBulkBar(page);
    }

    function downloadCsv(apiPath, filename) {
        // ApiClient.ajax with dataType=text bypasses JSON parsing and forwards the
        // current authentication headers; we then materialise a Blob for download.
        api().ajax({ type: 'GET', url: api().getUrl(apiPath), dataType: 'text' })
            .then(function (text) {
                var blob = new Blob([text], { type: 'text/csv;charset=utf-8' });
                var url = URL.createObjectURL(blob);
                var a = document.createElement('a');
                a.href = url;
                a.download = filename;
                a.style.display = 'none';
                document.body.appendChild(a);
                a.click();
                // Clean up after the download dialog has been handed off to the browser.
                setTimeout(function () { URL.revokeObjectURL(url); a.remove(); }, 100);
            })
            .catch(function () { Dashboard.alert(t('admin.export.error', 'Export failed.')); });
    }

    function selectedIds() {
        return Object.keys(state.selected);
    }

    function renderBulkBar(page) {
        var bar = page.querySelector('#npnpBulkBar');
        if (!bar) return;
        var ids = selectedIds();
        if (!ids.length) {
            bar.hidden = true;
            return;
        }
        bar.hidden = false;
        var tpl = t('admin.users.bulk.count', '{n} selected');
        bar.querySelector('.npnp-bulk-count').textContent = tpl.replace('{n}', String(ids.length));
    }

    function bindBulkBar(page) {
        var bar = page.querySelector('#npnpBulkBar');
        if (!bar) return;
        bar.querySelector('#npnpBulkClear').addEventListener('click', function () {
            state.selected = {};
            renderUsers(page);
        });
        bar.querySelector('#npnpBulkPay').addEventListener('click', function () {
            var ids = selectedIds();
            if (!ids.length) return;
            openBulkPaymentModal(page, ids);
        });
        bar.querySelector('#npnpBulkExempt').addEventListener('click', function () {
            var ids = selectedIds();
            if (!ids.length) return;
            // Use majority current state as toggle target: if any selected user is
            // not exempt, mark them all exempt; otherwise remove exemption.
            var anyNotExempt = ids.some(function (id) {
                var u = state.users.find(function (x) { return x.UserId === id; });
                return u && !u.IsExempt;
            });
            api().ajax({
                type: 'POST', url: api().getUrl('NoPayNoPlay/Users/BulkExempt'),
                data: JSON.stringify({ UserIds: ids, IsExempt: anyNotExempt }),
                contentType: 'application/json'
            }).then(function () { state.selected = {}; return loadUsers(page); });
        });
        bar.querySelector('#npnpBulkReset').addEventListener('click', function () {
            var ids = selectedIds();
            if (!ids.length) return;
            openConfirmModal(page,
                t('admin.users.action.reset', 'Reset trial'),
                t('admin.users.bulk.confirm.reset', 'Reset {n} member(s) back to a fresh trial?').replace('{n}', String(ids.length)),
                function () {
                    api().ajax({
                        type: 'POST', url: api().getUrl('NoPayNoPlay/Users/BulkReset'),
                        data: JSON.stringify({ UserIds: ids }),
                        contentType: 'application/json'
                    }).then(function () { state.selected = {}; return loadUsers(page); });
                });
        });
    }

    function openBulkPaymentModal(page, ids) {
        var defaultAmount = Number(state.settings.MonthlyPrice || 10).toFixed(2);
        var currency = state.settings.Currency || 'EUR';
        var body = ''
            + '<form id="npnpBulkPayForm">'
            + '<p>' + escapeHtml(t('admin.users.bulk.pay.intro', 'Recording the same payment for {n} member(s).').replace('{n}', String(ids.length))) + '</p>'
            + '<div class="inputContainer"><label for="npnpBulkAmount">' + escapeHtml(t('admin.payment.amount', 'Amount')) + ' (' + escapeHtml(currency) + ')</label>'
            + '<input is="emby-input" id="npnpBulkAmount" type="number" step="0.01" min="0" value="' + escapeHtml(defaultAmount) + '" /></div>'
            + '<div class="inputContainer"><label for="npnpBulkMonths">' + escapeHtml(t('admin.payment.months', 'Months')) + '</label>'
            + '<input is="emby-input" id="npnpBulkMonths" type="number" min="1" max="60" value="1" /></div>'
            + '<div class="inputContainer"><label for="npnpBulkMethod">' + escapeHtml(t('admin.payment.method', 'Method')) + '</label>'
            + '<select is="emby-select" id="npnpBulkMethod">'
            + '<option value="PayPal">PayPal</option><option value="Lydia">Lydia</option><option value="Bank">Bank</option><option value="Cash">Cash</option><option value="Other">Other</option>'
            + '</select></div>'
            + '</form>';
        var modal = buildModal(page,
            escapeHtml(t('admin.users.bulk.pay', 'Record payment')),
            body,
            '<button is="emby-button" type="button" class="button-alt" data-npnp-cancel>' + escapeHtml(t('common.cancel', 'Cancel')) + '</button>'
            + '<button is="emby-button" type="button" class="raised button-submit" data-npnp-confirm>OK</button>');
        modal.querySelector('[data-npnp-cancel]').addEventListener('click', function () { closeModal(page); });
        modal.querySelector('[data-npnp-confirm]').addEventListener('click', function () {
            var payload = {
                UserIds: ids,
                Amount: parseFloat(modal.querySelector('#npnpBulkAmount').value || '0'),
                MonthsAdded: parseInt(modal.querySelector('#npnpBulkMonths').value || '1', 10),
                Method: modal.querySelector('#npnpBulkMethod').value || 'Other',
                Note: ''
            };
            api().ajax({
                type: 'POST', url: api().getUrl('NoPayNoPlay/Users/BulkPay'),
                data: JSON.stringify(payload), contentType: 'application/json'
            }).then(function () {
                closeModal(page);
                state.selected = {};
                return Promise.all([loadUsers(page), loadActivity(page)]);
            });
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
                .then(function () { return Promise.all([loadSettings(page), loadTags(page), loadUsers(page), loadActivity(page), loadStatus(page)]); })
                .finally(function () { Dashboard.hideLoadingMsg(); });
        });

        bindTabs(page);
        bindFilters(page);
        bindTestMode(page);
        bindDiagnostics(page);
        bindPromoForm(page);
        bindTiersTab(page);
        bindTagsTab(page);

        // Warn before leaving the page when tiers/tags have unsaved edits.
        window.addEventListener('beforeunload', function (e) {
            if (hasUnsavedEdits()) {
                e.preventDefault();
                e.returnValue = '';
            }
        });

        page.querySelector('#npnpSettingsForm').addEventListener('submit', function (e) {
            e.preventDefault();
            var warnings = validateSettingsFields(page);
            if (warnings.length) {
                flash(page, warnings.join(' '), true);
                return false;
            }
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
