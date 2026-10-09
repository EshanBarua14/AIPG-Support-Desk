/* AIPG Support Desk - support desk pages (version 3.3). Loaded after app.js; plain JavaScript.
     data-reply            the reply form of a ticket (data-name, data-ticket, data-me fill the canned replies)
     data-canned           <select> of canned replies: the chosen text goes into the reply box
     data-note-toggle      checkbox "internal note": the form shows that only AIPG staff will read it
     data-auto-submit      <select> that submits its form when changed (the house filter of the service report)
   Version 3.4:
     data-kb-ask           title field of a new ticket: asks this address for articles that fit what is typed
                           (data-kb-product = the product <select>, data-kb-into = the box that lists them)
     data-kb-insert        button next to a related article: puts a link to it into the reply box */
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
                if (label) { label.textContent = note.checked ? 'Internal note (AIPG staff only)' : replyLabel; }
            };
            note.addEventListener('change', show);
            show();
        }
    });

    // ---- knowledge base: suggestions while the title of a ticket is typed ----
    Array.prototype.forEach.call(doc.querySelectorAll('[data-kb-ask]'), function (input) {
        var box = doc.querySelector(input.getAttribute('data-kb-into'));
        var list = box && box.querySelector('ul');
        var product = doc.querySelector(input.getAttribute('data-kb-product') || '#none');
        var address = input.getAttribute('data-kb-ask');
        if (!box || !list || !address || !window.fetch) { return; }

        var timer = null, lastAsked = '', counter = 0;

        function show(items) {
            while (list.firstChild) { list.removeChild(list.firstChild); }
            items.forEach(function (item) {
                var li = doc.createElement('li');
                var link = doc.createElement('a');
                link.href = item.url; link.target = '_blank'; link.rel = 'noopener';
                link.textContent = item.title;                       // text, never markup
                var text = doc.createElement('span');
                text.textContent = item.text || '';
                li.appendChild(link); li.appendChild(text);
                list.appendChild(li);
            });
            box.hidden = items.length === 0;
        }

        function ask() {
            var words = input.value.trim();
            var key = words + '|' + (product ? product.value : '');
            if (key === lastAsked) { return; }
            lastAsked = key;
            if (words.length < 6) { show([]); return; }
            var mine = ++counter;
            var url = address + '?q=' + encodeURIComponent(words) + (product && product.value ? '&productId=' + encodeURIComponent(product.value) : '');
            fetch(url, { credentials: 'same-origin', headers: { 'X-Requested-With': 'XMLHttpRequest', 'Accept': 'application/json' } })
                .then(function (response) { return response.ok ? response.json() : []; })
                .then(function (items) { if (mine === counter) { show(Array.isArray(items) ? items : []); } })
                .catch(function () { /* a suggestion is never worth a message */ });
        }

        input.addEventListener('input', function () { clearTimeout(timer); timer = setTimeout(ask, 450); });
        input.addEventListener('blur', ask);
        if (product) { product.addEventListener('change', ask); }
    });

    // ---- knowledge base: a link to an article goes into the reply ----
    Array.prototype.forEach.call(doc.querySelectorAll('[data-kb-insert]'), function (button) {
        button.addEventListener('click', function () {
            var form = doc.querySelector('[data-reply]');
            if (!form) { return; }
            var link = doc.createElement('a');
            link.setAttribute('href', button.getAttribute('data-href'));
            link.textContent = button.getAttribute('data-title') || 'the article';
            var paragraph = doc.createElement('p');
            paragraph.appendChild(doc.createTextNode('This article may help: '));
            paragraph.appendChild(link);
            var body = form.querySelector('.rte-body');
            var area = form.querySelector('textarea[name="body"]');
            if (body) {
                if (!body.textContent.trim()) { body.innerHTML = ''; }
                body.appendChild(paragraph);
                body.dispatchEvent(new Event('input', { bubbles: true }));
                body.focus();
            } else if (area) {
                area.value = (area.value ? area.value + '\n' : '') + paragraph.outerHTML;
            }
            form.scrollIntoView({ block: 'center' });
        });
    });

    Array.prototype.forEach.call(doc.querySelectorAll('select[data-auto-submit]'), function (select) {
        select.addEventListener('change', function () { if (select.form) { select.form.submit(); } });
    });
})();
