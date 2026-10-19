// مرتجع أو استبدال بعد التسليم (Views/Corrections/_FrameReturn.cshtml).
// expenses.js opens the two popups and copies each button's data-fields into them; this file
// adds what they need on top of that. Both forms still post normally, and
// sp_admin_return_delivered_frame / sp_admin_exchange_delivered_frame have the final word -
// this only keeps the form honest while it is being filled in.
(function () {
    "use strict";

    var money = function (n) {
        return Number(n).toLocaleString("ar-EG", { maximumFractionDigits: 2 }) + " ج";
    };
    var round2 = function (n) { return Math.round(n * 100) / 100; };

    function openerFields(e, modalId) {
        var opener = e.target.closest('[data-modal-open="' + modalId + '"]');
        return opener ? JSON.parse(opener.getAttribute("data-fields") || "{}") : null;
    }

    // ── مرتجع ────────────────────────────────────────────────────────────
    // The refund box takes what was actually handed back, capped at the item's price or what
    // was paid on the invoice, whichever is smaller.
    var retAmount = document.getElementById("retAmount");
    if (retAmount) {
        document.addEventListener("click", function (e) {
            var fields = openerFields(e, "returnModal");
            if (fields) retAmount.max = fields.maxRefund;
        });
    }

    // ── زر التنفيذ يتقفل بعد أول إرسال ───────────────────────────────────
    // On a slow connection a second click sends the same return or exchange again. The
    // procedure refuses the second one, but the page would then show that refusal for an
    // operation that actually went through. Registered after expenses.js, so a submit it
    // stopped (missing reason) is left alone and the button stays usable.
    document.querySelectorAll("#returnModal form, #exchangeModal form").forEach(function (form) {
        form.addEventListener("submit", function (e) {
            if (e.defaultPrevented) return;
            var submit = form.querySelector('button[type="submit"]');
            if (submit) submit.disabled = true;
        });
    });

    // ── استبدال ──────────────────────────────────────────────────────────
    var modal = document.getElementById("exchangeModal");
    if (!modal) return;

    var box = document.getElementById("exBarcode");
    var resultBox = document.getElementById("exFrameResult");
    var errorBox = document.getElementById("exFrameError");
    var meta = document.getElementById("exFrameMeta");
    var price = document.getElementById("exPrice");
    var lensGroup = document.getElementById("exLensGroup");
    var lensPrice = document.getElementById("exLens");
    var lensDesc = document.getElementById("exLensDesc");
    var kindHint = document.getElementById("exKindHint");
    var diffBox = document.getElementById("exDiff");
    var amount = document.getElementById("exAmount");
    var amountLabel = document.getElementById("exAmountLabel");
    var confirmBtn = document.getElementById("exConfirmBtn");

    var orderTotal = Number(modal.getAttribute("data-order-total")) || 0;
    var orderPaid = Number(modal.getAttribute("data-order-paid")) || 0;
    var oldPrice = 0;

    function withLenses() {
        var el = modal.querySelector('input[name="WithLenses"]:checked');
        return !!el && el.value === "true";
    }

    // Frame details are staff-typed text, so they go in as text, never as HTML.
    function line(label, value, bold) {
        var span = document.createElement("span");
        if (label) span.append(label);
        (bold ? span.appendChild(document.createElement("b")) : span).append(String(value == null ? "" : value));
        return span;
    }

    // The difference the procedure will see: the invoice as it would be after the exchange,
    // minus what has been paid on it. Shown so staff know what to collect or hand back; the
    // amount box is then capped at it. Hidden until the prices it depends on are filled in.
    function updateDifference() {
        var lenses = withLenses();
        if (price.value === "" || (lenses && lensPrice.value === "")) {
            diffBox.hidden = true;
            amountLabel.textContent = "المبلغ (ج) *";
            amount.removeAttribute("max");
            return;
        }

        var newTotal = Number(price.value) + (lenses ? Number(lensPrice.value) : 0);
        var diff = round2(orderTotal - oldPrice + newTotal - orderPaid);

        diffBox.hidden = false;
        if (diff > 0) {
            diffBox.textContent = "على العميل " + money(diff) + ". اكتب المبلغ الذي دفعه فعلًا، ويبقى الباقي على الفاتورة.";
            amountLabel.textContent = "المبلغ المدفوع الآن (ج) *";
            amount.max = diff;
        } else if (diff < 0) {
            diffBox.textContent = "للعميل " + money(-diff) + ". اكتب المبلغ الذي رُدّ إليه فعلًا.";
            amountLabel.textContent = "المبلغ المرتجع للعميل (ج) *";
            amount.max = -diff;
        } else {
            diffBox.textContent = "لا يوجد فرق في السعر.";
            amountLabel.textContent = "المبلغ (ج) *";
            amount.max = 0;
            amount.value = "0";
        }
    }

    // Frame only: the lens fields go away and are not required - nothing about lenses is
    // sent. Frame and lenses: a real price and a description are required, and the order
    // goes back to "sold" to be made up. required comes off with the hidden fields, or the
    // browser would block the submit on an error nobody can see.
    function applyKind() {
        var lenses = withLenses();
        lensGroup.hidden = !lenses;
        lensPrice.required = lenses;
        lensDesc.required = lenses;
        if (!lenses) {
            lensPrice.value = "";
            lensDesc.value = "";
        }
        kindHint.textContent = lenses
            ? "يعود الطلب إلى «تم البيع» حتى تُجهَّز العدسات، ثم يُسلَّم من جديد."
            : "يبقى الطلب «تم التسليم»، ويُباع الإطار الجديد مباشرة.";
        updateDifference();
    }

    // Every lookup gets a number, and only the latest one may fill the form. Typing in the
    // box, a new lookup or reopening the popup moves the number on (all three go through
    // resetLookup), so an answer still in flight from before is dropped.
    var lookupSeq = 0;

    function resetLookup() {
        lookupSeq++;
        resultBox.classList.remove("show");
        errorBox.classList.remove("show");
        confirmBtn.disabled = true;
    }

    // Same lookup as the new-order wizard and the swap popup: the confirm button stays
    // locked until the barcode resolves to a frame that is actually available, so a typo
    // can't be sent. The price is filled with the frame's sell price and can be edited.
    function lookup() {
        resetLookup();
        var seq = lookupSeq;
        var barcode = box.value.trim();
        if (!barcode) return;

        fetch(modal.getAttribute("data-lookup-url") + "?barcode=" + encodeURIComponent(barcode))
            .then(function (r) { return r.json(); })
            .then(function (data) {
                if (seq !== lookupSeq) return;
                if (box.value.trim() !== barcode) return;
                if (data.found) {
                    meta.replaceChildren(
                        line(null, (data.brand || "") + " " + (data.modelName || ""), true),
                        line(null, (data.color || "") + " — " + (data.size || "")),
                        line("السعر: ", money(data.sellPrice || 0), true),
                        line("المتاح: ", data.qtyAvailable, true));
                    resultBox.classList.add("show");
                    price.value = data.sellPrice;
                    confirmBtn.disabled = false;
                    updateDifference();
                } else {
                    errorBox.textContent = data.message || "الإطار غير موجود";
                    errorBox.classList.add("show");
                }
            });
    }

    // scanner.js types a scan into any [data-role="barcodeInput"] and then sends Enter.
    // Enter here means "look it up", never "submit".
    box.addEventListener("keydown", function (e) {
        if (e.key !== "Enter") return;
        e.preventDefault();
        lookup();
    });
    box.addEventListener("input", resetLookup);
    document.getElementById("exSearchBtn").addEventListener("click", lookup);
    price.addEventListener("input", updateDifference);
    lensPrice.addEventListener("input", updateDifference);
    modal.querySelectorAll('input[name="WithLenses"]').forEach(function (k) {
        k.addEventListener("change", applyKind);
    });

    // Every opening starts clean, as frame only - the usual case.
    document.addEventListener("click", function (e) {
        var fields = openerFields(e, "exchangeModal");
        if (!fields) return;
        oldPrice = Number(fields.oldPrice) || 0;
        modal.querySelector('input[name="WithLenses"][value="false"]').checked = true;
        resetLookup();
        applyKind();
    });
})();
