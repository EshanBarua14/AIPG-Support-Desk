/* XFL CSMS - page behaviour. Plain JavaScript, no libraries.
   Everything is opt-in through data-* attributes, so the views stay free of inline scripts:

     data-nav-toggle            open/close the side menu
     data-theme-toggle          light/dark
     data-menu                  button that opens the next .menu-panel
     data-peek                  show/hide the password in the same .input-wrap
     data-match="#id"           input must equal the other input (confirm password)
     data-list                  ticket / to-do list: live search, sorting and paging without a page reload
     data-filter="#tableId"     filter the rows of a small table while typing
     data-delete                delete through fetch after a confirmation (data-url, data-confirm, data-method)
     data-rte                   textarea -> small rich text editor
     data-drop                  file drop zone around <input type="file" multiple>
     data-report="#target"      report filter form: load the result into the target
     data-print / data-export-csv="#tableId"
     data-confirm-submit        form asks before submitting
     data-assign                button that opens the "assign ticket" dialog (data-url, data-ticket, data-current)
     data-role-choice           role select of the user forms: shows what the role may do
*/
(function () {
    'use strict';

    var doc = document;
    var root = doc.documentElement;
    var base = (doc.querySelector('meta[name="app-base"]') || {}).content || '/';
    var csrf = (doc.querySelector('meta[name="csrf-token"]') || {}).content || '';
    var sprite = (doc.querySelector('meta[name="icon-sprite"]') || {}).content || (base + 'img/icons.svg');

    function $(selector, scope) { return (scope || doc).querySelector(selector); }
    function $all(selector, scope) { return Array.prototype.slice.call((scope || doc).querySelectorAll(selector)); }
    function on(target, type, handler, options) { target.addEventListener(type, handler, options); }
    function escapeHtml(text) {
        return String(text == null ? '' : text).replace(/[&<>"']/g, function (c) {
            return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c];
        });
    }
    function icon(name) {
        return '<svg class="icon" aria-hidden="true"><use href="' + sprite + '#' + name + '"></use></svg>';
    }

    /* ---- requests ------------------------------------------------------ */
    function request(url, options) {
        options = options || {};
        var headers = options.headers || {};
        headers['X-Requested-With'] = 'XMLHttpRequest';
        if (options.method && options.method !== 'GET') { headers['RequestVerificationToken'] = csrf; }
        options.headers = headers;
        options.credentials = 'same-origin';
        return fetch(url, options).then(function (response) {
            if (response.status === 401) {
                // the session ended: back to the sign-in page
                window.location.href = base;
                throw new Error('signed out');
            }
            return response;
        });
    }

    /* ---- toasts + confirm dialog -------------------------------------- */
    function toast(message, bad) {
        var holder = $('.toasts');
        if (!holder) {
            holder = doc.createElement('div');
            holder.className = 'toasts';
            holder.setAttribute('role', 'status');
            holder.setAttribute('aria-live', 'polite');
            doc.body.appendChild(holder);
        }
        var item = doc.createElement('div');
        item.className = 'toast' + (bad ? ' bad' : '');
        item.innerHTML = icon(bad ? 'circle-alert' : 'check') + '<span>' + escapeHtml(message) + '</span>';
        holder.appendChild(item);
        setTimeout(function () { item.remove(); }, bad ? 7000 : 3500);
    }

    function confirmDialog(settings) {
        return new Promise(function (resolve) {
            if (typeof HTMLDialogElement === 'undefined') {
                resolve(window.confirm(settings.title + (settings.text ? '\n\n' + settings.text : '')));
                return;
            }
            var dialog = doc.createElement('dialog');
            dialog.className = 'dialog';
            dialog.innerHTML =
                '<div class="dialog-body"><h2>' + escapeHtml(settings.title) + '</h2>' +
                (settings.text ? '<p>' + escapeHtml(settings.text) + '</p>' : '') + '</div>' +
                '<div class="dialog-foot">' +
                '<button type="button" class="btn" value="no">' + escapeHtml(settings.cancel || 'Cancel') + '</button>' +
                '<button type="button" class="btn ' + (settings.danger ? 'btn-danger' : 'btn-primary') + '" value="yes">' +
                escapeHtml(settings.action || 'Confirm') + '</button></div>';
            doc.body.appendChild(dialog);
            var answer = false;
            on(dialog, 'click', function (event) {
                var button = event.target.closest('button');
                if (button) { answer = button.value === 'yes'; dialog.close(); }
            });
            on(dialog, 'close', function () { dialog.remove(); resolve(answer); });
            dialog.showModal();
            $('button[value="no"]', dialog).focus();
        });
    }

    /* ---- theme + side menu -------------------------------------------- */
    function store(key, value) { try { localStorage.setItem(key, value); } catch (e) { /* private mode */ } }
    function read(key) { try { return localStorage.getItem(key); } catch (e) { return null; } }

    on(doc, 'click', function (event) {
        var themeButton = event.target.closest('[data-theme-toggle]');
        if (themeButton) {
            var next = root.getAttribute('data-theme') === 'dark' ? 'light' : 'dark';
            root.setAttribute('data-theme', next);
            store('csms-theme', next);
            return;
        }

        var app = $('.app');
        var navButton = event.target.closest('[data-nav-toggle]');
        if (navButton && app) {
            if (window.matchMedia('(max-width: 960px)').matches) {
                app.classList.toggle('nav-open');
            } else {
                store('csms-nav', app.classList.toggle('nav-hidden') ? 'hidden' : 'shown');
            }
            return;
        }
        if (app && event.target.closest('.backdrop')) { app.classList.remove('nav-open'); }

        // drop-down menus
        var menuButton = event.target.closest('[data-menu]');
        $all('.menu-panel').forEach(function (panel) {
            var owner = panel.previousElementSibling;
            if (menuButton && owner === menuButton) {
                panel.hidden = !panel.hidden;
                menuButton.setAttribute('aria-expanded', String(!panel.hidden));
            } else if (!panel.contains(event.target)) {
                panel.hidden = true;
                if (owner) { owner.setAttribute('aria-expanded', 'false'); }
            }
        });

        var peek = event.target.closest('[data-peek]');
        if (peek) {
            var field = $('input', peek.closest('.input-wrap'));
            var show = field.type === 'password';
            field.type = show ? 'text' : 'password';
            peek.innerHTML = icon(show ? 'eye-off' : 'eye');
            peek.setAttribute('aria-label', show ? 'Hide password' : 'Show password');
            return;
        }

        if (event.target.closest('[data-print]')) { window.print(); return; }

        var exportButton = event.target.closest('[data-export-csv]');
        if (exportButton) { exportCsv($(exportButton.getAttribute('data-export-csv')), exportButton.getAttribute('data-file') || 'report'); return; }

        var deleteButton = event.target.closest('[data-delete]');
        if (deleteButton) { event.preventDefault(); removeThroughRequest(deleteButton); }
    });

    on(doc, 'keydown', function (event) {
        if (event.key !== 'Escape') { return; }
        $all('.menu-panel').forEach(function (panel) { panel.hidden = true; });
        var app = $('.app');
        if (app) { app.classList.remove('nav-open'); }
    });

    /* ---- delete with confirmation ------------------------------------- */
    function removeThroughRequest(button) {
        confirmDialog({
            title: button.getAttribute('data-confirm') || 'Delete this item?',
            text: button.getAttribute('data-confirm-text') || 'This cannot be undone.',
            action: button.getAttribute('data-action') || 'Delete',
            danger: true
        }).then(function (yes) {
            if (!yes) { return; }
            var method = button.getAttribute('data-method') || 'DELETE';
            var options = { method: method };
            var body = button.getAttribute('data-body');
            if (body) {
                options.body = body;
                options.headers = { 'Content-Type': 'application/x-www-form-urlencoded' };
            }
            button.classList.add('is-busy');
            request(button.getAttribute('data-url'), options).then(function (response) {
                if (response.ok) {
                    var row = button.closest(button.getAttribute('data-remove') || 'tr');
                    var list = button.closest('[data-list]');
                    if (list) { reloadList(list, window.location.href); }
                    else if (row) { removeRow(row); }
                    toast(button.getAttribute('data-done') || 'Deleted.');
                    return;
                }
                return response.text().then(function (message) {
                    button.classList.remove('is-busy');
                    toast(message && message.length < 300 ? message : 'The item could not be deleted.', true);
                });
            }).catch(function (error) {
                button.classList.remove('is-busy');
                if (!error || error.message !== 'signed out') { toast('The server could not be reached. Nothing was deleted.', true); }
            });
        });
    }

    // take a row out of a small table and keep its "3 of 12" counter right
    function removeRow(row) {
        var card = row.closest('.card');
        row.remove();
        if (!card) { return; }
        var total = $('[data-total]', card);
        if (total) { total.textContent = Math.max(0, parseInt(total.textContent, 10) - 1); }
        var filter = $('[data-filter]', card);
        if (filter) { filter.dispatchEvent(new Event('input')); }
    }

    /* ---- lists: search, sort and paging without a page reload ---------- */
    function reloadList(list, url) {
        var region = $('[data-list-region]', list);
        // Answers can arrive out of order while somebody types; only the newest request may update the list.
        var ticket = (list.reloadTicket || 0) + 1;
        list.reloadTicket = ticket;
        return request(url).then(function (response) {
            if (!response.ok) { throw new Error('status ' + response.status); }
            return response.text();
        }).then(function (html) {
            if (list.reloadTicket !== ticket) { return; }
            var fresh = new DOMParser().parseFromString(html, 'text/html').querySelector('[data-list-region]');
            if (!fresh) { return; }
            region.innerHTML = fresh.innerHTML;
            history.replaceState(null, '', url);
        }).catch(function (error) {
            if (list.reloadTicket === ticket && (!error || error.message !== 'signed out')) {
                toast('The list could not be refreshed. Check the connection and try again.', true);
            }
        });
    }

    function listUrl(list, page) {
        var region = $('[data-list-region]', list);
        var state = $('[data-sort-field]', region);
        var params = new URLSearchParams();
        params.set('page', page || 1);
        var size = $('[data-list-size]', list);
        if (size) { params.set('rowperpage', size.value); }
        var search = $('[data-list-search]', list);
        if (search && search.value.trim()) { params.set('searchString', search.value.trim()); }
        if (state && state.getAttribute('data-sort-field')) {
            params.set('sortField', state.getAttribute('data-sort-field'));
            params.set('sortAscending', state.getAttribute('data-sort-ascending'));
        }
        return list.getAttribute('data-url') + '?' + params.toString();
    }

    $all('[data-list]').forEach(function (list) {
        var timer;
        var search = $('[data-list-search]', list);
        if (search) {
            on(search, 'input', function () {
                clearTimeout(timer);
                timer = setTimeout(function () { reloadList(list, listUrl(list, 1)); }, 220);
            });
            // Enter must not submit anything; the list already follows the text
            on(search, 'keydown', function (event) { if (event.key === 'Enter') { event.preventDefault(); } });
        }
        var size = $('[data-list-size]', list);
        if (size) { on(size, 'change', function () { reloadList(list, listUrl(list, 1)); }); }
        on(list, 'click', function (event) {
            var link = event.target.closest('a[data-list-link]');
            if (!link || event.ctrlKey || event.metaKey || event.shiftKey) { return; }
            event.preventDefault();
            reloadList(list, link.href);
        });
    });

    /* ---- small tables: filter while typing ------------------------------ */
    $all('[data-filter]').forEach(function (input) {
        var table = $(input.getAttribute('data-filter'));
        if (!table) { return; }
        var counter = $(input.getAttribute('data-count') || '#none');
        on(input, 'input', function () {
            var text = input.value.trim().toLowerCase();
            var shown = 0;
            $all('tbody tr', table).forEach(function (row) {
                if (row.hasAttribute('data-empty')) { return; }
                var match = !text || row.textContent.toLowerCase().indexOf(text) >= 0;
                row.hidden = !match;
                if (match) { shown++; }
            });
            var empty = $('tr[data-empty]', table);
            if (empty) { empty.hidden = shown > 0; }
            if (counter) { counter.textContent = shown; }
        });
    });

    /* ---- assign a ticket (dialog rendered by Views/Shared/_AssignDialog.cshtml) ---- */
    on(doc, 'click', function (event) {
        var opener = event.target.closest('[data-assign]');
        var dialog = $('#assignDialog');
        if (opener && dialog && dialog.showModal) {
            var form = $('form', dialog);
            var select = $('select', dialog);
            var current = opener.getAttribute('data-current') || '';
            form.action = opener.getAttribute('data-url');
            $('#assignTitle', dialog).textContent = 'Assign ticket ' + (opener.getAttribute('data-ticket') || '');
            // a ticket nobody has: an engineer must be chosen. A ticket somebody has: "Unassigned" takes it away.
            var holder = opener.getAttribute('data-current-name') || '';
            select.options[0].textContent = holder ? 'Unassigned' : 'Choose an engineer';
            select.required = !holder;
            select.value = current;
            if (select.value !== current) { select.value = ''; }
            dialog.showModal();
            select.focus();
            return;
        }
        var closer = event.target.closest('dialog [data-close]');
        if (closer) { closer.closest('dialog').close(); }
    });

    /* ---- user forms: what the chosen role may do ------------------------ */
    on(doc, 'change', function (event) {
        var choice = event.target.closest('[data-role-choice]');
        var note = $('#RoleSummary');
        if (!choice || !note) { return; }
        var option = choice.options[choice.selectedIndex];
        note.textContent = option ? option.getAttribute('data-summary') || '' : '';
    });

    /* ---- forms ---------------------------------------------------------- */
    $all('[data-match]').forEach(function (input) {
        var other = $(input.getAttribute('data-match'));
        if (!other) { return; }
        function check() { input.setCustomValidity(input.value && input.value !== other.value ? 'The two passwords do not match.' : ''); }
        on(input, 'input', check);
        on(other, 'input', check);
    });

    on(doc, 'submit', function (event) {
        var form = event.target;
        if (form.hasAttribute('data-report')) { return; }

        if (form.hasAttribute('data-confirm-submit') && !form.confirmed) {
            event.preventDefault();
            confirmDialog({
                title: form.getAttribute('data-confirm-submit'),
                text: form.getAttribute('data-confirm-text') || '',
                action: form.getAttribute('data-action') || 'Confirm',
                danger: form.hasAttribute('data-danger')
            }).then(function (yes) {
                if (yes) { form.confirmed = true; form.requestSubmit ? form.requestSubmit() : form.submit(); }
            });
            return;
        }

        // a second click must not send the form twice
        setTimeout(function () {
            $all('button[type="submit"], input[type="submit"]', form).forEach(function (button) { button.classList.add('is-busy'); });
        }, 0);
    });
    // coming back with the browser's Back button: buttons must work again
    on(window, 'pageshow', function () { $all('.is-busy').forEach(function (button) { button.classList.remove('is-busy'); }); });

    /* ---- rich text ------------------------------------------------------ */
    var RTE_ACTIONS = [
        ['bold', 'bold', 'Bold'], ['italic', 'italic', 'Italic'], ['underline', 'underline', 'Underline'], null,
        ['insertUnorderedList', 'list', 'Bulleted list'], ['insertOrderedList', 'list-ordered', 'Numbered list'], null,
        ['createLink', 'link', 'Link'], ['removeFormat', 'undo-2', 'Clear formatting']
    ];

    $all('textarea[data-rte]').forEach(function (area) {
        var wrap = doc.createElement('div');
        wrap.className = 'rte';
        var bar = doc.createElement('div');
        bar.className = 'rte-bar';
        bar.setAttribute('role', 'toolbar');
        bar.setAttribute('aria-label', 'Formatting');
        RTE_ACTIONS.forEach(function (action) {
            if (!action) { bar.insertAdjacentHTML('beforeend', '<span class="sep"></span>'); return; }
            bar.insertAdjacentHTML('beforeend',
                '<button type="button" class="btn btn-icon" data-command="' + action[0] + '" title="' + action[2] + '" aria-label="' + action[2] + '">' + icon(action[1]) + '</button>');
        });
        var body = doc.createElement('div');
        body.className = 'rte-body prose';
        body.contentEditable = 'true';
        body.setAttribute('role', 'textbox');
        body.setAttribute('aria-multiline', 'true');
        body.setAttribute('aria-label', area.getAttribute('aria-label') || 'Details');
        body.setAttribute('data-placeholder', area.getAttribute('placeholder') || '');
        body.innerHTML = area.value; // already sanitised on the server
        wrap.appendChild(bar);
        wrap.appendChild(body);
        area.hidden = true;
        area.parentNode.insertBefore(wrap, area);

        // the visible label belongs to the editor now, not to the hidden textarea
        var label = area.id ? $('label[for="' + area.id + '"]') : null;
        if (label) {
            if (!label.id) { label.id = area.id + 'Label'; }
            label.removeAttribute('for');
            body.removeAttribute('aria-label');
            body.setAttribute('aria-labelledby', label.id);
            on(label, 'click', function () { body.focus(); });
        }

        function sync() {
            // "empty" = nothing to read and nothing to look at (an editor that was typed in and cleared still holds <br>)
            var empty = !body.textContent.trim() && !$('img, table, hr, li', body);
            body.classList.toggle('is-empty', empty);
            area.value = empty ? '' : body.innerHTML;
        }
        sync();
        function marks() {
            $all('[data-command]', bar).forEach(function (button) {
                var command = button.getAttribute('data-command');
                var active = false;
                try { active = /^(bold|italic|underline|insert)/.test(command) && doc.queryCommandState(command); } catch (e) { /* not supported */ }
                button.classList.toggle('on', !!active);
            });
        }
        on(bar, 'mousedown', function (event) { event.preventDefault(); }); // keep the selection
        on(bar, 'click', function (event) {
            var button = event.target.closest('[data-command]');
            if (!button) { return; }
            var command = button.getAttribute('data-command');
            body.focus();
            if (command === 'createLink') {
                var url = window.prompt('Link address', 'https://');
                if (!url || !/^(https?:\/\/|mailto:)\S+$/i.test(url)) { return; }
                var selection = window.getSelection();
                if (selection && selection.isCollapsed) {
                    // nothing selected: put the address in as the link text
                    doc.execCommand('insertHTML', false, '<a href="' + escapeHtml(url) + '">' + escapeHtml(url) + '</a>');
                } else {
                    doc.execCommand(command, false, url);
                }
            } else {
                doc.execCommand(command, false, null);
            }
            sync();
            marks();
        });
        // paste as plain text: no styles, scripts or tracking pixels from other pages
        on(body, 'paste', function (event) {
            event.preventDefault();
            var text = (event.clipboardData || window.clipboardData).getData('text/plain');
            doc.execCommand('insertText', false, text);
        });
        on(body, 'input', sync);
        on(body, 'keyup', marks);
        on(body, 'mouseup', marks);
        if (area.form) { on(area.form, 'submit', sync); }
    });

    /* ---- file drop zone ------------------------------------------------- */
    function fileSize(bytes) {
        if (bytes < 1024) { return bytes + ' B'; }
        if (bytes < 1048576) { return (bytes / 1024).toFixed(0) + ' KB'; }
        return (bytes / 1048576).toFixed(1) + ' MB';
    }

    $all('[data-drop]').forEach(function (zone) {
        var input = $('input[type="file"]', zone);
        var list = $(zone.getAttribute('data-drop') || '#none') || zone.nextElementSibling;
        var allowed = (zone.getAttribute('data-accept') || '').toLowerCase().split(',').filter(Boolean);
        var canEdit = typeof DataTransfer !== 'undefined';
        var chosen = canEdit ? new DataTransfer() : null;

        function draw() {
            if (!list) { return; }
            list.innerHTML = '';
            Array.prototype.forEach.call(input.files, function (file, index) {
                list.insertAdjacentHTML('beforeend',
                    '<li class="file">' + icon('paperclip') + '<span class="file-name">' + escapeHtml(file.name) + '</span>' +
                    '<span class="file-meta">' + fileSize(file.size) + '</span>' +
                    (canEdit ? '<button type="button" class="btn btn-icon btn-sm danger" data-index="' + index + '" aria-label="Remove ' + escapeHtml(file.name) + '">' + icon('x') + '</button>' : '') +
                    '</li>');
            });
        }
        function isChosen(file) {
            return !!chosen && Array.prototype.some.call(chosen.files, function (old) {
                return old.name === file.name && old.size === file.size && old.lastModified === file.lastModified;
            });
        }
        function add(files) {
            var refused = [];
            Array.prototype.forEach.call(files, function (file) {
                var extension = file.name.indexOf('.') >= 0 ? file.name.split('.').pop().toLowerCase() : '';
                if (allowed.length && allowed.indexOf(extension) < 0) { refused.push(file.name); return; }
                if (chosen && !isChosen(file)) { chosen.items.add(file); } // the same file twice is one attachment
            });
            if (chosen) { input.files = chosen.files; }
            if (refused.length) {
                toast('Not attached (file type not allowed): ' + refused.join(', '), true);
                if (!chosen) { input.value = ''; }
            }
            draw();
        }
        // "change" replaces the browser's selection; add() merges it into what was chosen before
        on(input, 'change', function () { add(Array.prototype.slice.call(input.files)); });
        ['dragenter', 'dragover'].forEach(function (type) {
            on(zone, type, function (event) { event.preventDefault(); zone.classList.add('over'); });
        });
        ['dragleave', 'drop'].forEach(function (type) {
            on(zone, type, function (event) { event.preventDefault(); zone.classList.remove('over'); });
        });
        on(zone, 'drop', function (event) { if (canEdit && event.dataTransfer) { add(event.dataTransfer.files); } });
        if (list) {
            on(list, 'click', function (event) {
                var button = event.target.closest('[data-index]');
                if (!button || !chosen) { return; }
                chosen.items.remove(Number(button.getAttribute('data-index')));
                input.files = chosen.files;
                draw();
            });
        }
    });

    /* ---- reports -------------------------------------------------------- */
    $all('form[data-report]').forEach(function (form) {
        var target = $(form.getAttribute('data-report'));
        on(form, 'submit', function (event) {
            event.preventDefault();
            var query = new URLSearchParams(new FormData(form)).toString();
            var button = $('button[type="submit"]', form);
            if (button) { button.classList.add('is-busy'); }
            request(form.action + '?' + query).then(function (response) {
                return response.text().then(function (html) {
                    if (!response.ok) { toast('The report could not be created. Please try again.', true); return; }
                    target.innerHTML = html;
                    $all('[data-needs-report]').forEach(function (element) { element.hidden = false; });
                    target.scrollIntoView({ behavior: 'smooth', block: 'start' });
                });
            }).catch(function () { /* handled in request() */ }).then(function () {
                if (button) { button.classList.remove('is-busy'); }
            });
        });
    });

    function exportCsv(table, name) {
        if (!table) { return; }
        var lines = $all('tr', table).map(function (row) {
            return $all('th, td', row).map(function (cell) {
                var text = (cell.hasAttribute('data-export') ? cell.getAttribute('data-export') : cell.innerText).replace(/\s*\n\s*/g, ' ').trim();
                // a leading = + - @ would be run as a formula by spreadsheet programs
                if (/^[=+\-@]/.test(text)) { text = "'" + text; }
                return '"' + text.replace(/"/g, '""') + '"';
            }).join(',');
        });
        var blob = new Blob(['﻿' + lines.join('\r\n')], { type: 'text/csv;charset=utf-8' });
        var link = doc.createElement('a');
        link.href = URL.createObjectURL(blob);
        link.download = name + '-' + new Date().toISOString().slice(0, 10) + '.csv';
        doc.body.appendChild(link);
        link.click();
        link.remove();
        setTimeout(function () { URL.revokeObjectURL(link.href); }, 1000);
    }

    // for the few page-specific scripts in the views
    window.Csms = { toast: toast, confirm: confirmDialog, request: request, icon: icon };
})();
