/* Xpert CSMS - charts and the dashboard. Plain JavaScript and SVG, no libraries.

   A chart is an element with data-chart="trend | split | columns" inside a container that carries the numbers:

     <div data-charts>
         <script type="application/json" data-chart-data>{ ...Models/Admin/TicketCharts as JSON... }</script>
         <div data-chart="trend"   data-key="trend"></div>        raised and closed over time (two lines)
         <div data-chart="split"   data-key="products"></div>     bars split into not closed / closed
         <div data-chart="columns" data-key="closeTimes"></div>   one column per group
         <b data-stat="raised"></b>                               a number of the data (counts up)
     </div>

   Every chart has a table with the same numbers (the button with data-chart-view switches), a legend when it shows
   two series, and a tooltip on hover and keyboard focus. Colours are the tokens --series-open / --series-closed of
   wwwroot/css/app.css (checked as a pair for both themes); text never takes the colour of a series.

   The dashboard (data-dashboard) adds: the period (data-period), and widgets that can be hidden, moved and made
   wide or narrow - remembered per person in this browser (localStorage "csms-dash:<user>:<role>").
*/
(function () {
    'use strict';

    var doc = document;
    var SVG = 'http://www.w3.org/2000/svg';
    var still = window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches;

    function $(selector, scope) { return (scope || doc).querySelector(selector); }
    function $all(selector, scope) { return Array.prototype.slice.call((scope || doc).querySelectorAll(selector)); }
    function el(tag, className, text) {
        var node = doc.createElement(tag);
        if (className) { node.className = className; }
        if (text != null) { node.textContent = text; }
        return node;
    }
    function svg(tag, attributes) {
        var node = doc.createElementNS(SVG, tag);
        Object.keys(attributes || {}).forEach(function (name) { node.setAttribute(name, attributes[name]); });
        return node;
    }
    function number(value) { return Number(value || 0).toLocaleString('en-US'); }
    function plural(count, one, many) { return number(count) + ' ' + (count === 1 ? one : many); }
    function read(key) { try { return localStorage.getItem(key); } catch (e) { return null; } }
    function store(key, value) { try { localStorage.setItem(key, value); } catch (e) { /* private mode */ } }

    /* ---- the tooltip (one for the page) ----------------------------------- */
    var tip = null;
    function showTip(title, rows, x, y) {
        if (!tip) { tip = el('div', 'chart-tip'); tip.setAttribute('role', 'status'); doc.body.appendChild(tip); }
        tip.textContent = '';
        if (title) { tip.appendChild(el('div', 'chart-tip-title', title)); }
        rows.forEach(function (row) {
            var line = el('div', 'chart-tip-row');
            if (row.key) { var key = el('i', 'key ' + row.key); line.appendChild(key); }
            line.appendChild(el('b', null, row.value));          // the value leads, the name follows
            line.appendChild(el('span', null, row.name));
            tip.appendChild(line);
        });
        tip.hidden = false;
        var box = tip.getBoundingClientRect();
        var left = Math.min(Math.max(8, x + 14), window.innerWidth - box.width - 8);
        var top = y - box.height - 12;
        if (top < 8) { top = y + 18; }
        tip.style.left = left + 'px';
        tip.style.top = top + 'px';
    }
    function hideTip() { if (tip) { tip.hidden = true; } }
    window.addEventListener('scroll', hideTip, true);

    /* ---- a table with the numbers of a chart -------------------------------- */
    function table(head, rows) {
        var wrap = el('div', 'table-wrap chart-table');
        var node = el('table', 'table compact');
        var thead = el('thead'); var tr = el('tr');
        head.forEach(function (text, index) { var th = el('th', index ? 't-right' : null, text); th.scope = 'col'; tr.appendChild(th); });
        thead.appendChild(tr); node.appendChild(thead);
        var body = el('tbody');
        rows.forEach(function (cells) {
            var line = el('tr');
            cells.forEach(function (text, index) {
                var cell = el(index ? 'td' : 'th', index ? 't-right num' : null, text);
                if (!index) { cell.scope = 'row'; cell.style.fontWeight = '500'; }
                line.appendChild(cell);
            });
            body.appendChild(line);
        });
        node.appendChild(body); wrap.appendChild(node);
        return wrap;
    }

    function empty(text) {
        var node = el('div', 'empty small chart-empty');
        node.appendChild(el('span', null, text));
        return node;
    }

    function legend(items) {
        var node = el('div', 'legend chart-legend');
        items.forEach(function (item) {
            var entry = el('span');
            entry.appendChild(el('i', item.key));
            entry.appendChild(doc.createTextNode(item.name));
            node.appendChild(entry);
        });
        return node;
    }

    /* ---- raised and closed over time: two lines ----------------------------- */
    // Top of the axis: four equal steps of whole tickets (1, 2, 3, 4, 5, 6, 8, 10, 20, 25, ... each), never "2.5 tickets".
    function niceMax(value) {
        var step = Math.max(1, Math.ceil(value / 4));
        var power = Math.pow(10, Math.floor(Math.log10(step)));
        var nice = [1, 2, 2.5, 3, 4, 5, 6, 8, 10];
        for (var i = 0; i < nice.length; i++) {
            var candidate = nice[i] * power;
            if (candidate >= step && candidate === Math.round(candidate)) { return candidate * 4; }
        }
        return 10 * power * 4;
    }

    function trend(host, points, animate) {
        if (host.chartObserver) { host.chartObserver.disconnect(); host.chartObserver = null; }   // of the chart drawn here before
        host.textContent = '';
        if (!points || !points.length || !points.some(function (p) { return p.raised || p.closed; })) {
            host.appendChild(empty('No tickets were raised or closed in this period.'));
            return;
        }

        var series = [
            { key: 'raised', name: 'Raised', css: 's-open' },
            { key: 'closed', name: 'Closed', css: 's-closed' }
        ];
        host.appendChild(legend(series.map(function (s) { return { key: 'line ' + s.css, name: s.name }; })));

        var plot = el('div', 'chart-plot');
        host.appendChild(plot);
        host.appendChild(table(['Period', 'Raised', 'Closed'], points.map(function (p) { return [p.title, number(p.raised), number(p.closed)]; })));

        function draw(first) {
            plot.textContent = '';
            var width = Math.max(280, plot.clientWidth);
            var height = 230;
            var pad = { left: 34, right: 40, top: 12, bottom: 26 };
            var innerW = width - pad.left - pad.right;
            var innerH = height - pad.top - pad.bottom;
            var top = niceMax(Math.max.apply(null, points.map(function (p) { return Math.max(p.raised, p.closed); })));
            var stepX = points.length > 1 ? innerW / (points.length - 1) : 0;
            function x(index) { return pad.left + (points.length > 1 ? index * stepX : innerW / 2); }
            function y(value) { return pad.top + innerH - (value / top) * innerH; }

            var root = svg('svg', { viewBox: '0 0 ' + width + ' ' + height, width: width, height: height, role: 'img', tabindex: '0', 'class': 'chart-svg' });
            root.setAttribute('aria-label', 'Tickets raised and closed per ' + (host.getAttribute('data-bucket') || 'day') + '. Use the arrow keys to read the values; the table shows all of them.');

            // grid and the numbers of the axis: hairlines, one step off the surface
            for (var tick = 0; tick <= 4; tick++) {
                var value = top / 4 * tick;
                root.appendChild(svg('line', { x1: pad.left, x2: width - pad.right, y1: y(value), y2: y(value), 'class': tick ? 'grid' : 'axis' }));
                var label = svg('text', { x: pad.left - 8, y: y(value) + 4, 'text-anchor': 'end', 'class': 'tick' });
                label.textContent = number(value);
                root.appendChild(label);
            }
            // dates under the axis: as many as fit
            var every = Math.max(1, Math.ceil(points.length / Math.max(2, Math.floor(innerW / 72))));
            var lastIndex = points.length - 1;
            points.forEach(function (point, index) {
                // every n-th point and the newest one; a regular label that would touch the newest gives way to it
                if (index !== lastIndex && (index % every !== 0 || (lastIndex - index) * stepX < 58)) { return; }
                var text = svg('text', { x: x(index), y: height - 6, 'text-anchor': index === 0 ? 'start' : index === points.length - 1 ? 'end' : 'middle', 'class': 'tick' });
                text.textContent = point.label;
                root.appendChild(text);
            });

            var ends = [];
            series.forEach(function (s) {
                var path = points.map(function (point, index) { return (index ? 'L' : 'M') + x(index).toFixed(1) + ' ' + y(point[s.key]).toFixed(1); }).join(' ');
                var line = svg('path', { d: path, 'class': 'line ' + s.css, pathLength: '1' });
                if (first && animate && !still) { line.classList.add('draw'); }
                root.appendChild(line);
                var last = points[points.length - 1];
                ends.push({ s: s, x: x(points.length - 1), y: y(last[s.key]), value: last[s.key] });
            });
            // the newest point of each line: a dot with a ring in the surface colour, and its value when the two do not collide
            var apart = Math.abs(ends[0].y - ends[1].y) >= 15;
            ends.forEach(function (end) {
                root.appendChild(svg('circle', { cx: end.x, cy: end.y, r: 4.5, 'class': 'dot ' + end.s.css }));
                if (apart) {
                    var text = svg('text', { x: end.x + 9, y: end.y + 4, 'class': 'end-label' });
                    text.textContent = number(end.value);
                    root.appendChild(text);
                }
            });

            // the crosshair finds the day; one tooltip lists both series
            var hair = svg('line', { y1: pad.top, y2: pad.top + innerH, 'class': 'hair', visibility: 'hidden' });
            root.appendChild(hair);
            var marks = series.map(function (s) { var dot = svg('circle', { r: 4.5, 'class': 'dot ' + s.css, visibility: 'hidden' }); root.appendChild(dot); return dot; });
            var current = -1;
            function point(index, clientX, clientY) {
                index = Math.max(0, Math.min(points.length - 1, index));
                current = index;
                hair.setAttribute('x1', x(index)); hair.setAttribute('x2', x(index)); hair.setAttribute('visibility', 'visible');
                series.forEach(function (s, i) {
                    marks[i].setAttribute('cx', x(index)); marks[i].setAttribute('cy', y(points[index][s.key])); marks[i].setAttribute('visibility', 'visible');
                });
                var box = root.getBoundingClientRect();
                showTip(points[index].title, series.map(function (s) { return { key: 'line ' + s.css, value: number(points[index][s.key]), name: s.name.toLowerCase() }; }),
                    clientX == null ? box.left + x(index) * (box.width / width) : clientX, clientY == null ? box.top + pad.top : clientY);
            }
            function leave() {
                current = -1; hair.setAttribute('visibility', 'hidden');
                marks.forEach(function (dot) { dot.setAttribute('visibility', 'hidden'); });
                hideTip();
            }
            root.addEventListener('pointermove', function (event) {
                var box = root.getBoundingClientRect();
                var px = (event.clientX - box.left) * (width / box.width);
                point(stepX ? Math.round((px - pad.left) / stepX) : 0, event.clientX, event.clientY);
            });
            root.addEventListener('pointerleave', leave);
            root.addEventListener('blur', leave);
            root.addEventListener('focus', function () { if (current < 0) { point(points.length - 1); } });
            root.addEventListener('keydown', function (event) {
                if (event.key === 'ArrowLeft') { point((current < 0 ? points.length : current) - 1); event.preventDefault(); }
                if (event.key === 'ArrowRight') { point(current + 1); event.preventDefault(); }
                if (event.key === 'Home') { point(0); event.preventDefault(); }
                if (event.key === 'End') { point(points.length - 1); event.preventDefault(); }
                if (event.key === 'Escape') { leave(); }
            });

            plot.appendChild(root);
        }

        draw(true);
        if (window.ResizeObserver) {
            var known = plot.clientWidth;
            var observer = new ResizeObserver(function () {
                if (!doc.body.contains(plot)) { observer.disconnect(); return; }
                if (plot.clientWidth && Math.abs(plot.clientWidth - known) > 4) { known = plot.clientWidth; hideTip(); draw(false); }
            });
            observer.observe(plot);
            host.chartObserver = observer;
        }
    }

    /* ---- bars split into "not closed" and "closed" -------------------------- */
    function split(host, rows, animate) {
        host.textContent = '';
        rows = (rows || []).filter(function (row) { return row.open + row.closed > 0; });
        if (!rows.length) { host.appendChild(empty(host.getAttribute('data-empty') || 'No tickets in this period.')); return; }

        host.appendChild(legend([{ key: 's-open', name: 'Not closed' }, { key: 's-closed', name: 'Closed' }]));
        var most = Math.max.apply(null, rows.map(function (row) { return row.open + row.closed; }));
        var bars = el('div', 'hbars chart-plot');
        rows.forEach(function (row) {
            var total = row.open + row.closed;
            var name = el('span', 'name', row.name); name.title = row.name;
            var bar = el('span', 'bar' + (animate && !still ? ' grow' : ''));
            bar.style.width = Math.max(2, Math.round(total / most * 100)) + '%';
            bar.tabIndex = 0;
            bar.setAttribute('role', 'img');
            bar.setAttribute('aria-label', row.name + ': ' + plural(row.open, 'ticket', 'tickets') + ' not closed, ' + number(row.closed) + ' closed');
            [['open', 's-open', 'not closed'], ['closed', 's-closed', 'closed']].forEach(function (part) {
                if (!row[part[0]]) { return; }
                var segment = el('i', part[1]);
                segment.style.flex = row[part[0]];
                bar.appendChild(segment);
            });
            function tell(event) {
                var box = bar.getBoundingClientRect();
                showTip(row.name, [
                    { key: 's-open', value: number(row.open), name: 'not closed' },
                    { key: 's-closed', value: number(row.closed), name: 'closed' }
                ], event && event.clientX != null && event.type !== 'focus' ? event.clientX : box.left + box.width / 2, event && event.clientY != null && event.type !== 'focus' ? event.clientY : box.top);
            }
            bar.addEventListener('pointermove', tell); bar.addEventListener('focus', tell);
            bar.addEventListener('pointerleave', hideTip); bar.addEventListener('blur', hideTip);
            var value = el('span', 'val', number(row.open) + ' open of ' + number(total));
            bars.appendChild(name); bars.appendChild(bar); bars.appendChild(value);
        });
        host.appendChild(bars);
        host.appendChild(table([host.getAttribute('data-name') || 'Name', 'Not closed', 'Closed', 'Total'], rows.map(function (row) { return [row.name, number(row.open), number(row.closed), number(row.open + row.closed)]; })));
    }

    /* ---- one column (or one bar) per group, one colour ---------------------- */
    function columns(host, rows, animate) {
        host.textContent = '';
        rows = rows || [];
        var across = host.getAttribute('data-across') === 'true';       // bars instead of columns (long names)
        if (across) { rows = rows.filter(function (row) { return row.count > 0; }); }
        if (!rows.length || !rows.some(function (row) { return row.count > 0; })) {
            host.appendChild(empty(host.getAttribute('data-empty') || 'Nothing to show for this period.'));
            return;
        }

        var most = Math.max.apply(null, rows.map(function (row) { return row.count; }));
        var unit = (host.getAttribute('data-unit') || 'ticket|tickets').split('|');
        function tellAt(mark, row) {
            return function (event) {
                var box = mark.getBoundingClientRect();
                showTip(row.name, [{ key: 's-open', value: number(row.count), name: row.count === 1 ? unit[0] : unit[1] }],
                    event.type === 'focus' ? box.left + box.width / 2 : event.clientX, event.type === 'focus' ? box.top : event.clientY);
            };
        }

        var plot;
        if (across) {
            plot = el('div', 'hbars chart-plot');
            rows.forEach(function (row) {
                var name = el('span', 'name', row.name); name.title = row.name;
                var bar = el('span', 'bar' + (animate && !still ? ' grow' : ''));
                bar.style.width = Math.max(2, Math.round(row.count / most * 100)) + '%';
                bar.tabIndex = 0; bar.setAttribute('role', 'img'); bar.setAttribute('aria-label', row.name + ': ' + plural(row.count, unit[0], unit[1]));
                bar.appendChild(el('i', 's-open')); bar.firstChild.style.flex = 1;
                var tell = tellAt(bar, row);
                bar.addEventListener('pointermove', tell); bar.addEventListener('focus', tell);
                bar.addEventListener('pointerleave', hideTip); bar.addEventListener('blur', hideTip);
                plot.appendChild(name); plot.appendChild(bar); plot.appendChild(el('span', 'val', number(row.count)));
            });
        } else {
            plot = el('div', 'cols chart-plot');
            rows.forEach(function (row) {
                var column = el('div', 'col');
                var track = el('span', 'col-track');
                track.appendChild(el('b', null, number(row.count)));         // the value sits on the cap
                var mark = el('i', 's-open' + (animate && !still ? ' grow' : ''));
                // the tallest column leaves room for its value above it
                mark.style.height = row.count ? 'max(3px, calc((100% - 22px) * ' + (row.count / most).toFixed(4) + '))' : '0';
                mark.tabIndex = 0; mark.setAttribute('role', 'img'); mark.setAttribute('aria-label', row.name + ': ' + plural(row.count, unit[0], unit[1]));
                var tell = tellAt(mark, row);
                mark.addEventListener('pointermove', tell); mark.addEventListener('focus', tell);
                mark.addEventListener('pointerleave', hideTip); mark.addEventListener('blur', hideTip);
                track.appendChild(mark); column.appendChild(track);
                column.appendChild(el('span', 'col-name', row.name));
                plot.appendChild(column);
            });
        }
        host.appendChild(plot);
        host.appendChild(table([host.getAttribute('data-name') || 'Group', unit[1].charAt(0).toUpperCase() + unit[1].slice(1)], rows.map(function (row) { return [row.name, number(row.count)]; })));
    }

    /* ---- numbers that count up ---------------------------------------------- */
    function countUp(node, to, format) {
        format = format || number;
        var from = Number(node.getAttribute('data-shown') || 0);
        node.setAttribute('data-shown', to);
        if (still || from === to || !window.requestAnimationFrame) { node.textContent = format(to); return; }
        var start = null; var length = 620;
        function frame(now) {
            if (start == null) { start = now; }
            var done = Math.min(1, (now - start) / length);
            var eased = 1 - Math.pow(1 - done, 3);
            node.textContent = format(Math.round(from + (to - from) * eased));
            if (done < 1 && Number(node.getAttribute('data-shown')) === to) { window.requestAnimationFrame(frame); }
            else if (Number(node.getAttribute('data-shown')) === to) { node.textContent = format(to); }
        }
        window.requestAnimationFrame(frame);
    }

    function duration(hours) {
        if (hours == null) { return 'None closed'; }
        if (hours < 1) { return 'Under 1 hour'; }
        if (hours < 48) { var h = Math.round(hours); return h + (h === 1 ? ' hour' : ' hours'); }
        var days = Math.round(hours / 24 * 10) / 10;
        return days + ' days';
    }

    /* ---- how a number compares with the period before ------------------------ */
    function delta(node, now, before, goodWhen, text) {
        node.textContent = '';
        node.className = 'tile-delta';
        if (before == null || now == null) { return; }
        var change = now - before;
        if (!change) { node.textContent = 'Same as the ' + text; return; }
        var up = change > 0;
        if (goodWhen) { node.classList.add((goodWhen === 'up') === up ? 'good' : 'bad'); }
        node.appendChild(el('i', up ? 'up' : 'down'));
        node.appendChild(doc.createTextNode((up ? 'Up from ' : 'Down from ') + number(before) + ' in the ' + text));
    }

    /* ---- draw everything inside one container -------------------------------- */
    function drawAll(container, data, animate) {
        container.classList.remove('is-loading');
        var before = (data.days === 365 ? '12 months' : data.days + ' days') + ' before';   // "... in the 30 days before"
        $all('[data-chart]', container).forEach(function (host) {
            var rows = data[host.getAttribute('data-key')];
            host.setAttribute('data-bucket', data.bucket || 'day');
            var kind = host.getAttribute('data-chart');
            if (kind === 'trend') { trend(host, rows, animate); }
            else if (kind === 'split') { split(host, rows, animate); }
            else if (kind === 'columns') { columns(host, rows, animate); }
        });
        $all('[data-stat]', container).forEach(function (node) {
            var key = node.getAttribute('data-stat');
            if (key === 'medianHours') { node.textContent = duration(data.medianHours); }
            else { countUp(node, Number(data[key] || 0)); }
        });
        $all('[data-delta]', container).forEach(function (node) {
            var key = node.getAttribute('data-delta');
            if (key === 'raised') { delta(node, data.raised, data.previousRaised, null, before); }
            if (key === 'closed') { delta(node, data.closed, data.previousClosed, 'up', before); }
            if (key === 'medianHours') {
                node.textContent = ''; node.className = 'tile-delta';
                if (data.medianHours != null && data.previousMedianHours != null && data.medianHours !== data.previousMedianHours) {
                    var faster = data.medianHours < data.previousMedianHours;
                    node.classList.add(faster ? 'good' : 'bad');
                    node.appendChild(el('i', faster ? 'down' : 'up'));
                    node.appendChild(doc.createTextNode((faster ? 'Faster than ' : 'Slower than ') + duration(data.previousMedianHours).toLowerCase() + ' in the ' + before));
                }
            }
        });
        $all('[data-period-name]', container).forEach(function (node) { node.textContent = data.periodName || ''; });
    }

    function scan(root) {
        $all('[data-charts]', root || doc).forEach(function (container) {
            var source = $('script[data-chart-data]', container);
            if (!source || container.chartsDrawn) { return; }
            var data;
            try { data = JSON.parse(source.textContent); } catch (e) { return; }
            container.chartsDrawn = true;
            container.chartData = data;
            if (!container.hasAttribute('data-dashboard')) { drawAll(container, data, true); }
        });
    }

    // chart <-> table, per chart card
    doc.addEventListener('click', function (event) {
        var button = event.target.closest('[data-chart-view]');
        if (!button) { return; }
        var card = button.closest('.card') || doc;
        var asTable = button.getAttribute('aria-pressed') !== 'true';
        button.setAttribute('aria-pressed', asTable ? 'true' : 'false');
        var label = $('span', button);
        if (label) { label.textContent = asTable ? 'Chart' : 'Table'; }
        $all('[data-chart]', card).forEach(function (host) { host.setAttribute('data-view', asTable ? 'table' : 'chart'); });
    });

    /* ---- the dashboard: period, and widgets the way each person wants them ---- */
    function dashboard(board) {
        var grid = $('[data-panels]', board);
        var key = 'csms-dash:' + (board.getAttribute('data-user') || '0') + ':' + (board.getAttribute('data-role') || '');
        var saved = {};
        try { saved = JSON.parse(read(key) || '{}') || {}; } catch (e) { saved = {}; }
        var widgets = function () { return $all('[data-panel]', grid); };
        var defaults = widgets().map(function (widget) { return { id: widget.getAttribute('data-panel'), size: widget.getAttribute('data-size') || 'half' }; });

        function apply() {
            var order = Array.isArray(saved.order) ? saved.order : [];
            var all = widgets();
            // saved order first, then what is new since it was saved (in the order of the page)
            all.slice().sort(function (a, b) {
                var ia = order.indexOf(a.getAttribute('data-panel')), ib = order.indexOf(b.getAttribute('data-panel'));
                if (ia < 0 && ib < 0) { return all.indexOf(a) - all.indexOf(b); }
                if (ia < 0) { return 1; }
                if (ib < 0) { return -1; }
                return ia - ib;
            }).forEach(function (widget) { grid.appendChild(widget); });
            widgets().forEach(function (widget) {
                var id = widget.getAttribute('data-panel');
                widget.hidden = Array.isArray(saved.hidden) && saved.hidden.indexOf(id) >= 0;
                var size = saved.size && saved.size[id];
                if (size === 'full' || size === 'half') { widget.setAttribute('data-size', size); }
            });
        }
        function remember() {
            saved.order = widgets().map(function (widget) { return widget.getAttribute('data-panel'); });
            saved.hidden = widgets().filter(function (widget) { return widget.hidden; }).map(function (widget) { return widget.getAttribute('data-panel'); });
            saved.size = {};
            widgets().forEach(function (widget) { saved.size[widget.getAttribute('data-panel')] = widget.getAttribute('data-size') || 'half'; });
            store(key, JSON.stringify(saved));
            tray();
        }

        // ---- the period
        var data = board.chartData;
        var loading = 0;
        function load(days, animate) {
            if (data && data.days === days) { drawAll(board, data, animate); return; }
            var ticket = ++loading;
            board.classList.add('is-loading');                        // the charts stay as they are, dimmed, until the new numbers arrive
            var ask = window.Csms && window.Csms.request ? window.Csms.request : function (url) { return fetch(url, { credentials: 'same-origin', headers: { 'X-Requested-With': 'XMLHttpRequest' } }); };
            ask(board.getAttribute('data-source') + '?days=' + days)
                .then(function (response) { if (!response.ok) { throw new Error(response.status); } return response.json(); })
                .then(function (fresh) { if (ticket !== loading) { return; } data = fresh; board.chartData = fresh; drawAll(board, fresh, true); })
                .catch(function (error) {
                    if (ticket !== loading || (error && error.message === 'signed out')) { return; }
                    board.classList.remove('is-loading');
                    if (window.Csms) { window.Csms.toast('The numbers for this period could not be loaded. Try again.', true); }
                });
        }
        var days = [7, 30, 90, 365].indexOf(Number(saved.days)) >= 0 ? Number(saved.days) : (data ? data.days : 30);
        $all('[data-period]', board).forEach(function (input) {
            input.checked = Number(input.value) === days;
            input.addEventListener('change', function () {
                if (!input.checked) { return; }
                days = Number(input.value); saved.days = days; store(key, JSON.stringify(saved));
                load(days, true);
            });
        });

        // ---- arranging the widgets
        var editButton = $('[data-dash-edit]', board);
        var hiddenTray = $('[data-dash-hidden]', board);
        function title(widget) { return widget.getAttribute('data-title') || widget.getAttribute('data-panel'); }
        function tray() {
            if (!hiddenTray) { return; }
            hiddenTray.textContent = '';
            var hidden = widgets().filter(function (widget) { return widget.hidden; });
            hiddenTray.hidden = !hidden.length || !board.classList.contains('is-editing');
            if (!hidden.length) { return; }
            hiddenTray.appendChild(el('span', 'muted', 'Hidden:'));
            hidden.forEach(function (widget) {
                var button = el('button', 'chip', '+ ' + title(widget));
                button.type = 'button'; button.widget = widget;
                button.addEventListener('click', function () {
                    widget.hidden = false; remember();
                    if (data) { drawAll(board, data, false); }
                    if (widget.syncTools) { widget.syncTools(); }
                    widget.scrollIntoView({ block: 'nearest', behavior: still ? 'auto' : 'smooth' });
                });
                hiddenTray.appendChild(button);
            });
        }
        function tools(widget) {
            if ($('.widget-tools', widget)) { return; }
            var bar = el('div', 'widget-tools');
            bar.setAttribute('role', 'group'); bar.setAttribute('aria-label', 'Arrange: ' + title(widget));
            function tool(text, label, action) {
                var button = el('button', 'btn btn-sm', text); button.type = 'button'; button.title = label; button.setAttribute('aria-label', label + ': ' + title(widget));
                button.addEventListener('click', function () {
                    action(); remember(); sync();
                    if (!widget.hidden) { button.focus(); return; }
                    // the widget is gone: on to its entry under "Hidden", so the keyboard does not lose its place
                    var back = $all('button', hiddenTray).filter(function (chip) { return chip.widget === widget; })[0];
                    if (back) { back.focus(); }
                });
                bar.appendChild(button); return button;
            }
            var grip = el('span', 'grip', '\u2807'); grip.title = 'Drag to move'; grip.setAttribute('aria-hidden', 'true'); bar.appendChild(grip);
            tool('↑', 'Move earlier', function () { var prev = visibleSibling(widget, -1); if (prev) { grid.insertBefore(widget, prev); } });
            tool('↓', 'Move later', function () { var next = visibleSibling(widget, 1); if (next) { grid.insertBefore(next, widget); } });
            tool('', 'Change the width', function () { widget.setAttribute('data-size', widget.getAttribute('data-size') === 'full' ? 'half' : 'full'); if (data) { drawAll(board, data, false); } });
            tool('Hide', 'Hide', function () { widget.hidden = true; });
            function sync() {
                $all('[data-panel]', grid).forEach(function (other) {
                    var own = $('.widget-tools', other); if (!own) { return; }
                    var buttons = $all('button', own);
                    buttons[0].disabled = !visibleSibling(other, -1);
                    buttons[1].disabled = !visibleSibling(other, 1);
                    buttons[2].textContent = other.getAttribute('data-size') === 'full' ? 'Make narrow' : 'Make wide';
                });
            }
            widget.syncTools = sync;
            widget.insertBefore(bar, widget.firstChild);
        }
        function visibleSibling(widget, direction) {
            var node = widget;
            do { node = direction < 0 ? node.previousElementSibling : node.nextElementSibling; } while (node && (node.hidden || !node.hasAttribute('data-panel')));
            return node;
        }

        var dragged = null;
        var orderBefore = null;     // the order when a drag began: a drag that is cancelled puts it back
        function editing(on) {
            board.classList.toggle('is-editing', on);
            if (editButton) {
                editButton.setAttribute('aria-pressed', on ? 'true' : 'false');
                var label = $('span', editButton); if (label) { label.textContent = on ? 'Done' : 'Customize'; }
            }
            widgets().forEach(function (widget) {
                if (on) { tools(widget); }
                widget.draggable = on;
            });
            var first = widgets()[0]; if (on && first && first.syncTools) { first.syncTools(); }
            var reset = $('[data-dash-reset]', board); if (reset) { reset.hidden = !on; }
            tray();
        }
        if (editButton) { editButton.addEventListener('click', function () { editing(!board.classList.contains('is-editing')); }); }
        var resetButton = $('[data-dash-reset]', board);
        if (resetButton) {
            resetButton.addEventListener('click', function () {
                saved = { days: saved.days };
                store(key, JSON.stringify(saved));
                defaults.forEach(function (item) {
                    var widget = $('[data-panel="' + item.id + '"]', grid);
                    if (widget) { widget.hidden = false; widget.setAttribute('data-size', item.size); grid.appendChild(widget); }
                });
                if (data) { drawAll(board, data, false); }
                var first = widgets()[0]; if (first && first.syncTools) { first.syncTools(); }
                tray();
            });
        }
        grid.addEventListener('dragstart', function (event) {
            var widget = event.target.closest && event.target.closest('[data-panel]');
            if (!widget || !board.classList.contains('is-editing')) { return; }
            dragged = widget; widget.classList.add('is-dragged');
            orderBefore = widgets();
            if (event.dataTransfer) { event.dataTransfer.effectAllowed = 'move'; try { event.dataTransfer.setData('text/plain', widget.getAttribute('data-panel')); } catch (e) { /* older browsers */ } }
        });
        grid.addEventListener('dragover', function (event) {
            if (!dragged) { return; }
            var over = event.target.closest && event.target.closest('[data-panel]');
            event.preventDefault();
            if (!over || over === dragged) { return; }
            var box = over.getBoundingClientRect();
            var after = (event.clientY - box.top) / box.height > .5 || (box.width < grid.clientWidth * .7 && (event.clientX - box.left) / box.width > .5 && (event.clientY - box.top) / box.height > .15);
            grid.insertBefore(dragged, after ? over.nextSibling : over);
        });
        function dropped(event) {
            if (!dragged) { return; }
            dragged.classList.remove('is-dragged'); dragged = null;
            // Escape, or a drop outside the grid: as it was
            if (event && event.type === 'dragend' && event.dataTransfer && event.dataTransfer.dropEffect === 'none' && orderBefore) {
                orderBefore.forEach(function (widget) { grid.appendChild(widget); });
            }
            orderBefore = null;
            remember();
            var first = widgets()[0]; if (first && first.syncTools) { first.syncTools(); }
            if (data) { drawAll(board, data, false); }
        }
        grid.addEventListener('drop', function (event) { event.preventDefault(); dropped(event); });
        grid.addEventListener('dragend', dropped);

        apply();
        load(days, true);
        // the parts arrive one after the other once; after that nothing replays (leaving "Customize", moving a widget)
        window.setTimeout(function () { board.classList.add('is-ready'); }, 1200);
    }

    function start() {
        scan(doc);
        $all('[data-dashboard]').forEach(dashboard);
        // the numbers of the page that count up once (data-count-to)
        $all('[data-count-to]').forEach(function (node) { countUp(node, Number(node.getAttribute('data-count-to') || 0)); });
    }
    if (doc.readyState === 'loading') { doc.addEventListener('DOMContentLoaded', start); } else { start(); }

    window.CsmsCharts = { scan: scan };
})();
