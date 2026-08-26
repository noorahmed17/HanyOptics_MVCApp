/* ============================================================
   HanyOptics — قارئ الباركود (USB / Bluetooth HID Scanner)

   القارئ بيتصرف زي كيبورد: بيكتب الكود بسرعة وبعدين يبعت Enter.
   الملف ده بيمسك الكتابة دي على مستوى الصفحة كلها ويحوّلها لسكان،
   وبيوجّهها لخانة الباركود المفتوحة مهما كانت.

   ليه الملف ده موجود أصلاً:
   لو الويندوز شغال على كيبورد عربي، القارئ بيبعت أزرار والويندوز
   بيترجمها حروف عربية — فبدل ما يتكتب 28D2300N55 بيتكتب كلام مكسّر.
   الحل إننا نقرا مكان الزرار نفسه (e.code) مش الحرف، فالنتيجة واحدة
   مهما كانت لغة الويندوز.
   ============================================================ */

(function () {
    "use strict";

    var CFG = {
        // القارئ بيبعت الحروف على بعد 5-15 مللي. الكتابة البشرية السريعة
        // بتوصل 80-150 مللي. 40 بتفصل بينهم بأمان.
        //
        // القيمة دي كانت 120 في النسخة الأصلية، وده كان بيخلّي الكتابة
        // السريعة تتحسب سكان فتتاكل حروف من خانات الأسماء العربية.
        // لو فيه قارئ بطيء مش بيتقرا، ارفعها بالتدريج وجرّب:
        //     HanyScanner.config.maxGapMs = 60
        maxGapMs: 40,
        minLength: 4,
        beep: true,
        toast: true,
        debug: false
    };

    // خانات الباركود في النظام. الشاشة ممكن يكون فيها أكتر من واحدة
    // (نموذج البند في الصفحة + نسخة تانية في بوب أب التعديل)، عشان كده
    // بندوّر على المفتوحة/المركّز عليها مش على id ثابت.
    var BARCODE_SELECTOR = '[data-role="barcodeInput"], #swapBarcodeInput';

    // ---------- خريطة الكيبورد العربي (احتياطية) ----------
    // الحرف العربي → الحرف الإنجليزي اللي على نفس الزرار
    var AR = {
        "ض": "q", "ص": "w", "ث": "e", "ق": "r", "ف": "t", "غ": "y", "ع": "u",
        "ه": "i", "خ": "o", "ح": "p", "ش": "a", "س": "s", "ي": "d", "ب": "f",
        "ل": "g", "ا": "h", "ت": "j", "ن": "k", "م": "l", "ئ": "z", "ء": "x",
        "ؤ": "c", "ر": "v", "لا": "b", "ى": "n", "ة": "m",
        "َ": "Q", "ً": "W", "ُ": "E", "ٌ": "R", "لإ": "T",
        "إ": "Y", "‘": "U", "÷": "I", "×": "O", "؛": "P", "ِ": "A",
        "ٍ": "S", "]": "D", "[": "F", "لأ": "G", "أ": "H", "ـ": "J",
        "،": "K", "/": "L", "~": "Z", "ْ": "X", "}": "C", "{": "V",
        "لآ": "B", "آ": "N", "’": "M"
    };

    var buf = [], lastAt = 0, synthetic = false;

    function isVisible(el) {
        return !!(el && el.offsetParent !== null);
    }

    // خانة الباركود اللي المفروض السكان يروح لها:
    // الأولوية للمركّز عليها، وبعدين أول واحدة ظاهرة على الشاشة.
    function currentTarget() {
        var active = document.activeElement;
        if (active && active.matches && active.matches(BARCODE_SELECTOR)) return active;

        var all = document.querySelectorAll(BARCODE_SELECTOR);
        for (var i = 0; i < all.length; i++) {
            if (isVisible(all[i])) return all[i];
        }
        return null;
    }

    // ---------- تحويل الزرار لحرف، مستقل عن لغة الويندوز ----------
    function charFrom(e) {
        var c = e.code || "";

        if (/^Key[A-Z]$/.test(c)) {
            var L = c.slice(3);
            return e.shiftKey ? L : L.toLowerCase();
        }
        if (/^Digit[0-9]$/.test(c) && !e.shiftKey) return c.slice(5);
        if (/^Numpad[0-9]$/.test(c)) return c.slice(6);
        if (c === "Minus" || c === "NumpadSubtract") return e.shiftKey ? "_" : "-";
        if (c === "Period" || c === "NumpadDecimal") return ".";

        var k = e.key;
        if (!k || k === "Unidentified") return null;
        if (/^[0-9A-Za-z]$/.test(k)) return k;
        if (Object.prototype.hasOwnProperty.call(AR, k)) return AR[k];
        if (/^[٠-٩]$/.test(k)) return String(k.charCodeAt(0) - 0x0660);
        return null;
    }

    // ---------- الماسك الرئيسي ----------
    document.addEventListener("keydown", function (e) {
        if (synthetic || e.ctrlKey || e.metaKey) return;

        var now = (window.performance && performance.now) ? performance.now() : Date.now();
        var gap = now - lastAt;
        var target = currentTarget();
        var inTarget = target && document.activeElement === target;

        if (CFG.debug) {
            console.log("[scan] key=%o code=%o shift=%o gap=%oms buf=%o",
                e.key, e.code, e.shiftKey, Math.round(gap), buf.join(""));
        }

        // Enter / Tab = نهاية السكان
        if (e.key === "Enter" || e.key === "Tab") {
            var code = buf.join("");
            buf = [];
            if (code.length >= CFG.minLength) {
                e.preventDefault();
                e.stopPropagation();
                handleScan(code);
            } else if (CFG.debug) {
                console.warn("[scan] الكود قصير أو ضاع:", JSON.stringify(code));
            }
            return;
        }

        var ch = charFrom(e);
        if (ch === null) {
            if (e.key !== "Shift" && e.key !== "CapsLock" && e.key !== "Alt") buf = [];
            return;
        }

        if (gap > CFG.maxGapMs) buf = [];   // بداية سلسلة جديدة
        buf.push(ch);
        lastAt = now;

        if (inTarget) {
            // بنكتب الحرف بنفسنا — بيتخطى مشكلة الكيبورد العربي
            e.preventDefault();
            target.value = buf.join("");
        } else if (buf.length >= 2 && gap <= CFG.maxGapMs) {
            // سكان بره الخانة — نبلعه عشان ميتكتبش في مكان غلط
            e.preventDefault();
            var el = document.activeElement;
            if (buf.length === 2 && el && typeof el.value === "string") {
                el.value = el.value.slice(0, Math.max(0, el.value.length - 1)); // شيل أول حرف فلت
            }
        }
    }, true);

    // ---------- تنفيذ السكان ----------
    function handleScan(code) {
        beep();
        toast("تم مسح الكود: " + code);
        if (CFG.debug) console.log("%c[scan] ✔ " + code, "color:#0e7c74;font-weight:bold");

        var target = currentTarget();
        if (target) {
            target.value = code;
            try { target.focus(); } catch (err) { /* ممكن يكون مخفي */ }

            // نبعت Enter صناعي عشان البحث يشتغل. الـ handler اللي في
            // order-item-form.js و searchSwapFrame بيمسكوه ويعملوا اللوك أب.
            // synthetic بيمنع الماسك اللي فوق إنه يحسب الـ Enter ده سكان جديد.
            synthetic = true;
            target.dispatchEvent(new KeyboardEvent("keydown", {
                key: "Enter", code: "Enter", bubbles: true, cancelable: true
            }));
            synthetic = false;
        }

        document.dispatchEvent(new CustomEvent("hany:scan", { detail: { code: code } }));
    }

    // ---------- صوت تأكيد ----------
    var audioCtx = null;
    function beep() {
        if (!CFG.beep) return;
        try {
            var Ctx = window.AudioContext || window.webkitAudioContext;
            if (!Ctx) return;
            audioCtx = audioCtx || new Ctx();
            var osc = audioCtx.createOscillator(), gain = audioCtx.createGain();
            osc.type = "sine";
            osc.frequency.value = 1180;
            gain.gain.setValueAtTime(0.09, audioCtx.currentTime);
            gain.gain.exponentialRampToValueAtTime(0.0001, audioCtx.currentTime + 0.11);
            osc.connect(gain).connect(audioCtx.destination);
            osc.start();
            osc.stop(audioCtx.currentTime + 0.12);
        } catch (err) { /* الصوت مش ضروري */ }
    }

    // ---------- إشعار ----------
    var toastEl = null, toastTimer = null;
    function toast(msg) {
        if (!CFG.toast) return;
        if (!toastEl) {
            toastEl = document.createElement("div");
            toastEl.setAttribute("role", "status");
            toastEl.style.cssText =
                "position:fixed;bottom:18px;left:50%;transform:translateX(-50%);" +
                "background:#0e7c74;color:#fff;padding:9px 18px;border-radius:999px;" +
                "font-family:inherit;font-size:14px;font-weight:600;z-index:9999;" +
                "box-shadow:0 6px 20px rgba(0,0,0,.22);opacity:0;transition:opacity .18s;" +
                "pointer-events:none;direction:rtl";
            document.body.appendChild(toastEl);
        }
        toastEl.textContent = msg;
        toastEl.style.opacity = "1";
        clearTimeout(toastTimer);
        toastTimer = setTimeout(function () { toastEl.style.opacity = "0"; }, 1600);
    }

    window.HanyScanner = {
        version: 3,
        config: CFG,
        test: handleScan,
        target: currentTarget,
        debug: function (on) {
            CFG.debug = (on !== false);
            console.log("[scan] وضع التشخيص " + (CFG.debug ? "مفتوح — اعمل سكان دلوقتي" : "مقفول"));
        }
    };

    console.log("[scan] HanyScanner v3 جاهز");
})();
