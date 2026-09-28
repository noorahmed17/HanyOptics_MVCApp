// The financial reports (إغلاق اليومية · التقرير الشهري · مصروفات الشهر). Every figure is
// rendered by the server from the report views; this only adds what needs the browser:
// the daily-net chart, clickable rows and the log filters.
// Any text that comes from data is set with textContent, never as markup.
(function () {
    "use strict";

    // Same digits as the server renders (.NET's ar-EG uses 0-9 with Arabic separators), so a
    // total rewritten by a filter looks like the figures around it.
    var nf = new Intl.NumberFormat("ar-EG", { maximumFractionDigits: 2, numberingSystem: "latn" });
    function money(v) { return (v < 0 ? "− " : "") + nf.format(Math.abs(v)) + " ج"; }

    // ── Rows that open something (a day, a category) ─────────────────────
    function activate(row) {
        var href = row.getAttribute("data-href");
        if (href) { window.location.href = href; return; }
        var cat = row.getAttribute("data-category");
        var select = document.getElementById("logCategory");
        if (cat !== null && select && row.closest("#logTable") === null) {
            select.value = cat;
            filterLog();
            document.getElementById("expenseLog").scrollIntoView({ behavior: "smooth", block: "start" });
        }
    }
    document.addEventListener("click", function (e) {
        if (e.target.closest("a")) return;
        var row = e.target.closest("tr.row-link");
        if (row) activate(row);
    });
    document.addEventListener("keydown", function (e) {
        if (e.key !== "Enter") return;
        var row = e.target.closest && e.target.closest("tr.row-link");
        if (row) activate(row);
    });

    // ── Expense log filters (مصروفات الشهر) ──────────────────────────────
    var log = document.getElementById("logTable");
    function filterLog() {
        if (!log) return;
        var srcEl = document.querySelector('input[name="logSource"]:checked');
        var src = srcEl ? srcEl.value : "all";
        var cat = document.getElementById("logCategory").value;
        var count = 0, total = 0;
        log.querySelectorAll("tbody tr[data-source]").forEach(function (tr) {
            var show = (src === "all" || tr.getAttribute("data-source") === src)
                && (!cat || tr.getAttribute("data-category") === cat);
            tr.hidden = !show;
            if (show) { count++; total += parseFloat(tr.getAttribute("data-amount")) || 0; }
        });
        document.getElementById("logEmpty").hidden = count > 0;
        document.getElementById("logCount").textContent = nf.format(count);
        document.getElementById("logTotal").textContent = nf.format(Math.round(total * 100) / 100);
    }
    if (log) {
        document.getElementById("logSource").addEventListener("change", filterLog);
        document.getElementById("logCategory").addEventListener("change", filterLog);
    }

    // ── Net per day (التقرير الشهري) ─────────────────────────────────────
    var box = document.getElementById("netChart");
    var dataEl = document.getElementById("netChartData");
    if (!box || !dataEl) return;

    var days = JSON.parse(dataEl.textContent || "[]");
    if (!days.length) return;
    var dayUrl = box.getAttribute("data-day-url");
    var NS = "http://www.w3.org/2000/svg";

    function niceStep(range) {
        var raw = (range || 1) / 5, p = Math.pow(10, Math.floor(Math.log10(raw))), f = raw / p;
        return (f <= 1 ? 1 : f <= 2 ? 2 : f <= 2.5 ? 2.5 : f <= 5 ? 5 : 10) * p;
    }
    function el(name, attrs) {
        var e = document.createElementNS(NS, name);
        Object.keys(attrs).forEach(function (k) { e.setAttribute(k, attrs[k]); });
        return e;
    }

    var W = 720, H = 240, padT = 12, padB = 28, padL = 8, padR = 60;
    var vals = days.map(function (d) { return d.net; });
    var max = Math.max.apply(null, vals.concat([0])), min = Math.min.apply(null, vals.concat([0]));
    var step = niceStep(max - min), top = Math.ceil(max / step) * step, bot = Math.floor(min / step) * step;
    if (top === bot) top = bot + step;
    function y(v) { return padT + (top - v) / (top - bot) * (H - padT - padB); }

    // RTL like the rest of the page: the 1st of the month sits on the right.
    var plotW = W - padL - padR, slot = plotW / days.length, bw = Math.max(4, Math.min(22, slot - 6));
    var svg = el("svg", { viewBox: "0 0 " + W + " " + H, role: "img", "aria-label": "صافي كل يوم" });
    svg.style.direction = "ltr";

    for (var v = bot; v <= top + 0.001; v += step) {
        svg.appendChild(el("line", { "class": v === 0 ? "zero" : "gl", x1: padL, x2: W - padR, y1: y(v), y2: y(v) }));
        var t = el("text", { x: W - padR + 8, y: y(v) + 4, "text-anchor": "start" });
        t.textContent = v ? (v < 0 ? "− " : "") + nf.format(Math.abs(v) / 1000) + " ألف" : nf.format(0);
        svg.appendChild(t);
    }

    days.forEach(function (d, i) {
        var cx = W - padR - (i + 0.5) * slot, h = Math.max(1, Math.abs(y(d.net) - y(0)));
        if (d.net !== 0) {
            svg.appendChild(el("rect", {
                "class": "bar", "data-i": i, x: cx - bw / 2, width: bw, rx: 3,
                y: d.net >= 0 ? y(0) - h : y(0), height: h,
                fill: d.net < 0 ? "#dc2626" : "#057a55"
            }));
        }
        if (d.day === 1 || d.day % 5 === 0 || i === days.length - 1) {
            var lbl = el("text", { x: cx, y: H - 8, "text-anchor": "middle" });
            lbl.textContent = nf.format(d.day);
            svg.appendChild(lbl);
        }
        var hit = el("rect", { "class": "hit", "data-i": i, x: cx - slot / 2, y: padT, width: slot, height: H - padT - padB });
        svg.appendChild(hit);
    });
    box.appendChild(svg);

    var tip = document.createElement("div");
    tip.className = "rep-tip";
    tip.hidden = true;
    box.appendChild(tip);

    function row(label, value) {
        var r = document.createElement("div"); r.className = "r";
        var a = document.createElement("span"); a.textContent = label;
        var b = document.createElement("span"); b.textContent = value;
        r.appendChild(a); r.appendChild(b);
        return r;
    }
    function show(i) {
        var d = days[i];
        box.classList.add("hovering");
        box.querySelectorAll(".bar").forEach(function (b) { b.classList.toggle("on", +b.getAttribute("data-i") === i); });
        tip.textContent = "";
        var head = document.createElement("b"); head.textContent = d.label;
        tip.appendChild(head);
        tip.appendChild(row("الإيرادات", money(d.rev)));
        tip.appendChild(row("جميع المصروفات", money(d.exp)));
        tip.appendChild(row("صافي اليوم", money(d.net)));
        tip.hidden = false;
        var rect = svg.getBoundingClientRect(), k = rect.width / W, cx = (W - padR - (i + 0.5) * slot) * k;
        var left = Math.min(Math.max(cx - tip.offsetWidth / 2, 0), rect.width - tip.offsetWidth);
        tip.style.left = left + "px";
        tip.style.top = Math.max(y(Math.max(0, d.net)) * k - tip.offsetHeight - 10, 0) + "px";
    }
    function hide() { box.classList.remove("hovering"); tip.hidden = true; }

    svg.querySelectorAll(".hit").forEach(function (h) {
        var i = +h.getAttribute("data-i");
        h.addEventListener("mouseenter", function () { show(i); });
        h.addEventListener("click", function () {
            window.location.href = dayUrl + "?date=" + encodeURIComponent(days[i].date);
        });
    });
    svg.addEventListener("mouseleave", hide);
})();
