/* Xpert CSMS - support desk pages (version 3.3). Loaded after app.js; plain JavaScript.
     data-reply            the reply form of a ticket (data-name, data-ticket, data-me fill the canned replies)
     data-canned           <select> of canned replies: the chosen text goes into the reply box
     data-note-toggle      checkbox "internal note": the form shows that only XFL staff will read it
     data-auto-submit      <select> that submits its form when changed (the house filter of the service report) */
(function () {
    'use strict';

    var doc = document;

    Array.prototype.forEach.call(doc.querySelectorAll('[data-reply]'), function (form) {
        var area = form.querySelector('textarea[name="body"]');
        var canned = form.querySelector('[data-canned]');
        var note = form.querySelector('[data-note-toggle]');
        var send = form.querySelector('[data-reply-send] span');
        var label = area && area.id ? doc.getElementById(area.id + 'Label') || form.querySelector('label[for="' + area.id + '"]') : null;
        var replyLabel = label ? label.textContent : '';

        function editor() { return form.querySelector('.rte-body'); }

        function escapeHtml(text) {
            return String(text == null ? '' : text).replace(/[&<>"']/g, function (c) {
                return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c];
            });
        }

        if (canned) {
            canned.addEventListener('change', function () {
                var option = canned.options[canned.selectedIndex];
                var html = option && option.getAttribute('data-body');
                if (!html) { return; }
                // the placeholders of the text; values are escaped, the text itself was cleaned on the server
                html = html.replace(/\{name\}/g, escapeHtml(form.getAttribute('data-name') || ''))
                    .replace(/\{ticket\}/g, escapeHtml(form.getAttribute('data-ticket') || ''))
                    .replace(/\{me\}/g, escapeHtml(form.getAttribute('data-me') || ''));
                var body = editor();
                if (body) {
                    // after what is written already, never instead of it
                    var empty = !body.textContent.trim();
                    body.innerHTML = empty ? html : body.innerHTML + html;
                    body.dispatchEvent(new Event('input', { bubbles: true }));
                    body.focus();
                } else if (area) {
                    area.value = (area.value ? area.value + '\n' : '') + html;
                }
                canned.selectedIndex = 0;
            });
        }

        if (note) {
            var show = function () {
                form.classList.toggle('is-note', note.checked);
                if (send) { send.textContent = note.checked ? 'Add internal note' : 'Send reply'; }
                if (label) { label.textContent = note.checked ? 'Internal note (XFL staff only)' : replyLabel; }
            };
            note.addEventListener('change', show);
            show();
        }
    });

    Array.prototype.forEach.call(doc.querySelectorAll('select[data-auto-submit]'), function (select) {
        select.addEventListener('change', function () { if (select.form) { select.form.submit(); } });
    });
})();
