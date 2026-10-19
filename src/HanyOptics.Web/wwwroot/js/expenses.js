// Behaviour shared by the expenses screens: حركة الدرج، المصروفات والإيرادات، الموردون،
// التصحيحات. Every form still posts normally and the stored procedures have the final
// word - this only keeps the form honest while it is being filled in, so the user is not
// offered a combination the database will refuse.
(function () {
    "use strict";

    var money = function (n) {
        return Number(n).toLocaleString("ar-EG", { maximumFractionDigits: 2 }) + " ج";
    };

    function checkedValue(form, name) {
        var el = form.querySelector('input[name="' + name + '"]:checked');
        return el ? el.value : "";
    }

    function setChecked(form, name, value) {
        var el = form.querySelector('input[name="' + name + '"][value="' + value + '"]');
        if (el) el.checked = true;
    }

    // data-show-if="EntryType=expense,owner_draw" - shown while that radio group holds one
    // of the listed values. Conditions joined with "&" must all hold; alternatives are
    // separated by "|" ("EntryType=income|EntryType=expense&FundingSource=outside").
    function matches(form, expr) {
        return expr.split("|").some(function (group) {
            return group.split("&").every(function (cond) {
                var parts = cond.split("=");
                return parts[1].split(",").indexOf(checkedValue(form, parts[0])) >= 0;
            });
        });
    }

    // The same rules sp_add_expense / sp_update_expense enforce, applied as the user clicks:
    //   - other income always lands in the drawer;
    //   - an expense or draw paid from the drawer is always cash;
    //   - only an expense can be paid to a supplier.
    function syncExpenseForm(form) {
        var type = checkedValue(form, "EntryType");
        var outside = form.querySelector('input[name="FundingSource"][value="outside"]');

        // Income always goes into the drawer, and an owner's draw always comes out of it -
        // so for both, "from outside" is switched back and locked. Without this a choice
        // made on the expense card survived, hidden, after switching to سحب للمالك.
        if (type === "income" || type === "owner_draw") {
            setChecked(form, "FundingSource", "drawer");
            if (outside) outside.disabled = true;
        } else if (outside) {
            outside.disabled = false;
        }

        if (type !== "income" && checkedValue(form, "FundingSource") === "drawer")
            setChecked(form, "PaymentMethod", "cash");

        var supplier = form.querySelector('select[name="SupplierId"]');
        if (supplier && type !== "expense") supplier.value = "";

        form.querySelectorAll("[data-show-if]").forEach(function (el) {
            el.hidden = !matches(form, el.getAttribute("data-show-if"));
        });

        // Every type has its own list (rendered by _CategoryLists as cat-<type>), so the box
        // looks and works the same - a drop-down arrow with choices - whichever is picked.
        var category = form.querySelector("input[data-category]");
        if (category && type) category.setAttribute("list", "cat-" + type);

        checkDrawer(form);
    }

    // A payout from the drawer larger than the drawer holds is refused by the procedure;
    // saying so before the post saves a round trip and a retyped form.
    function checkDrawer(form) {
        var warn = form.querySelector("[data-drawer-warn]");
        if (!warn || !form.hasAttribute("data-drawer-balance")) return;

        var balance = parseFloat(form.getAttribute("data-drawer-balance")) || 0;
        var amountBox = form.querySelector('input[name="Amount"]');
        var amount = parseFloat(amountBox && amountBox.value) || 0;
        var fromDrawer = checkedValue(form, "EntryType") !== "income"
            && checkedValue(form, "FundingSource") === "drawer";

        var over = fromDrawer && amount > balance;
        warn.hidden = !over;
        if (over)
            warn.textContent = "المبلغ أكبر من رصيد الدرج الحالي (" + money(balance)
                + "). إذا دُفع من مال آخر، اختر «من خارج الدرج».";

        var submit = form.querySelector('button[type="submit"]');
        if (submit) submit.disabled = over;
    }

    document.querySelectorAll("form.js-expense-form").forEach(function (form) {
        form.addEventListener("change", function (e) {
            // On a new-entry form (data-default-source), choosing مصروف starts it on that
            // source again - otherwise the drawer that a draw or income forced stays
            // selected. Only on the type switch itself, so a source the user then picks
            // is kept.
            var fallback = form.getAttribute("data-default-source");
            if (fallback && e.target.name === "EntryType" && e.target.value === "expense")
                setChecked(form, "FundingSource", fallback);
            syncExpenseForm(form);
        });
        form.addEventListener("input", function () { checkDrawer(form); });
        syncExpenseForm(form);
    });

    // ── "Owed after saving" line (الموردون) ──────────────────────────────
    // data-current is what is owed now, data-sign +1 for an invoice and -1 for a return; the
    // figure updates as the amount is typed, so the direction of the change is visible
    // before the form is sent.
    function updatePreview(preview) {
        var form = preview.closest("form");
        var amountBox = form && form.querySelector('input[name="Amount"]');
        var amount = parseFloat(amountBox && amountBox.value) || 0;
        var after = (parseFloat(preview.getAttribute("data-current")) || 0)
            + amount * (parseFloat(preview.getAttribute("data-sign")) || 0);
        var target = preview.querySelector("[data-after]");
        // Below zero the supplier owes the shop, which a minus sign does not say clearly.
        if (target) target.textContent = after < 0 ? "لك عند المورد " + money(-after) : money(after);
    }

    document.querySelectorAll("[data-balance-preview]").forEach(function (preview) {
        var form = preview.closest("form");
        if (form) form.addEventListener("input", function () { updatePreview(preview); });
    });

    // ── Modals ─────────────────────────────────────────────────────────
    // <button data-modal-open="id" data-fields='{"paymentId":"5","label":"..."}'> opens the
    // modal and copies each field into the input of that name, or into the text of any
    // [data-text=name] element. Text is set with textContent, never as markup.
    var overlay = document.getElementById("exOverlay");

    function closeModals() {
        document.querySelectorAll(".modal.show").forEach(function (m) { m.classList.remove("show"); });
        if (overlay) overlay.classList.remove("show");
    }

    function openModal(modal, fields) {
        Object.keys(fields).forEach(function (key) {
            var value = fields[key] == null ? "" : String(fields[key]);
            modal.querySelectorAll('[name="' + key + '"]').forEach(function (input) {
                if (input.type === "radio") input.checked = input.value === value;
                else input.value = value;
            });
            modal.querySelectorAll('[data-text="' + key + '"]').forEach(function (el) { el.textContent = value; });
        });

        modal.querySelectorAll("[data-clear]").forEach(function (input) { input.value = ""; });
        modal.querySelectorAll("[data-balance-preview]").forEach(updatePreview);
        modal.classList.add("show");
        if (overlay) overlay.classList.add("show");

        var focus = modal.querySelector("[autofocus]") || modal.querySelector("input:not([type=hidden]):not([type=radio])");
        if (focus) focus.focus();
    }

    document.addEventListener("click", function (e) {
        var opener = e.target.closest("[data-modal-open]");
        if (opener) {
            var modal = document.getElementById(opener.getAttribute("data-modal-open"));
            if (modal) {
                e.preventDefault();
                openModal(modal, JSON.parse(opener.getAttribute("data-fields") || "{}"));
            }
            return;
        }

        if (e.target.closest("[data-modal-close]") || e.target === overlay)
            closeModals();
    });

    document.addEventListener("keydown", function (e) {
        if (e.key === "Escape") closeModals();
    });

    // A reason is what makes a correction auditable, so an empty one never leaves the page.
    document.querySelectorAll("form[data-require-reason]").forEach(function (form) {
        form.addEventListener("submit", function (e) {
            var box = form.querySelector('[name="reason"], [name="Reason"]');
            var warn = form.querySelector("[data-reason-warn]");
            if (box && !box.value.trim()) {
                e.preventDefault();
                if (warn) warn.hidden = false;
                box.focus();
            }
        });
    });
})();
