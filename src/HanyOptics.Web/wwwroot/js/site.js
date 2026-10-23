// Site-wide behaviour.

// Scrolling the page while a number field has focus makes the browser change its value
// instead of scrolling. On a price or payment box that means money moving with no click,
// no keystroke and nothing on screen to say it happened - you scroll past a form and the
// amount is silently different.
//
// Blurring the field on wheel is the fix rather than blocking the event: the value stops
// changing, and the page still scrolls the way the user expected. Browsers only apply
// wheel changes to a focused number input, so removing focus is the whole of it.
//
// Delegated from the document so it covers fields that arrive later - the order-detail
// popup and the edit-item dialog are both fetched after page load.
(function () {
    document.addEventListener("wheel", function (e) {
        var el = document.activeElement;
        if (!el || el.type !== "number") return;
        if (el !== e.target) return;
        el.blur();
    }, { passive: true });
})();

// A pick-or-type box (input.combo with a list: a category, a lens description) opens its
// list on click, like the <select> it looks like. showPicker is not available everywhere;
// where it is missing, the list still appears as the user types - the browser's own
// behaviour. Delegated, so it covers boxes in windows fetched after page load too.
(function () {
    document.addEventListener("click", function (e) {
        var combo = e.target.closest && e.target.closest("input.combo[list]");
        if (combo && typeof combo.showPicker === "function") {
            try { combo.showPicker(); } catch (err) { /* not allowed right now - typing still works */ }
        }
    });
})();

// On a phone the sidebar sits off-screen. The ☰ button slides it in over the page, and a
// tap on the dark overlay slides it back. Following a link needs nothing here - the next
// page loads with the sidebar closed.
(function () {
    var app = document.getElementById("app");
    var toggle = document.getElementById("menu-toggle");
    var overlay = document.getElementById("sidebar-overlay");
    if (!app || !toggle || !overlay) return;
    toggle.addEventListener("click", function () { app.classList.toggle("menu-open"); });
    overlay.addEventListener("click", function () { app.classList.remove("menu-open"); });
})();

// A slow connection makes a second tap on a save button look like the first did nothing,
// and the same payment, expense or invoice is then recorded twice. The button is switched
// off once the form is really on its way.
//
// Switched off after the event rather than inside it: a form stopped by its own check (a
// missing reason, a confirm answered with إلغاء) or sent in the background only reports
// defaultPrevented once every handler has run - and by then the clicked button's own
// name/value (إضافة بند آخر / التالي) has already been read into the submission.
(function () {
    document.addEventListener("submit", function (e) {
        var form = e.target;
        if ((form.getAttribute("method") || "").toLowerCase() !== "post") return;
        var button = e.submitter || form.querySelector('button[type="submit"]');
        if (!button) return;
        setTimeout(function () {
            if (e.defaultPrevented) return;
            button.disabled = true;
            button.dataset.busy = "1";
        }, 0);
    });

    // The browser's Back button can bring the page back exactly as it was left, with the
    // button still switched off - so it is switched back on.
    window.addEventListener("pageshow", function () {
        document.querySelectorAll('button[data-busy="1"]').forEach(function (b) {
            b.disabled = false;
            delete b.dataset.busy;
        });
    });
})();
